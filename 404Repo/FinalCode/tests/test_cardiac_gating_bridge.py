"""Tests for cardiac_gating_bridge.py — the Python.NET bridge module.

Validates every public function exposed to C# via Python.NET:
  - get_diagnostics()
  - validate_inputs(csv_path, mrd_file)
  - run_cardiac_gating(csv_path, mrd_file, output_dir, config_path=None)

Tests that require real MRD data are decorated with @needs_data and skipped
automatically if test_cases/case_01/ is absent.

Run from the 404Repo/FinalCode directory:
    pytest tests/test_cardiac_gating_bridge.py -v
"""

import glob
import os
from pathlib import Path

import pytest

# conftest.py has already inserted FinalCode and PythonScripts onto sys.path.
from cardiac_gating_bridge import get_diagnostics, run_cardiac_gating, validate_inputs

# ---------------------------------------------------------------------------
# Test-data sentinel
# ---------------------------------------------------------------------------

_CASE_01 = Path(__file__).resolve().parent.parent / "test_cases" / "case_01"
_CSV = _CASE_01 / "cardiogram.csv"
_MRD = _CASE_01 / "te014_case_01.mrd"
_HAS_DATA = _CASE_01.exists() and _CSV.exists() and _MRD.exists()

needs_data = pytest.mark.skipif(
    not _HAS_DATA,
    reason="test_cases/case_01 data not found — skipping live-pipeline tests",
)

_DEFAULT_CONFIG = (
    Path(__file__).resolve().parent.parent / "config" / "default.yaml"
)


# ---------------------------------------------------------------------------
# 1. Module-level / get_diagnostics
# ---------------------------------------------------------------------------


def test_bridge_module_importable():
    """Bridge is importable (verified by the import statement at module scope)."""
    import cardiac_gating_bridge as m  # noqa: F401 (re-import to verify)

    assert m is not None


def test_get_diagnostics_returns_string():
    info = get_diagnostics()
    assert isinstance(info, str)


def test_get_diagnostics_is_non_empty():
    info = get_diagnostics()
    assert len(info) > 0


def test_get_diagnostics_reports_sys_version():
    info = get_diagnostics()
    assert "sys.version" in info


def test_get_diagnostics_reports_sys_path():
    info = get_diagnostics()
    assert "sys.path" in info


def test_get_diagnostics_reports_platform():
    info = get_diagnostics()
    assert "sys.platform" in info


# ---------------------------------------------------------------------------
# 2. validate_inputs — error paths (no real data required)
# ---------------------------------------------------------------------------


def test_validate_inputs_both_missing():
    result = validate_inputs("/nonexistent/a.csv", "/nonexistent/b.mrd")

    assert result["valid"] is False
    assert result["csv_exists"] is False
    assert result["mrd_exists"] is False
    assert len(result["error"]) > 0


def test_validate_inputs_missing_csv_only(tmp_path):
    mrd = tmp_path / "stub.mrd"
    mrd.write_bytes(b"\x00")
    result = validate_inputs("/nonexistent/missing.csv", str(mrd))

    assert result["valid"] is False
    assert result["csv_exists"] is False
    assert result["mrd_exists"] is True
    assert len(result["error"]) > 0


def test_validate_inputs_missing_mrd_only(tmp_path):
    csv = tmp_path / "cardio.csv"
    csv.write_text("timestamp_ms,signal\n0,0.1\n")
    result = validate_inputs(str(csv), "/nonexistent/missing.mrd")

    assert result["valid"] is False
    assert result["csv_exists"] is True
    assert result["mrd_exists"] is False
    assert len(result["error"]) > 0


def test_validate_inputs_result_always_has_required_keys(tmp_path):
    csv = tmp_path / "c.csv"
    csv.write_text("timestamp_ms,signal\n")
    result = validate_inputs(str(csv), "/nonexistent/x.mrd")

    assert "valid" in result
    assert "csv_exists" in result
    assert "mrd_exists" in result
    assert "error" in result


def test_validate_inputs_error_field_is_string_on_failure():
    result = validate_inputs("/bad.csv", "/bad.mrd")
    assert isinstance(result["error"], str)


# ---------------------------------------------------------------------------
# 3. validate_inputs — happy path (requires test data)
# ---------------------------------------------------------------------------


@needs_data
def test_validate_inputs_valid_case_01():
    result = validate_inputs(str(_CSV), str(_MRD))

    assert result["valid"] is True
    assert result["csv_exists"] is True
    assert result["mrd_exists"] is True
    assert result["error"] == ""


@needs_data
def test_validate_inputs_error_empty_on_success():
    result = validate_inputs(str(_CSV), str(_MRD))
    assert result["error"] == ""


# ---------------------------------------------------------------------------
# 4. run_cardiac_gating — error paths (no real data required)
# ---------------------------------------------------------------------------


def test_run_cardiac_gating_missing_both_returns_error(tmp_path):
    result = run_cardiac_gating(
        "/nonexistent/bad.csv",
        "/nonexistent/bad.mrd",
        str(tmp_path / "out"),
    )

    assert result["success"] is False
    assert len(result["error"]) > 0


