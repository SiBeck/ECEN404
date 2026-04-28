"""Unit and integration tests for the cardiac_gating package (FinalCode).

Coverage:
  - ScanFilterConfig  (construction, YAML load, field defaults)
  - CardiogramCsvReader  (reads cardiogram.csv → CardioSeries)
  - MRDSolutionsReader   (reads .mrd file → MRIFrameSeries)
  - ScanFilterPipeline   (end-to-end run on real test cases)
  - DicomSeriesWriter    (output files are valid DICOM)

Tests that need the real .mrd test data are guarded by @needs_case01 /
@needs_case02 markers and skipped automatically when absent.

Run from 404Repo/FinalCode:
    pytest tests/test_cardiac_gating_pipeline.py -v
"""

import glob
import os
from pathlib import Path

import numpy as np
import pytest

# conftest.py has already inserted FinalCode onto sys.path.
from cardiac_gating.config import (
    AlignmentConfig,
    CycleDetectionConfig,
    FilteringConfig,
    IOConfig,
    ScanFilterConfig,
)
from cardiac_gating.models import CardioSeries, MRIFrameSeries
from cardiac_gating.pipeline import ScanFilterPipeline
from cardiac_gating.readers import CardiogramCsvReader, MRDSolutionsReader

# ---------------------------------------------------------------------------
# Paths and skip markers
# ---------------------------------------------------------------------------

_CASES = Path(__file__).resolve().parent.parent / "test_cases"
_C01 = _CASES / "case_01"
_C02 = _CASES / "case_02"
_C03 = _CASES / "case_03"

needs_case01 = pytest.mark.skipif(
    not (_C01 / "cardiogram.csv").exists() or not (_C01 / "te014_case_01.mrd").exists(),
    reason="test_cases/case_01 not found",
)
needs_case02 = pytest.mark.skipif(
    not (_C02 / "cardiogram.csv").exists() or not (_C02 / "te014_case_02.mrd").exists(),
    reason="test_cases/case_02 not found",
)
needs_case03 = pytest.mark.skipif(
    not (_C03 / "cardiogram.csv").exists() or not (_C03 / "te014_case_03.mrd").exists(),
    reason="test_cases/case_03 not found",
)

# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture(scope="module")
def default_config():
    yaml_path = Path(__file__).resolve().parent.parent / "config" / "default.yaml"
    if yaml_path.exists():
        return ScanFilterConfig.load(str(yaml_path))
    return ScanFilterConfig()


@pytest.fixture(scope="module")
def case01_cardio():
    return CardiogramCsvReader().read(str(_C01 / "cardiogram.csv"))


@pytest.fixture(scope="module")
def case01_frames():
    return MRDSolutionsReader().read(str(_C01 / "te014_case_01.mrd"))


# ---------------------------------------------------------------------------
# 1. ScanFilterConfig
# ---------------------------------------------------------------------------


def test_default_config_constructs_without_arguments():
    cfg = ScanFilterConfig()
    assert cfg is not None


def test_default_config_cycle_detection_window_positive():
    cfg = ScanFilterConfig()
    assert cfg.cycle_detection.window_ms > 0


def test_default_config_stable_phase_fraction_in_range():
    cfg = ScanFilterConfig()
    assert 0 < cfg.filtering.stable_phase_fraction <= 1.0


def test_default_config_fallback_min_frames_positive():
    cfg = ScanFilterConfig()
    assert cfg.filtering.fallback_min_frames >= 1


def test_config_load_from_yaml_succeeds():
    yaml_path = Path(__file__).resolve().parent.parent / "config" / "default.yaml"
    if not yaml_path.exists():
        pytest.skip("default.yaml not found")
    cfg = ScanFilterConfig.load(str(yaml_path))
    assert cfg.cycle_detection.window_ms > 0


def test_config_yaml_values_match_expected_defaults():
    yaml_path = Path(__file__).resolve().parent.parent / "config" / "default.yaml"
    if not yaml_path.exists():
        pytest.skip("default.yaml not found")
    cfg = ScanFilterConfig.load(str(yaml_path))
    # Values from default.yaml as committed in the repository
    assert cfg.cycle_detection.window_ms == pytest.approx(900.0)
    assert cfg.filtering.stable_phase_fraction == pytest.approx(0.35)
    assert cfg.filtering.fallback_min_frames == 2


def test_config_accepts_custom_values():
    cfg = ScanFilterConfig(
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
        io=IOConfig(mrd_frame_period_ms=50.0),
    )
    assert cfg.alignment.max_offset_ms == pytest.approx(3000.0)
    assert cfg.cycle_detection.window_ms == pytest.approx(600.0)
    assert cfg.filtering.fallback_min_frames == 1
    assert cfg.io.mrd_frame_period_ms == pytest.approx(50.0)


