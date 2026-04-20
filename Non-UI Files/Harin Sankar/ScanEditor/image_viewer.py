"""Utility for reconstructing and displaying a DICOM frame."""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Optional, Tuple

import matplotlib.pyplot as plt
import numpy as np
import pydicom
from pydicom.dataset import FileDataset
from pydicom.multival import MultiValue


def _as_float(value: Optional[float | MultiValue]) -> Optional[float]:
	"""Return the first numeric entry from a DICOM value if present."""

	if value is None:
		return None
	if isinstance(value, MultiValue):
		return float(value[0])
	return float(value)


def _load_pixels(dataset: FileDataset) -> np.ndarray:
	"""Return pixel data with rescale slope/intercept applied."""

	pixels = dataset.pixel_array.astype(np.float32)
	slope = float(getattr(dataset, "RescaleSlope", 1.0))
	intercept = float(getattr(dataset, "RescaleIntercept", 0.0))
	return pixels * slope + intercept


def _window_bounds(
	pixels: np.ndarray,
	dataset: FileDataset,
	override_center: Optional[float],
	override_width: Optional[float],
) -> Tuple[float, float]:
	"""Determine lower/upper bounds for grayscale windowing."""

	center = override_center
	width = override_width

	if center is None or width is None:
		ds_center = _as_float(dataset.get("WindowCenter"))
		ds_width = _as_float(dataset.get("WindowWidth"))
		if center is None:
			center = ds_center
		if width is None:
			width = ds_width

	if center is not None and width is not None:
		half_width = max(width / 2.0, 1e-3)
		low, high = center - half_width, center + half_width
	else:
		low, high = float(np.min(pixels)), float(np.max(pixels))

	if low == high:
		high = low + 1.0
	return low, high


def _normalize(pixels: np.ndarray, low: float, high: float) -> np.ndarray:
	"""Clip/scale pixels to 0-255 uint8 range for display."""

	clipped = np.clip(pixels, low, high)
	normalized = (clipped - low) / (high - low)
	return (normalized * 255).astype(np.uint8)


def _parse_args() -> argparse.Namespace:
	parser = argparse.ArgumentParser(
		description=(
			"Reconstruct a DICOM frame (like the provided te014 image) "
			"and view it with matplotlib."
		)
	)
	parser.add_argument(
		"dicom_path",
		type=Path,
		nargs="?",
		default=Path("examples/data/dicom_sample/frame_0004.dcm"),
		help=(
			"Path to the DICOM file to view. Defaults to the sample frame "
			"under examples/data/dicom_sample."
		),
	)
	parser.add_argument(
		"--window-center",
		type=float,
		dest="window_center",
		help="Override the DICOM WindowCenter tag if desired.",
	)
	parser.add_argument(
		"--window-width",
		type=float,
		dest="window_width",
		help="Override the DICOM WindowWidth tag if desired.",
	)
	parser.add_argument(
		"--save",
		type=Path,
		help="Optional PNG path for exporting the reconstructed image.",
	)
	return parser.parse_args()


def main() -> None:
	args = _parse_args()
	dicom_path = args.dicom_path.expanduser().resolve()

	if not dicom_path.exists():
		raise FileNotFoundError(f"No DICOM file found at {dicom_path}")

	dataset = pydicom.dcmread(dicom_path)
	pixels = _load_pixels(dataset)
	low, high = _window_bounds(pixels, dataset, args.window_center, args.window_width)
	display_pixels = _normalize(pixels, low, high)

	plt.figure(figsize=(4, 4))
	plt.imshow(display_pixels, cmap="gray", interpolation="nearest")
	plt.title(f"{dicom_path.stem} Image")
	plt.xlabel("")
	plt.ylabel("")
	plt.tight_layout()

	if args.save:
		save_path = args.save.expanduser().resolve()
		save_path.parent.mkdir(parents=True, exist_ok=True)
		plt.savefig(save_path, dpi=300, bbox_inches="tight")

	plt.show()


if __name__ == "__main__":
	main()
