from __future__ import annotations  # Use modern typing behavior.

import argparse  # CLI parser.
from pathlib import Path  # Filesystem path helper.
import sys  # Python path adjustment for direct script execution.

# Ensure local package imports resolve when running this file from workspace root.
_THIS_DIR = Path(__file__).resolve().parent  # Folder containing this main.py.
if str(_THIS_DIR) not in sys.path:
    sys.path.insert(0, str(_THIS_DIR))

from cardiac_gating.config import ScanFilterConfig  # Config loader/dataclasses.
from cardiac_gating.dicom_writer import DicomSeriesWriter  # Optional full-series DICOM export.
from cardiac_gating.pipeline import ScanFilterPipeline  # Main gating pipeline.


# Builds CLI argument parser and returns parsed namespace.
def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(  # Create argument parser.
        description="Run cardiac gating with a cardiogram CSV and MRD scan input",  # CLI description.
    )
    parser.add_argument("cardio_csv", help="Path to the cardiogram CSV file")  # Required cardiogram path.
    parser.add_argument("mrd_file", help="Path to the MRD scan file")  # Required MRD path.
    parser.add_argument(  # Optional config path.
        "--config",
        default="config/default.yaml",
        help="Path to YAML configuration file",
    )
    parser.add_argument(  # Optional output directory.
        "--output",
        default="output/gated_dicom",
        help="Destination folder for gated DICOM output",
    )
    parser.add_argument(  # Optional extra export for full reconstruction (pre-gating filter).
        "--export-reconstructed",
        default=None,
        help="Optional folder to also export the full reconstructed MRD series before gating",
    )
    return parser.parse_args()  # Parse and return CLI args.


# Entrypoint used by __main__ guard.
def main() -> None:
    args = parse_args()  # Parse CLI arguments.
    config = ScanFilterConfig.load(args.config)  # Load YAML config (or defaults behavior in loader).
    pipeline = ScanFilterPipeline(config)  # Build pipeline with configured dependencies.
    artifacts = pipeline.run(args.cardio_csv, args.mrd_file, args.output)  # Run full gating workflow.

    print(  # Emit one-line completion summary.
        "Cardiac gating completed. "
        f"Accepted={artifacts.accepted_frames}, "
        f"Rejected={artifacts.rejected_frames}, "
        f"Stable cycles={artifacts.stable_cycles}, "
        f"Offset={artifacts.alignment_offset_ms:.2f} ms, "
        f"Output={artifacts.output_dir}"
    )

    if args.export_reconstructed:  # Optional full-series export branch.
        full_series = pipeline.mrd_reader.read(args.mrd_file)  # Read all MRD frames.
        reconstructed_dir = DicomSeriesWriter().write(  # Reconstruct/write every frame to DICOM.
            full_series.frames,
            Path(args.export_reconstructed),
        )
        print(f"Full reconstructed MRD-to-DICOM series written to {reconstructed_dir}")  # Confirm export.


if __name__ == "__main__":  # Script execution guard.
    main()  # Run CLI program.
