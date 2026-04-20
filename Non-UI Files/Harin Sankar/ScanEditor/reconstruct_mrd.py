"""Standalone MRD k-space to PNG reconstruction.

Usage:
    python reconstruct_mrd.py <path_to.mrd> [--output <out.png>] [--hamming] [--zero-pad N M]

Defaults:
    Output: same directory as input, same stem, .png extension.
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


# ---------------------------------------------------------------------------
# MRD reader (self-contained, no scan_filter dependency)
# ---------------------------------------------------------------------------

def read_mrd(file_path: str | Path) -> tuple[np.ndarray, dict]:
    """Read an MR Solutions .mrd file.

    Returns
    -------
    data : np.ndarray
        k-space array, shape (Nfe, Npe) for single-frame files or
        (Nfe, Npe, N3d, Nslice, Nechoes, Nexps) for multi-frame.
    header : dict
        Parsed header fields.
    """
    path = Path(file_path)
    if not path.exists():
        raise FileNotFoundError(f"MRD file not found: {file_path}")

    with path.open("rb") as fh:
        # Header: 4 × int32 → Nfe, Npe, N3d, Nslice
        head = np.fromfile(fh, dtype="<i4", count=4)
        if head.size != 4:
            raise IOError("Truncated MRD header")
        nfe, npe, n3d, nslice = map(int, head)

        # 2-byte skip, then int16 datatype
        fh.seek(2, 1)
        dt_raw = np.fromfile(fh, dtype="<i2", count=1)
        if dt_raw.size != 1:
            raise IOError("Missing MRD datatype entry")
        dattype_hex = f"{int(dt_raw[0]):X}".zfill(2)
        is_complex = dattype_hex[0] == "1"

        # 132-byte skip, then 2 × int32 → Nechoes, Nexps
        fh.seek(132, 1)
        dims = np.fromfile(fh, dtype="<i4", count=2)
        if dims.size != 2:
            raise IOError("Missing MRD echo/experiment counts")
        nechoes, nexps = map(int, dims)

        # Seek to byte 256 (comments block)
        cur = fh.tell()
        if cur < 256:
            fh.seek(256 - cur, 1)
        comments = fh.read(256).decode("ascii", errors="ignore").rstrip("\x00")

        # Data dtype
        dtype_code = dattype_hex[1]
        _map = {"0": "<u1", "1": "<i1", "2": "<i2", "3": "<i2",
                "4": "<i4", "5": "<f4", "6": "<f8"}
        if dtype_code not in _map:
            raise ValueError(f"Unsupported MRD datatype nibble: {dtype_code}")
        base_dtype = np.dtype(_map[dtype_code])

        # Read raw k-space
        num_pts = nfe * npe * n3d * nslice * nechoes * nexps
        shape6 = (nfe, npe, n3d, nslice, nechoes, nexps)

        if num_pts == 0:
            data = np.array([], dtype=complex if is_complex else base_dtype)
        elif is_complex:
            raw = np.fromfile(fh, dtype=base_dtype, count=num_pts * 2)
            if raw.size != num_pts * 2:
                raise IOError("Unexpected EOF reading complex MRD data")
            data = (raw[0::2].astype(np.float64) + 1j * raw[1::2].astype(np.float64)).reshape(shape6, order="F")
        else:
            raw = np.fromfile(fh, dtype=base_dtype, count=num_pts)
            if raw.size != num_pts:
                raise IOError("Unexpected EOF reading MRD data")
            data = raw.reshape(shape6, order="F")

        fh.read(120)  # filename slot (unused)
        parameters = fh.read().decode("ascii", errors="ignore")

    # Collapse trivial dimensions for single-frame files
    if n3d == 1 and nslice == 1 and nechoes == 1 and nexps == 1:
        data = data[:, :, 0, 0, 0, 0]

    header = dict(nfe=nfe, npe=npe, n3d=n3d, nslice=nslice,
                  nechoes=nechoes, nexps=nexps,
                  is_complex=is_complex, comments=comments,
                  parameters=parameters)
    return data, header


# ---------------------------------------------------------------------------
# Reconstruction helpers
# ---------------------------------------------------------------------------

def apply_hamming_window(kspace: np.ndarray) -> np.ndarray:
    """Multiply k-space by a 2-D Hamming window (in-place copy)."""
    nfe, npe = kspace.shape[:2]
    window = np.outer(np.hamming(nfe), np.hamming(npe))
    return kspace * window


def zero_pad(kspace: np.ndarray, new_nfe: int, new_npe: int) -> np.ndarray:
    """Centre-pad k-space to (new_nfe, new_npe)."""
    nfe, npe = kspace.shape[:2]
    pad_fe = (new_nfe - nfe, 0)  # (before, after) calculated below
    pad_pe = (new_npe - npe, 0)
    pad_fe = ((new_nfe - nfe) // 2, new_nfe - nfe - (new_nfe - nfe) // 2)
    pad_pe = ((new_npe - npe) // 2, new_npe - npe - (new_npe - npe) // 2)
    extra_dims = [(0, 0)] * (kspace.ndim - 2)
    return np.pad(kspace, [pad_fe, pad_pe] + extra_dims, mode="constant")


def reconstruct(kspace: np.ndarray) -> np.ndarray:
    """2-D FFT reconstruction: returns magnitude image, normalised to [0, 255]."""
    # For multi-frame data use first frame
    if kspace.ndim > 2:
        kspace = kspace[:, :, 0, 0, 0, 0]

    img = np.abs(np.fft.fftshift(np.fft.fft2(kspace)))

    # Normalise to 8-bit
    img_min, img_max = img.min(), img.max()
    if img_max > img_min:
        img = (img - img_min) / (img_max - img_min) * 255.0
    else:
        img = np.zeros_like(img)

    return img.astype(np.uint8)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Reconstruct an MR Solutions .mrd k-space file to a PNG image."
    )
    parser.add_argument("mrd_file", help="Path to the .mrd file")
    parser.add_argument("--output", default=None,
                        help="Output PNG path (default: same directory/name as input)")
    parser.add_argument("--hamming", action="store_true",
                        help="Apply a 2-D Hamming window before reconstruction")
    parser.add_argument("--zero-pad", nargs=2, type=int, metavar=("NFE", "NPE"),
                        default=None,
                        help="Zero-pad k-space to NFE × NPE before reconstruction")
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    mrd_path = Path(args.mrd_file)
    print(f"Reading  : {mrd_path}")

    kspace, header = read_mrd(mrd_path)
    print(f"k-space  : shape={kspace.shape}, complex={header['is_complex']}")
    if header["comments"]:
        print(f"Comments : {header['comments']}")

    if args.hamming:
        kspace = apply_hamming_window(kspace)
        print("Applied  : Hamming window")

    if args.zero_pad:
        nfe, npe = args.zero_pad
        kspace = zero_pad(kspace, nfe, npe)
        print(f"Zero-pad : {nfe} × {npe}")

    img_array = reconstruct(kspace)
    print(f"Image    : {img_array.shape[1]} × {img_array.shape[0]} px")

    out_path = Path(args.output) if args.output else mrd_path.with_suffix(".png")
    Image.fromarray(img_array, mode="L").save(out_path)
    print(f"Saved    : {out_path}")


if __name__ == "__main__":
    main()
