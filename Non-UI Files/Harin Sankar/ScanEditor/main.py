from __future__ import annotations

import argparse
import sys
from pathlib import Path

from scan_filter.io.mrd_reader import reconstruct_mrd_to_png


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    raw_args = list(sys.argv[1:] if argv is None else argv)
    known_commands = {"pipeline", "mrd-to-png", "gate-mrd", "view-dicom"}
    if raw_args and raw_args[0] not in known_commands:
        raw_args = ["pipeline", *raw_args]

    parser = argparse.ArgumentParser(description="Scan Filter tools")
    subparsers = parser.add_subparsers(dest="command", required=True)

    pipeline_parser = subparsers.add_parser(
        "pipeline",
        help="Run the cardiogram-aligned scan filtering pipeline",
    )
    pipeline_parser.add_argument("cardio_csv", help="Path to cleaned cardiogram CSV file")
    pipeline_parser.add_argument("dicom_folder", help="Folder containing the DICOM MRI series or an .mrd file")
    pipeline_parser.add_argument(
        "--config",
        default="config/default.yaml",
        help="Path to YAML configuration file",
    )
    pipeline_parser.add_argument(
        "--output",
        default=None,
        help="Destination folder for the filtered DICOM series",
    )

    mrd_parser = subparsers.add_parser(
        "mrd-to-png",
        help="Reconstruct an MRD file and write PNG image(s)",
    )
    mrd_parser.add_argument("mrd_file", help="Path to the .mrd file")
    mrd_parser.add_argument(
        "--output",
        default=None,
        help=(
            "PNG output file for single-frame input, or directory for multi-frame input. "
            "If omitted, writes next to the MRD file."
        ),
    )
    mrd_parser.add_argument(
        "--hamming",
        action="store_true",
        help="Apply a 2-D Hamming window before reconstruction",
    )
    mrd_parser.add_argument(
        "--zero-pad-fe",
        type=int,
        default=None,
        help="Zero-pad frequency-encoding dimension to this size",
    )
    mrd_parser.add_argument(
        "--zero-pad-pe",
        type=int,
        default=None,
        help="Zero-pad phase-encoding dimension to this size",
    )

    gate_mrd_parser = subparsers.add_parser(
        "gate-mrd",
        help="Run cardiac gating with cardiogram CSV + MRD input and output DICOM",
    )
    gate_mrd_parser.add_argument("cardio_csv", help="Path to cleaned cardiogram CSV file")
    gate_mrd_parser.add_argument("mrd_file", help="Path to MR Solutions .mrd file")
    gate_mrd_parser.add_argument(
        "--config",
        default="config/default.yaml",
        help="Path to YAML configuration file",
    )
    gate_mrd_parser.add_argument(
        "--output",
        default=None,
        help="Destination folder for gated/filtered DICOM output",
    )
    gate_mrd_parser.add_argument(
        "--export-reconstructed",
        default=None,
        help=(
            "Optional folder to also export the full reconstructed MRD-as-DICOM series "
            "before gating."
        ),
    )

    view_dicom_parser = subparsers.add_parser(
        "view-dicom",
        help="Open a DICOM file in a local matplotlib viewer",
    )
    view_dicom_parser.add_argument("dicom_file", help="Path to a .dcm file to display")

    return parser.parse_args(raw_args)


def main() -> None:
    args = parse_args()
    command = args.command

    if command == "mrd-to-png":
        zero_pad_shape = None
        if args.zero_pad_fe is not None or args.zero_pad_pe is not None:
            if args.zero_pad_fe is None or args.zero_pad_pe is None:
                raise ValueError("Both --zero-pad-fe and --zero-pad-pe must be provided together")
            zero_pad_shape = (int(args.zero_pad_fe), int(args.zero_pad_pe))
        outputs = reconstruct_mrd_to_png(
            args.mrd_file,
            output_path=args.output,
            apply_hamming_window=args.hamming,
            zero_pad_shape=zero_pad_shape,
        )
        for output in outputs:
            print(f"Wrote {output}")
        return

    if command == "view-dicom":
        from image_viewer import main as dicom_viewer_main

        sys.argv = ["image_viewer.py", args.dicom_file]
        dicom_viewer_main()
        return

    from scan_filter.config import ScanFilterConfig
    from scan_filter.core.pipeline import ScanFilterPipeline
    from scan_filter.io.dicom_writer import DicomSeriesWriter

    cfg = ScanFilterConfig.load(args.config)
    pipeline = ScanFilterPipeline(cfg)
    source = args.dicom_folder if command == "pipeline" else args.mrd_file
    artifacts = pipeline.run(args.cardio_csv, source, args.output)
    print(
        f"Scan Filter completed. Accepted={artifacts.accepted_frames}, Rejected={artifacts.rejected_frames}, "
        f"Stable cycles={artifacts.stable_cycles}, Offset={artifacts.alignment_offset_ms:.2f} ms, "
        f"Output={artifacts.output_dir}",
    )
    if command == "gate-mrd" and args.export_reconstructed:
        full_series = pipeline.mrd_reader.read(args.mrd_file)
        writer = DicomSeriesWriter()
        reconstructed_dir = writer.write(full_series.frames, Path(args.export_reconstructed))
        print(f"Reconstructed full MRD-to-DICOM series written to {reconstructed_dir}")


if __name__ == "__main__":
    main()
