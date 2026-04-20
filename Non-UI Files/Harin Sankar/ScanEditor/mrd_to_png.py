from __future__ import annotations

import argparse
from pathlib import Path

from scan_filter.io.mrd_reader import reconstruct_mrd_to_png


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Reconstruct MR Solutions .mrd files to PNG")
    parser.add_argument("mrd_file", help="Path to the .mrd file")
    parser.add_argument(
        "--output",
        default=None,
        help=(
            "PNG output path for single-frame input, or directory for multi-frame input. "
            "If omitted, outputs next to the MRD file."
        ),
    )
    parser.add_argument(
        "--hamming",
        action="store_true",
        help="Apply a 2-D Hamming window before reconstruction",
    )
    parser.add_argument(
        "--zero-pad-fe",
        type=int,
        default=None,
        help="Zero-pad frequency-encoding dimension to this size",
    )
    parser.add_argument(
        "--zero-pad-pe",
        type=int,
        default=None,
        help="Zero-pad phase-encoding dimension to this size",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
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
        print(f"Wrote {Path(output)}")


if __name__ == "__main__":
    main()
