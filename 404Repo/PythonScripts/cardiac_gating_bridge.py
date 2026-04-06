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

    # When Python is embedded via Python.NET, installed packages (pyyaml,
    # numpy, pydicom) may not be on sys.path. Explicitly add site-packages
    # from the Python installation prefix and any user site-packages.
    _add_site_packages()

    # Also add venv site-packages if present inside FinalCode.
    venv_dir = os.path.join(final_code_dir, ".venv")
    if os.path.isdir(venv_dir):
        if sys.platform == "win32":
            sp = os.path.join(venv_dir, "Lib", "site-packages")
        else:
            ver = f"{sys.version_info.major}.{sys.version_info.minor}"
            sp = os.path.join(venv_dir, "lib", f"python{ver}", "site-packages")
        if os.path.isdir(sp) and sp not in sys.path:
            sys.path.insert(0, sp)


def _add_site_packages():
    """Explicitly add site-packages directories to sys.path for embedded Python."""
    ver = f"{sys.version_info.major}.{sys.version_info.minor}"
    candidates = []

    if sys.platform == "win32":
        # Standard Python on Windows: <prefix>\Lib\site-packages
        candidates.append(os.path.join(sys.prefix, "Lib", "site-packages"))
        # Per-user installs on Windows
        appdata = os.environ.get("APPDATA", "")
        if appdata:
            candidates.append(os.path.join(appdata, "Python", f"Python{sys.version_info.major}{sys.version_info.minor}", "site-packages"))
    else:
        candidates.append(os.path.join(sys.prefix, "lib", f"python{ver}", "site-packages"))

    # Also check sys.base_prefix (differs from sys.prefix inside a venv)
    if sys.base_prefix != sys.prefix:
        if sys.platform == "win32":
            candidates.append(os.path.join(sys.base_prefix, "Lib", "site-packages"))
        else:
            candidates.append(os.path.join(sys.base_prefix, "lib", f"python{ver}", "site-packages"))

    for sp in candidates:
        if os.path.isdir(sp) and sp not in sys.path:
            sys.path.insert(0, sp)


def validate_inputs(csv_path, mrd_file):
    """
    Check that the cardiogram CSV and MRD file exist and are valid.

    Args:
        csv_path: Path to the cardiogram CSV file.
        mrd_file: Path to the MRD scan file.

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

    Args:
        csv_path: Path to the cardiogram CSV file.
        mrd_file: Path to the MRD scan file.
        output_dir: Destination folder for gated DICOM frames.
        config_path: Optional path to a YAML configuration file.
                     If None, uses the default config shipped with FinalCode.

    Returns:
        dict with keys: success, accepted_frames, rejected_frames,
        stable_cycles, alignment_offset_ms, output_dir, error
    """
    try:
        _ensure_cardiac_gating_on_path()

        from cardiac_gating.config import ScanFilterConfig
        from cardiac_gating.pipeline import ScanFilterPipeline

        # Load configuration
        if config_path and os.path.isfile(config_path):
            cfg = ScanFilterConfig.load(config_path)
        else:
            # Use default config from the FinalCode directory
            default_cfg = os.path.join(
                os.path.dirname(os.path.abspath(__file__)),
                os.pardir, "FinalCode", "config", "default.yaml"
            )
            default_cfg = os.path.normpath(default_cfg)
            if os.path.isfile(default_cfg):
                cfg = ScanFilterConfig.load(default_cfg)
            else:
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
        return {
            "success": False,
            "accepted_frames": 0,
            "rejected_frames": 0,
            "stable_cycles": 0,
            "alignment_offset_ms": 0.0,
            "output_dir": str(output_dir) if output_dir else "",
            "error": str(e),
        }


if __name__ == "__main__":
    print("=== Cardiac Gating Bridge Self-Test ===")
    print(f"validate_inputs('nonexistent.csv', 'nonexistent.mrd'):")
    print(f"  {validate_inputs('nonexistent.csv', 'nonexistent.mrd')}")