# ---------------------------------------------------------------------------
# 2. CardiogramCsvReader
# ---------------------------------------------------------------------------


@needs_case01
def test_cardio_reader_returns_cardio_series(case01_cardio):
    assert isinstance(case01_cardio, CardioSeries)


@needs_case01
def test_cardio_reader_samples_non_empty(case01_cardio):
    assert len(case01_cardio.samples) > 0


@needs_case01
def test_cardio_reader_timestamps_property_is_array(case01_cardio):
    ts = case01_cardio.timestamps
    assert isinstance(ts, np.ndarray)
    assert ts.ndim == 1


@needs_case01
def test_cardio_reader_timestamps_monotonically_increasing(case01_cardio):
    ts = case01_cardio.timestamps
    assert (ts[1:] > ts[:-1]).all(), "Cardiogram timestamps are not monotonically increasing"


@needs_case01
def test_cardio_reader_signal_values_finite(case01_cardio):
    vals = case01_cardio.values
    assert np.all(np.isfinite(vals)), "Cardiogram contains non-finite signal values"


@needs_case01
def test_cardio_reader_sample_period_estimate_positive(case01_cardio):
    period = case01_cardio.estimate_sample_period_ms()
    assert period > 0


@needs_case01
def test_cardio_reader_sample_period_plausible(case01_cardio):
    """Sample period should be a few milliseconds (physiologically plausible)."""
    period = case01_cardio.estimate_sample_period_ms()
    assert 0.1 <= period <= 100.0, f"Sample period {period} ms is implausible"


# ---------------------------------------------------------------------------
# 3. MRDSolutionsReader
# ---------------------------------------------------------------------------


@needs_case01
def test_mrd_reader_returns_frame_series(case01_frames):
    assert isinstance(case01_frames, MRIFrameSeries)


@needs_case01
def test_mrd_reader_frames_non_empty(case01_frames):
    assert len(case01_frames.frames) > 0


@needs_case01
def test_mrd_reader_frame_ids_unique(case01_frames):
    ids = [f.frame_id for f in case01_frames.frames]
    assert len(ids) == len(set(ids)), "Duplicate frame IDs found"


@needs_case01
def test_mrd_reader_timestamps_non_negative(case01_frames):
    for f in case01_frames.frames:
        assert f.timestamp_ms >= 0, f"Frame {f.frame_id} has negative timestamp {f.timestamp_ms}"


@needs_case01
def test_mrd_reader_kspace_data_is_ndarray(case01_frames):
    for f in case01_frames.frames[:3]:
        assert isinstance(f.dataset, np.ndarray), f"Frame {f.frame_id} dataset is not ndarray"


@needs_case01
def test_mrd_reader_kspace_data_non_empty(case01_frames):
    for f in case01_frames.frames[:3]:
        assert f.dataset.size > 0, f"Frame {f.frame_id} has empty k-space"


@needs_case01
def test_mrd_reader_kspace_data_2d_or_higher(case01_frames):
    for f in case01_frames.frames[:3]:
        assert f.dataset.ndim >= 2, f"Frame {f.frame_id} k-space has fewer than 2 dimensions"


@needs_case01
def test_mrd_reader_alignment_offset_starts_at_zero(case01_frames):
    assert case01_frames.alignment_offset_ms == pytest.approx(0.0)


# ---------------------------------------------------------------------------
# 4. ScanFilterPipeline — end-to-end with case_01
# ---------------------------------------------------------------------------


@needs_case01
def test_pipeline_case01_returns_artifacts(tmp_path, default_config):
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(tmp_path / "out"),
    )
    assert artifacts is not None


@needs_case01
def test_pipeline_case01_accepted_frames_positive(tmp_path, default_config):
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(tmp_path / "out"),
    )
    assert artifacts.accepted_frames > 0


@needs_case01
def test_pipeline_case01_total_frames_positive(tmp_path, default_config):
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(tmp_path / "out"),
    )
    total = artifacts.accepted_frames + artifacts.rejected_frames
    assert total > 0


@needs_case01
def test_pipeline_case01_stable_cycles_non_negative(tmp_path, default_config):
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(tmp_path / "out"),
    )
    assert artifacts.stable_cycles >= 0


@needs_case01
def test_pipeline_case01_output_dir_created(tmp_path, default_config):
    out = tmp_path / "pipeline_out"
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(out),
    )
    assert os.path.isdir(str(artifacts.output_dir)), (
        f"Expected output directory to exist: {artifacts.output_dir}"
    )


@needs_case01
def test_pipeline_case01_dcm_file_count_matches_accepted(tmp_path, default_config):
    out = tmp_path / "pipeline_count"
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(out),
    )
    dcm_files = glob.glob(os.path.join(str(artifacts.output_dir), "*.dcm"))
    assert len(dcm_files) == artifacts.accepted_frames, (
        f"DICOM file count {len(dcm_files)} != accepted_frames {artifacts.accepted_frames}"
    )


