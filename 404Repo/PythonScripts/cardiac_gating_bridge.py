"""
Cardiac gating bridge for Python.NET integration with the BioMetrix C# framework.

This module wraps the cardiac_gating package (from FinalCode/), providing flat
functions that return plain dicts for clean marshalling across the Python.NET
boundary.

Usage from C#:
    dynamic bridge = Py.Import("cardiac_gating_bridge");
    dynamic result = bridge.run_cardiac_gating(csvPath, mrdFile, outputDir);
"""

import os
import sys


def _ensure_cardiac_gating_on_path():
    """Add the FinalCode directory to sys.path so the cardiac_gating package is importable."""
    final_code_dir = os.path.join(
        os.path.dirname(os.path.abspath(__file__)), os.pardir, "FinalCode"
    )
    final_code_dir = os.path.normpath(final_code_dir)
    if final_code_dir not in sys.path:
        sys.path.insert(0, final_code_dir)


def get_diagnostics():
    """Return a diagnostic string with Python runtime info for troubleshooting."""
    lines = [
        f"sys.executable: {sys.executable}",
        f"sys.prefix: {sys.prefix}",
        f"sys.base_prefix: {sys.base_prefix}",
        f"sys.version: {sys.version}",
        f"sys.platform: {sys.platform}",
        f"PYTHONHOME: {os.environ.get('PYTHONHOME', '<not set>')}",
        f"PYTHONPATH: {os.environ.get('PYTHONPATH', '<not set>')}",
        "sys.path:",
    ]
    for p in sys.path:
        lines.append(f"  - {p}")
    return "\n".join(lines)


def validate_inputs(csv_path, mrd_file):
    """
    Check that the cardiogram CSV and MRD file exist and are valid.

    Returns:
        dict with keys: valid, csv_exists, mrd_exists, error
    """
    try:
        csv_exists = os.path.isfile(csv_path)
        mrd_exists = os.path.isfile(mrd_file)

        errors = []
        if not csv_exists:
            errors.append(f"CSV file not found: {csv_path}")
        if not mrd_exists:
            errors.append(f"MRD file not found: {mrd_file}")

        return {
            "valid": csv_exists and mrd_exists,
            "csv_exists": csv_exists,
            "mrd_exists": mrd_exists,
            "error": "; ".join(errors) if errors else "",
        }
    except Exception as e:
        return {
            "valid": False,
            "csv_exists": False,
            "mrd_exists": False,
            "error": str(e),
        }


def run_cardiac_gating(csv_path, mrd_file, output_dir, config_path=None):
    """
    Run the full cardiac gating pipeline.

    Returns:
        dict with keys: success, accepted_frames, rejected_frames,
        stable_cycles, alignment_offset_ms, output_dir, error
    """
    try:
        _ensure_cardiac_gating_on_path()

        from cardiac_gating.config import ScanFilterConfig
        from cardiac_gating.pipeline import ScanFilterPipeline

        # Load configuration (falls back to defaults if pyyaml missing)
        cfg = None
        try:
            if config_path and os.path.isfile(config_path):
                cfg = ScanFilterConfig.load(config_path)
            else:
                default_cfg = os.path.join(
                    os.path.dirname(os.path.abspath(__file__)),
                    os.pardir, "FinalCode", "config", "default.yaml"
                )
                default_cfg = os.path.normpath(default_cfg)
                if os.path.isfile(default_cfg):
                    cfg = ScanFilterConfig.load(default_cfg)
        except ImportError:
            pass

        if cfg is None:
            cfg = ScanFilterConfig()

        pipeline = ScanFilterPipeline(cfg)
        artifacts = pipeline.run(csv_path, mrd_file, output_dir)

        return {
            "success": True,
            "accepted_frames": artifacts.accepted_frames,
            "rejected_frames": artifacts.rejected_frames,
            "stable_cycles": artifacts.stable_cycles,
            "alignment_offset_ms": float(artifacts.alignment_offset_ms),
            "output_dir": str(artifacts.output_dir),
            "error": "",
        }
    except Exception as e:
        # Include full diagnostics so the user can see which Python is
        # being used and what paths are configured.
        diag = get_diagnostics()
        return {
            "success": False,
            "accepted_frames": 0,
            "rejected_frames": 0,
            "stable_cycles": 0,
            "alignment_offset_ms": 0.0,
            "output_dir": str(output_dir) if output_dir else "",
            "error": f"{e}\n\n--- Python Diagnostics ---\n{diag}",
        }


if __name__ == "__main__":
    print("=== Cardiac Gating Bridge Self-Test ===")
    print(get_diagnostics())
    print()
    print(f"validate_inputs('nonexistent.csv', 'nonexistent.mrd'):")
    print(f"  {validate_inputs('nonexistent.csv', 'nonexistent.mrd')}")
