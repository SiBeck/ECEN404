"""End-to-end integration tests for the ScanFilterPipeline."""

import glob
import os
from pathlib import Path

import pytest

from scan_filter.config import (
    AlignmentConfig,
    CycleDetectionConfig,
    FilteringConfig,
    IOConfig,
    ScanFilterConfig,
)
from scan_filter.core.pipeline import ScanFilterPipeline
from examples.create_sample_inputs import generate_cardiogram, generate_dicom_series


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture()
def sample_data(tmp_path):
    """Generate standard synthetic test data."""
    csv_path = tmp_path / "cardio.csv"
    dicom_dir = tmp_path / "dicom_series"
    generate_cardiogram(csv_path, beats=18, base_rr_ms=800.0)
    generate_dicom_series(dicom_dir, frame_count=12, start_ms=100.0, spacing_ms=200.0, seed=42)
    return csv_path, dicom_dir


@pytest.fixture()
def default_config():
    """Load the default pipeline configuration."""
    config_path = Path(__file__).resolve().parent.parent / "config" / "default.yaml"
    if config_path.exists():
        return ScanFilterConfig.load(config_path)
    return ScanFilterConfig()


@pytest.fixture()
def strict_config():
    """Configuration with very strict filtering to test fallback behavior."""
    return ScanFilterConfig(
        alignment=AlignmentConfig(max_offset_ms=5000.0),
        cycle_detection=CycleDetectionConfig(
            window_ms=400.0,
            min_peak_prominence=0.3,
            stable_rr_variation_pct=0.1,  # very strict — almost nothing is stable
            min_rr_ms=300.0,
            max_rr_ms=2000.0,
        ),
        filtering=FilteringConfig(
            stable_phase_fraction=0.05,  # very narrow window
            stable_phase_offset=0.5,
            fallback_min_frames=3,
        ),
        io=IOConfig(default_output_dir="output/filtered_series"),
    )


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

def test_pipeline_end_to_end(sample_data, default_config, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = tmp_path / "output"

    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(str(csv_path), str(dicom_dir), str(output_dir))

    assert artifacts.accepted_frames > 0
    assert artifacts.rejected_frames >= 0
    assert artifacts.stable_cycles >= 0
    assert artifacts.output_dir == output_dir


def test_pipeline_writes_output_files(sample_data, default_config, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = tmp_path / "output_files"

    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(str(csv_path), str(dicom_dir), str(output_dir))

    dcm_files = sorted(glob.glob(str(output_dir / "*.dcm")))
    assert len(dcm_files) == artifacts.accepted_frames
    # Verify naming convention: frame_NNNN.dcm
    for f in dcm_files:
        basename = os.path.basename(f)
        assert basename.startswith("frame_")
        assert basename.endswith(".dcm")


def test_pipeline_all_frames_accounted_for(sample_data, default_config, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = tmp_path / "output_sum"

    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(str(csv_path), str(dicom_dir), str(output_dir))

    assert artifacts.accepted_frames + artifacts.rejected_frames == 12


def test_pipeline_empty_cardiogram_raises(tmp_path, default_config):
    # Create a CSV with headers but no data rows
    csv_path = tmp_path / "empty_cardio.csv"
    csv_path.write_text("timestamp_ms,signal\n")
    dicom_dir = tmp_path / "dicom_series"
    generate_dicom_series(dicom_dir, frame_count=5, seed=42)
    output_dir = tmp_path / "output_empty_csv"

    pipeline = ScanFilterPipeline(default_config)
    with pytest.raises((ValueError, Exception)):
        pipeline.run(str(csv_path), str(dicom_dir), str(output_dir))


def test_pipeline_no_dicom_files_raises(sample_data, default_config, tmp_path):
    csv_path, _ = sample_data
    empty_dir = tmp_path / "no_dicoms"
    empty_dir.mkdir()
    output_dir = tmp_path / "output_no_dicom"

    pipeline = ScanFilterPipeline(default_config)
    with pytest.raises((ValueError, FileNotFoundError, Exception)):
        pipeline.run(str(csv_path), str(empty_dir), str(output_dir))


def test_pipeline_fallback_minimum_frames(sample_data, strict_config, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = tmp_path / "output_fallback"

    pipeline = ScanFilterPipeline(strict_config)
    artifacts = pipeline.run(str(csv_path), str(dicom_dir), str(output_dir))

    # Fallback should ensure at least fallback_min_frames are output
    assert artifacts.accepted_frames >= strict_config.filtering.fallback_min_frames


def test_pipeline_custom_config_values(sample_data, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = tmp_path / "output_custom"

    config = ScanFilterConfig(
        alignment=AlignmentConfig(max_offset_ms=3000.0),
        cycle_detection=CycleDetectionConfig(
            window_ms=600.0,
            min_peak_prominence=0.4,
            stable_rr_variation_pct=10.0,
            min_rr_ms=400.0,
            max_rr_ms=1500.0,
        ),
        filtering=FilteringConfig(
            stable_phase_fraction=0.5,
            stable_phase_offset=0.2,
            fallback_min_frames=1,
        ),
        io=IOConfig(default_output_dir="output/filtered_series"),
    )

    pipeline = ScanFilterPipeline(config)
    artifacts = pipeline.run(str(csv_path), str(dicom_dir), str(output_dir))

    assert artifacts.accepted_frames + artifacts.rejected_frames == 12
    assert artifacts.accepted_frames >= 1