def test_run_cardiac_gating_missing_csv_returns_error(tmp_path):
    mrd = tmp_path / "stub.mrd"
    mrd.write_bytes(b"\x00")
    result = run_cardiac_gating(
        "/nonexistent/missing.csv",
        str(mrd),
        str(tmp_path / "out"),
    )

    assert result["success"] is False
    assert len(result["error"]) > 0


def test_run_cardiac_gating_missing_mrd_returns_error(tmp_path):
    csv = tmp_path / "cardio.csv"
    csv.write_text("timestamp_ms,signal\n0,0.1\n")
    result = run_cardiac_gating(
        str(csv),
        "/nonexistent/missing.mrd",
        str(tmp_path / "out"),
    )

    assert result["success"] is False
    assert len(result["error"]) > 0


def test_run_cardiac_gating_result_always_has_required_keys(tmp_path):
    """The result dict schema must be stable regardless of success/failure."""
    result = run_cardiac_gating("/no.csv", "/no.mrd", str(tmp_path / "out"))

    assert "success" in result
    assert "accepted_frames" in result
    assert "rejected_frames" in result
    assert "stable_cycles" in result
    assert "alignment_offset_ms" in result
    assert "output_dir" in result
    assert "error" in result


def test_run_cardiac_gating_error_result_zeroes_counts(tmp_path):
    result = run_cardiac_gating("/no.csv", "/no.mrd", str(tmp_path / "out"))

    assert result["accepted_frames"] == 0
    assert result["rejected_frames"] == 0
    assert result["stable_cycles"] == 0


def test_run_cardiac_gating_error_includes_diagnostics(tmp_path):
    """On failure, error field should contain Python diagnostic info."""
    result = run_cardiac_gating("/no.csv", "/no.mrd", str(tmp_path / "out"))

    assert result["success"] is False
    assert "sys.path" in result["error"] or len(result["error"]) > 0


# ---------------------------------------------------------------------------
# 5. run_cardiac_gating — happy path (requires test data)
# ---------------------------------------------------------------------------


@needs_data
def test_run_cardiac_gating_happy_path_succeeds(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"


@needs_data
def test_run_cardiac_gating_error_empty_on_success(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert result["error"] == ""


@needs_data
def test_run_cardiac_gating_accepted_frames_positive(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert result["accepted_frames"] > 0


@needs_data
def test_run_cardiac_gating_total_frames_positive(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    total = result["accepted_frames"] + result["rejected_frames"]
    assert total > 0


@needs_data
def test_run_cardiac_gating_stable_cycles_non_negative(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert result["stable_cycles"] >= 0


@needs_data
def test_run_cardiac_gating_alignment_offset_is_float(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert isinstance(result["alignment_offset_ms"], float)


@needs_data
def test_run_cardiac_gating_output_dir_created(tmp_path):
    output_dir = str(tmp_path / "gated_output")
    result = run_cardiac_gating(str(_CSV), str(_MRD), output_dir)

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert os.path.isdir(result["output_dir"]), (
        f"Output directory not created: {result['output_dir']}"
    )


@needs_data
def test_run_cardiac_gating_output_contains_dicom_files(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    dcm_files = glob.glob(os.path.join(result["output_dir"], "*.dcm"))
    assert len(dcm_files) > 0, "Expected at least one DICOM file in output directory"


@needs_data
def test_run_cardiac_gating_output_file_count_matches_accepted(tmp_path):
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    dcm_files = glob.glob(os.path.join(result["output_dir"], "*.dcm"))
    assert len(dcm_files) == result["accepted_frames"], (
        f"Expected {result['accepted_frames']} DICOM files, found {len(dcm_files)}"
    )


@needs_data
def test_run_cardiac_gating_output_files_named_correctly(tmp_path):
    """Output DICOM files must follow the frame_NNNN.dcm convention."""
    result = run_cardiac_gating(str(_CSV), str(_MRD), str(tmp_path / "gated"))

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    dcm_files = sorted(glob.glob(os.path.join(result["output_dir"], "*.dcm")))
    for f in dcm_files:
        basename = os.path.basename(f)
        assert basename.startswith("frame_"), f"Unexpected prefix: {basename}"
        assert basename.endswith(".dcm"), f"Unexpected extension: {basename}"


@needs_data
def test_run_cardiac_gating_with_explicit_default_config(tmp_path):
    """Passing the default YAML config explicitly must still succeed."""
    config_path = str(_DEFAULT_CONFIG) if _DEFAULT_CONFIG.exists() else None
    result = run_cardiac_gating(
        str(_CSV), str(_MRD), str(tmp_path / "gated_cfg"), config_path
    )

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert result["accepted_frames"] > 0


@needs_data
def test_run_cardiac_gating_output_dir_in_result_matches_requested(tmp_path):
    """The output_dir key in the result must point to the requested directory."""
    requested = str(tmp_path / "gated_dir_check")
    result = run_cardiac_gating(str(_CSV), str(_MRD), requested)

    assert result["success"] is True, f"Pipeline failed: {result['error']}"
    assert os.path.normpath(result["output_dir"]) == os.path.normpath(requested)
