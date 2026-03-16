"""
Scan filter bridge for Python.NET integration with the BioMetrix C# framework.

This module wraps the scan_filter package, providing flat functions that return
plain dicts for clean marshalling across the Python.NET boundary.

Usage from C#:
    dynamic bridge = Py.Import("scan_filter_bridge");
    dynamic result = bridge.run_scan_filter(csvPath, dicomFolder, outputDir);
"""

import os
import sys
import glob
from pathlib import Path


def _ensure_scan_filter_on_path():
    """Add the Scan Filter Code directory to sys.path so the scan_filter package is importable."""
    scan_filter_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Scan Filter Code")
    if scan_filter_dir not in sys.path:
        sys.path.insert(0, scan_filter_dir)

    # Also add venv site-packages if present, so numpy/pandas/pydicom are available.
    venv_dir = os.path.join(scan_filter_dir, ".venv")
    if os.path.isdir(venv_dir):
        if sys.platform == "win32":
            sp = os.path.join(venv_dir, "Lib", "site-packages")
        else:
            # Linux/macOS: lib/pythonX.Y/site-packages
            ver = f"{sys.version_info.major}.{sys.version_info.minor}"
            sp = os.path.join(venv_dir, "lib", f"python{ver}", "site-packages")
        if os.path.isdir(sp) and sp not in sys.path:
            sys.path.insert(0, sp)


def validate_inputs(csv_path, dicom_folder):
    """
    Check that the cardiogram CSV and DICOM folder exist and are valid.

    Args:
        csv_path: Path to the cardiogram CSV file.
        dicom_folder: Path to the DICOM series folder.

    Returns:
        dict with keys: valid, csv_exists, dicom_exists, dicom_file_count, error
    """
    try:
        csv_exists = os.path.isfile(csv_path)
        dicom_exists = os.path.isdir(dicom_folder)
        dicom_file_count = 0
        if dicom_exists:
            dicom_file_count = len(glob.glob(os.path.join(dicom_folder, "*.dcm")))

        errors = []
        if not csv_exists:
            errors.append(f"CSV file not found: {csv_path}")
        if not dicom_exists:
            errors.append(f"DICOM folder not found: {dicom_folder}")
        elif dicom_file_count == 0:
            errors.append(f"No .dcm files found in: {dicom_folder}")

        return {
            "valid": csv_exists and dicom_exists and dicom_file_count > 0,
            "csv_exists": csv_exists,
            "dicom_exists": dicom_exists,
            "dicom_file_count": dicom_file_count,
            "error": "; ".join(errors) if errors else "",
        }
    except Exception as e:
        return {
            "valid": False,
            "csv_exists": False,
            "dicom_exists": False,
            "dicom_file_count": 0,
            "error": str(e),
        }


def run_scan_filter(csv_path, dicom_folder, output_dir, config_path=None):
    """
    Run the full scan filter pipeline.

    Args:
        csv_path: Path to the cleaned cardiogram CSV file.
        dicom_folder: Path to the folder containing the DICOM MRI series.
        output_dir: Destination folder for filtered DICOM frames.
        config_path: Optional path to a YAML configuration file.
                     If None, uses the default config.

    Returns:
        dict with keys: success, accepted_frames, rejected_frames,
        stable_cycles, alignment_offset_ms, output_dir, error
    """
    try:
        _ensure_scan_filter_on_path()

        from scan_filter.config import ScanFilterConfig
        from scan_filter.core.pipeline import ScanFilterPipeline

        # Load configuration
        if config_path and os.path.isfile(config_path):
            cfg = ScanFilterConfig.load(config_path)
        else:
            # Use default config from the Scan Filter Code directory
            default_cfg = os.path.join(
                os.path.dirname(os.path.abspath(__file__)),
                "Scan Filter Code", "config", "default.yaml"
            )
            if os.path.isfile(default_cfg):
                cfg = ScanFilterConfig.load(default_cfg)
            else:
                cfg = ScanFilterConfig()

        pipeline = ScanFilterPipeline(cfg)
        artifacts = pipeline.run(csv_path, dicom_folder, output_dir)

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
    print("=== Scan Filter Bridge Self-Test ===")
    print(f"validate_inputs('nonexistent.csv', 'nonexistent_dir'):")
    print(f"  {validate_inputs('nonexistent.csv', 'nonexistent_dir')}")
