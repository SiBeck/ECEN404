from __future__ import annotations

import argparse
from pathlib import Path

from cardiac_gating.config import ScanFilterConfig
from cardiac_gating.dicom_writer import DicomSeriesWriter
from cardiac_gating.pipeline import ScanFilterPipeline


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run cardiac gating with a cardiogram CSV and MRD scan input",
    )
    parser.add_argument("cardio_csv", help="Path to the cardiogram CSV file")
    parser.add_argument("mrd_file", help="Path to the MRD scan file")
    parser.add_argument(
        "--config",
        default="config/default.yaml",
        help="Path to YAML configuration file",
    )
    parser.add_argument(
        "--output",
        default="output/gated_dicom",
        help="Destination folder for gated DICOM output",
    )
    parser.add_argument(
        "--export-reconstructed",
        default=None,
        help="Optional folder to also export the full reconstructed MRD series before gating",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = ScanFilterConfig.load(args.config)
    pipeline = ScanFilterPipeline(config)
    artifacts = pipeline.run(args.cardio_csv, args.mrd_file, args.output)

    print(
        "Cardiac gating completed. "
        f"Accepted={artifacts.accepted_frames}, "
        f"Rejected={artifacts.rejected_frames}, "
        f"Stable cycles={artifacts.stable_cycles}, "
        f"Offset={artifacts.alignment_offset_ms:.2f} ms, "
        f"Output={artifacts.output_dir}"
    )

    if args.export_reconstructed:
        full_series = pipeline.mrd_reader.read(args.mrd_file)
        reconstructed_dir = DicomSeriesWriter().write(full_series.frames, Path(args.export_reconstructed))
        print(f"Full reconstructed MRD-to-DICOM series written to {reconstructed_dir}")


if __name__ == "__main__":
    main()