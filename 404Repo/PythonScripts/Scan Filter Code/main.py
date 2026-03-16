from __future__ import annotations

import argparse
from pathlib import Path

from scan_filter.config import ScanFilterConfig
from scan_filter.core.pipeline import ScanFilterPipeline


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the Scan Filter pipeline")
    parser.add_argument("cardio_csv", help="Path to cleaned cardiogram CSV file")
    parser.add_argument("dicom_folder", help="Folder containing the DICOM MRI series")
    parser.add_argument(
        "--config",
        default="config/default.yaml",
        help="Path to YAML configuration file",
    )
    parser.add_argument(
        "--output",
        default=None,
        help="Destination folder for the filtered DICOM series",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    cfg = ScanFilterConfig.load(args.config)
    pipeline = ScanFilterPipeline(cfg)
    artifacts = pipeline.run(args.cardio_csv, args.dicom_folder, args.output)
    print(
        f"Scan Filter completed. Accepted={artifacts.accepted_frames}, Rejected={artifacts.rejected_frames}, "
        f"Stable cycles={artifacts.stable_cycles}, Offset={artifacts.alignment_offset_ms:.2f} ms, "
        f"Output={artifacts.output_dir}",
    )


if __name__ == "__main__":
    main()
