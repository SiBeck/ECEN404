import argparse
from pathlib import Path

import pydicom


def main() -> None:
    parser = argparse.ArgumentParser(description="Copy a DICOM file with no modifications.")
    parser.add_argument("input", help="Path to the source DICOM file")
    parser.add_argument("output", help="Path to the destination DICOM file")
    args = parser.parse_args()

    src = Path(args.input)
    dst = Path(args.output)

    if not src.is_file():
        raise SystemExit(f"Input file not found: {src}")

    dataset = pydicom.dcmread(src)
    dst.parent.mkdir(parents=True, exist_ok=True)
    dataset.save_as(dst)
    print(f"Wrote {dst}")


if __name__ == "__main__":
    main()
