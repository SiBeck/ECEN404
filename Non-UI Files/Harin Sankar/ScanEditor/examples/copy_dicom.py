from __future__ import annotations

import argparse
from pathlib import Path

try:
    import pydicom
except ImportError as exc:  # pragma: no cover - simple utility script
    raise SystemExit("pydicom must be installed to copy DICOM files") from exc


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Read a single DICOM file and write it back unchanged.",
    )
    parser.add_argument("source", help="Path to the input DICOM file")
    parser.add_argument("destination", help="Path where the output DICOM will be written")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    source = Path(args.source)
    destination = Path(args.destination)

    if not source.is_file():
        raise SystemExit(f"Source DICOM file not found: {source}")

    dataset = pydicom.dcmread(source)
    destination.parent.mkdir(parents=True, exist_ok=True)
    dataset.save_as(destination)
    print(f"Copied {source} -> {destination}")


if __name__ == "__main__":
    main()
