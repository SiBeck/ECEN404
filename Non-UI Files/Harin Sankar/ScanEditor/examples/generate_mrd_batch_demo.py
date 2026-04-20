from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np

from scan_filter.io.mrd_reader import reconstruct_mrd_to_png


def _write_synthetic_mrd(
    path: Path,
    *,
    nfe: int,
    npe: int,
    is_complex: bool,
    seed: int,
) -> None:
    rng = np.random.default_rng(seed)
    datatype_hex = "15" if is_complex else "05"
    shape = (nfe, npe, 1, 1, 1, 1)

    with path.open("wb") as handle:
        np.array([nfe, npe, 1, 1], dtype="<i4").tofile(handle)
        handle.write(b"\x00\x00")
        np.array([int(datatype_hex, 16)], dtype="<i2").tofile(handle)
        handle.write(b"\x00" * 132)
        np.array([1, 1], dtype="<i4").tofile(handle)
        if handle.tell() < 256:
            handle.write(b"\x00" * (256 - handle.tell()))

        comments = b"SYNTHETIC_MRD_BATCH"
        handle.write(comments + b"\x00" * (256 - len(comments)))

        if is_complex:
            real = rng.normal(0.0, 1.0, size=shape).astype(np.float32).ravel(order="F")
            imag = rng.normal(0.0, 1.0, size=shape).astype(np.float32).ravel(order="F")
            raw = np.empty(real.size * 2, dtype=np.float32)
            raw[0::2] = real
            raw[1::2] = imag
            raw.astype("<f4").tofile(handle)
        else:
            data = rng.normal(0.0, 1.0, size=shape).astype(np.float32).ravel(order="F")
            data.astype("<f4").tofile(handle)

        handle.write(b"\x00" * 120)
        handle.write(b"BATCH_DEMO")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate 20 synthetic MRD files and reconstruct to PNG")
    parser.add_argument(
        "--root",
        default="examples/mrd_batch_demo",
        help="Output root for generated MRD inputs and reconstructed PNGs",
    )
    parser.add_argument(
        "--count",
        type=int,
        default=20,
        help="How many synthetic MRD files to generate",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    root = Path(args.root)
    inputs_dir = root / "inputs"
    png_dir = root / "png_outputs"
    inputs_dir.mkdir(parents=True, exist_ok=True)
    png_dir.mkdir(parents=True, exist_ok=True)

    for idx in range(args.count):
        nfe = 24 + (idx % 5) * 8
        npe = 20 + (idx % 4) * 6
        mrd_path = inputs_dir / f"case_{idx:02d}.mrd"
        _write_synthetic_mrd(
            mrd_path,
            nfe=nfe,
            npe=npe,
            is_complex=bool(idx % 2),
            seed=5000 + idx,
        )
        target_png = png_dir / f"case_{idx:02d}.png"
        reconstruct_mrd_to_png(mrd_path, output_path=target_png)
        print(f"{mrd_path.name} -> {target_png.name}")

    print(f"Generated {args.count} MRD files in: {inputs_dir}")
    print(f"Reconstructed PNG outputs in: {png_dir}")


if __name__ == "__main__":
    main()