@needs_case01
def test_pipeline_case01_output_files_named_correctly(tmp_path, default_config):
    out = tmp_path / "pipeline_names"
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(out),
    )
    dcm_files = sorted(glob.glob(os.path.join(str(artifacts.output_dir), "*.dcm")))
    for f in dcm_files:
        basename = os.path.basename(f)
        assert basename.startswith("frame_"), f"Unexpected file name: {basename}"
        assert basename.endswith(".dcm")


@needs_case01
def test_pipeline_case01_output_files_are_valid_dicom(tmp_path, default_config):
    """pydicom must be able to open every output file."""
    import pydicom

    out = tmp_path / "pipeline_valid"
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(out),
    )
    dcm_files = sorted(glob.glob(os.path.join(str(artifacts.output_dir), "*.dcm")))
    assert len(dcm_files) > 0, "No DICOM files produced"
    for dcm_path in dcm_files:
        ds = pydicom.dcmread(dcm_path)
        assert hasattr(ds, "Modality"), f"Missing Modality tag: {dcm_path}"
        assert hasattr(ds, "PixelData"), f"Missing PixelData: {dcm_path}"


@needs_case01
def test_pipeline_case01_dicom_modality_is_mr(tmp_path, default_config):
    import pydicom

    out = tmp_path / "pipeline_modality"
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(out),
    )
    dcm_files = sorted(glob.glob(os.path.join(str(artifacts.output_dir), "*.dcm")))
    for dcm_path in dcm_files[:3]:
        ds = pydicom.dcmread(dcm_path)
        assert ds.Modality == "MR", f"Expected Modality=MR, got {ds.Modality}"


@needs_case01
def test_pipeline_case01_dicom_has_trigger_time(tmp_path, default_config):
    """TriggerTime tag encodes the frame timestamp for cardiac phase reference."""
    import pydicom

    out = tmp_path / "pipeline_trigger"
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(out),
    )
    dcm_files = sorted(glob.glob(os.path.join(str(artifacts.output_dir), "*.dcm")))
    for dcm_path in dcm_files[:3]:
        ds = pydicom.dcmread(dcm_path)
        assert hasattr(ds, "TriggerTime"), f"Missing TriggerTime tag: {dcm_path}"


@needs_case01
def test_pipeline_case01_with_explicit_yaml_config(tmp_path):
    yaml_path = Path(__file__).resolve().parent.parent / "config" / "default.yaml"
    if not yaml_path.exists():
        pytest.skip("default.yaml not found")
    cfg = ScanFilterConfig.load(str(yaml_path))
    pipeline = ScanFilterPipeline(cfg)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(tmp_path / "out_yaml"),
    )
    assert artifacts.accepted_frames > 0


# ---------------------------------------------------------------------------
# 5. ScanFilterPipeline — cross-case reproducibility
# ---------------------------------------------------------------------------


@needs_case02
def test_pipeline_case02_succeeds(tmp_path, default_config):
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C02 / "cardiogram.csv"),
        str(_C02 / "te014_case_02.mrd"),
        str(tmp_path / "case02_out"),
    )
    assert artifacts.accepted_frames > 0


@needs_case03
def test_pipeline_case03_succeeds(tmp_path, default_config):
    pipeline = ScanFilterPipeline(default_config)
    artifacts = pipeline.run(
        str(_C03 / "cardiogram.csv"),
        str(_C03 / "te014_case_03.mrd"),
        str(tmp_path / "case03_out"),
    )
    assert artifacts.accepted_frames > 0


# ---------------------------------------------------------------------------
# 6. Fallback behaviour under strict config
# ---------------------------------------------------------------------------


@needs_case01
def test_pipeline_strict_config_respects_fallback_min(tmp_path):
    """With near-impossible RR variation threshold the fallback must activate."""
    strict = ScanFilterConfig(
        alignment=AlignmentConfig(max_offset_ms=5000.0),
        cycle_detection=CycleDetectionConfig(
            window_ms=400.0,
            min_peak_prominence=0.3,
            stable_rr_variation_pct=0.01,  # practically no cycle qualifies
            min_rr_ms=300.0,
            max_rr_ms=2000.0,
        ),
        filtering=FilteringConfig(
            stable_phase_fraction=0.01,
            stable_phase_offset=0.5,
            fallback_min_frames=2,
        ),
        io=IOConfig(),
    )
    pipeline = ScanFilterPipeline(strict)
    artifacts = pipeline.run(
        str(_C01 / "cardiogram.csv"),
        str(_C01 / "te014_case_01.mrd"),
        str(tmp_path / "strict_out"),
    )
    assert artifacts.accepted_frames >= strict.filtering.fallback_min_frames, (
        f"Fallback should guarantee at least {strict.filtering.fallback_min_frames} frames"
    )
