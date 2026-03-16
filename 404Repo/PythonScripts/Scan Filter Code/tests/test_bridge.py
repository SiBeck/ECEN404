"""Tests for scan_filter_bridge.py — the Python.NET bridge module."""

import os
import sys
import glob
import tempfile
from pathlib import Path

import pytest

# The bridge module lives one level up from "Scan Filter Code".
# Add its parent directory to sys.path so we can import it.
_scripts_dir = str(Path(__file__).resolve().parent.parent.parent)
if _scripts_dir not in sys.path:
    sys.path.insert(0, _scripts_dir)

from scan_filter_bridge import validate_inputs, run_scan_filter  # noqa: E402
from examples.create_sample_inputs import generate_cardiogram, generate_dicom_series  # noqa: E402


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture()
def sample_data(tmp_path):
    """Generate synthetic cardiogram CSV and DICOM series in a temp directory."""
    csv_path = tmp_path / "cardio.csv"
    dicom_dir = tmp_path / "dicom_series"
    generate_cardiogram(csv_path, beats=18, base_rr_ms=800.0)
    generate_dicom_series(dicom_dir, frame_count=12, start_ms=100.0, spacing_ms=200.0, seed=42)
    return csv_path, dicom_dir


@pytest.fixture()
def empty_dicom_folder(tmp_path):
    """Create an empty directory with no .dcm files."""
    folder = tmp_path / "empty_dicoms"
    folder.mkdir()
    # Put a non-DICOM file in it so the dir isn't empty on disk
    (folder / "readme.txt").write_text("no dicoms here")
    return folder


# ---------------------------------------------------------------------------
# validate_inputs tests
# ---------------------------------------------------------------------------

def test_validate_inputs_valid_data(sample_data):
    csv_path, dicom_dir = sample_data
    result = validate_inputs(str(csv_path), str(dicom_dir))

    assert result["valid"] is True
    assert result["csv_exists"] is True
    assert result["dicom_exists"] is True
    assert result["dicom_file_count"] == 12
    assert result["error"] == ""


def test_validate_inputs_missing_csv(sample_data):
    _, dicom_dir = sample_data
    result = validate_inputs("/nonexistent/missing.csv", str(dicom_dir))

    assert result["valid"] is False
    assert result["csv_exists"] is False
    assert result["dicom_exists"] is True
    assert "CSV" in result["error"] or "csv" in result["error"].lower()


def test_validate_inputs_missing_dicom_folder(sample_data):
    csv_path, _ = sample_data
    result = validate_inputs(str(csv_path), "/nonexistent/dicom_dir")

    assert result["valid"] is False
    assert result["dicom_exists"] is False
    assert result["csv_exists"] is True


def test_validate_inputs_empty_dicom_folder(sample_data, empty_dicom_folder):
    csv_path, _ = sample_data
    result = validate_inputs(str(csv_path), str(empty_dicom_folder))

    assert result["valid"] is False
    assert result["dicom_exists"] is True
    assert result["dicom_file_count"] == 0
    assert "No .dcm" in result["error"] or "dcm" in result["error"].lower()


# ---------------------------------------------------------------------------
# run_scan_filter tests
# ---------------------------------------------------------------------------

def test_run_scan_filter_happy_path(sample_data, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = str(tmp_path / "filtered_output")

    result = run_scan_filter(str(csv_path), str(dicom_dir), output_dir)

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert result["accepted_frames"] > 0
    assert result["error"] == ""
    assert os.path.isdir(result["output_dir"])


def test_run_scan_filter_frame_counts_sum(sample_data, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = str(tmp_path / "filtered_sum")

    result = run_scan_filter(str(csv_path), str(dicom_dir), output_dir)

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    total_input = 12  # generated with frame_count=12
    assert result["accepted_frames"] + result["rejected_frames"] == total_input


def test_run_scan_filter_output_files_match_count(sample_data, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = str(tmp_path / "filtered_files")

    result = run_scan_filter(str(csv_path), str(dicom_dir), output_dir)

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    dcm_files = glob.glob(os.path.join(result["output_dir"], "*.dcm"))
    assert len(dcm_files) == result["accepted_frames"]


def test_run_scan_filter_with_custom_config(sample_data, tmp_path):
    csv_path, dicom_dir = sample_data
    output_dir = str(tmp_path / "filtered_custom")

    # Use the default.yaml that ships with the package
    config_path = str(Path(__file__).resolve().parent.parent / "config" / "default.yaml")
    result = run_scan_filter(str(csv_path), str(dicom_dir), output_dir, config_path)

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert result["accepted_frames"] >= 0


def test_run_scan_filter_missing_csv_returns_error(sample_data, tmp_path):
    _, dicom_dir = sample_data
    output_dir = str(tmp_path / "filtered_err1")

    result = run_scan_filter("/nonexistent/bad.csv", str(dicom_dir), output_dir)

    assert result["success"] is False
    assert result["error"] != ""


def test_run_scan_filter_empty_folder_returns_error(sample_data, empty_dicom_folder, tmp_path):
    csv_path, _ = sample_data
    output_dir = str(tmp_path / "filtered_err2")

    result = run_scan_filter(str(csv_path), str(empty_dicom_folder), output_dir)

    assert result["success"] is False
    assert result["error"] != ""
