from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Iterator, Sequence

import numpy as np
from matplotlib import pyplot as plt

from ..core.data_models import MRIFrame, MRIFrameSeries


@dataclass(frozen=True)
class MRDHeader:
    frequency_encoding: int
    phase_encoding: int
    n3d: int
    slices: int
    echoes: int
    experiments: int
    datatype_hex: str
    is_complex: bool
    comments: str
    parameters: str

    @property
    def frame_count(self) -> int:
        return max(1, self.n3d * self.slices * self.echoes * self.experiments)


def read_mrd(file_path: str | Path) -> tuple[np.ndarray, MRDHeader]:
    """Parse an MR Solutions *.mrd file into a k-space array and header metadata."""

    path = Path(file_path)
    if not path.exists():
        raise FileNotFoundError(f"MRD file not found: {file_path}")

    with path.open("rb") as handle:
        header_values = np.fromfile(handle, dtype="<i4", count=4)
        if header_values.size != 4:
            raise IOError("Unexpected end of file while reading MRD header")
        nfe, npe, n3d, nslice = map(int, header_values)

        handle.seek(2, 1)
        dattype_raw = np.fromfile(handle, dtype="<i2", count=1)
        if dattype_raw.size != 1:
            raise IOError("Missing MRD datatype entry")
        dattype_hex = f"{int(dattype_raw[0]):X}".zfill(2)
        is_complex = dattype_hex[0] == "1"

        handle.seek(132, 1)
        dim_values = np.fromfile(handle, dtype="<i4", count=2)
        if dim_values.size != 2:
            raise IOError("MRD echo/experiment counts missing")
        nechoes, nexps = map(int, dim_values)

        current = handle.tell()
        if current < 256:
            handle.seek(256 - current, 1)

        comments = handle.read(256).decode("ascii", errors="ignore").rstrip("\x00")

        num_points = nfe * npe * n3d * nslice * nechoes * nexps
        dtype_code = dattype_hex[1]

        base_dtype: np.dtype
        if dtype_code == "0":
            base_dtype = np.dtype("<u1")
        elif dtype_code == "1":
            base_dtype = np.dtype("<i1")
        elif dtype_code in ("2", "3"):
            base_dtype = np.dtype("<i2")
        elif dtype_code == "4":
            base_dtype = np.dtype("<i4")
        elif dtype_code == "5":
            base_dtype = np.dtype("<f4")
        elif dtype_code == "6":
            base_dtype = np.dtype("<f8")
        else:
            raise ValueError(f"Unsupported MRD datatype nibble: {dtype_code}")

        if num_points == 0:
            data = np.array([], dtype=base_dtype)
        else:
            if not is_complex:
                raw = np.fromfile(handle, dtype=base_dtype, count=num_points)
                if raw.size != num_points:
                    raise IOError("Unexpected end of file while reading MRD data")
                data = raw.reshape((nfe, npe, n3d, nslice, nechoes, nexps), order="F")
            else:
                raw = np.fromfile(handle, dtype=base_dtype, count=num_points * 2)
                if raw.size != num_points * 2:
                    raise IOError("Unexpected end of file while reading complex MRD data")
                real = raw[0::2].astype(np.float64)
                imag = raw[1::2].astype(np.float64)
                complex_data = real + 1j * imag
                data = complex_data.reshape((nfe, npe, n3d, nslice, nechoes, nexps), order="F")

        handle.read(120)  # filename slot (unused)
        parameters = handle.read().decode("ascii", errors="ignore")

    if n3d == 1 and nslice == 1 and nexps == 1:
        data = data[:, :, 0, 0, 0, 0]

    header = MRDHeader(
        frequency_encoding=nfe,
        phase_encoding=npe,
        n3d=n3d,
        slices=nslice,
        echoes=nechoes,
        experiments=nexps,
        datatype_hex=dattype_hex,
        is_complex=is_complex,
        comments=comments,
        parameters=parameters,
    )
    return data, header


def reconstruct_mrd_to_png(
    file_path: str | Path,
    *,
    output_path: str | Path | None = None,
    apply_hamming_window: bool = False,
    zero_pad_shape: tuple[int, int] | None = None,
) -> list[Path]:
    """Reconstruct an MRD file and export frame images as PNG files.

    For single-frame MRD files this writes `<stem>.png` by default.
    For multi-frame MRD files this writes `<stem>_000.png`, `<stem>_001.png`, ...
    """

    path = Path(file_path)
    kspace, _ = read_mrd(path)
    reader = MRDSolutionsReader(
        apply_hamming_window=apply_hamming_window,
        zero_pad_shape=zero_pad_shape,
    )
    frame_arrays = list(reader._iter_frames(kspace))
    if not frame_arrays:
        raise ValueError(f"MRD file {file_path} did not contain any frames")

    if output_path is None:
        if len(frame_arrays) == 1:
            targets = [path.with_suffix(".png")]
        else:
            targets = [path.with_name(f"{path.stem}_{idx:03d}.png") for idx in range(len(frame_arrays))]
    else:
        requested = Path(output_path)
        if len(frame_arrays) == 1:
            if requested.suffix.lower() != ".png":
                requested = requested.with_suffix(".png")
            targets = [requested]
        else:
            if requested.suffix:
                raise ValueError("For multi-frame MRD exports, output_path must be a directory path")
            targets = [requested / f"{path.stem}_{idx:03d}.png" for idx in range(len(frame_arrays))]

    for target in targets:
        target.parent.mkdir(parents=True, exist_ok=True)

    for idx, frame_kspace in enumerate(frame_arrays):
        prepared = reader._prepare_kspace(frame_kspace)
        image = reader._reconstruct_image(prepared)
        scaled_image = reader._scale_to_uint16(image)
        plt.imsave(targets[idx], scaled_image, cmap="gray", vmin=0, vmax=65535)

    return targets


class MRDSolutionsReader:
    """Convert MR Solutions *.mrd files into k-space frame arrays."""

    def __init__(
        self,
        *,
        frame_period_ms: float = 40.0,
        start_time_ms: float = 0.0,
        apply_hamming_window: bool = False,
        zero_pad_shape: tuple[int, int] | None = None,
    ) -> None:
        if frame_period_ms <= 0:
            raise ValueError("frame_period_ms must be positive")
        if zero_pad_shape is not None:
            rows, cols = zero_pad_shape
            if rows <= 0 or cols <= 0:
                raise ValueError("zero_pad_shape must contain positive dimensions")
        self.frame_period_ms = float(frame_period_ms)
        self.start_time_ms = float(start_time_ms)
        self.apply_hamming_window = apply_hamming_window
        self.zero_pad_shape = zero_pad_shape

    def read(self, file_path: str | Path) -> MRIFrameSeries:
        path = Path(file_path)
        kspace, header = read_mrd(path)
        frame_arrays = list(self._iter_frames(kspace))
        if not frame_arrays:
            raise ValueError(f"MRD file {file_path} did not contain any frames")

        base_metadata = {
            "source_path": str(path),
            "source_type": "mrd_kspace",
            "mrd_datatype": header.datatype_hex,
            "mrd_comments": header.comments,
            "mrd_parameters": header.parameters,
            "kspace_shape": list(kspace.shape),
        }

        frames: list[MRIFrame] = []
        for idx, frame_kspace in enumerate(frame_arrays):
            prepared = self._prepare_kspace(frame_kspace)
            timestamp_ms = self.start_time_ms + idx * self.frame_period_ms
            metadata = {
                **base_metadata,
                "frame_index": idx,
                "rows": int(prepared.shape[0]),
                "columns": int(prepared.shape[1]),
            }
            frames.append(
                MRIFrame(
                    frame_id=idx,
                    timestamp_ms=timestamp_ms,
                    dataset=prepared,
                    metadata=metadata,
                ),
            )
        return MRIFrameSeries(frames)

    def _prepare_kspace(self, frame_kspace: np.ndarray) -> np.ndarray:
        data = np.array(frame_kspace, copy=True)
        if self.apply_hamming_window:
            data = self._apply_hamming_window(data)
        if self.zero_pad_shape is not None:
            data = self._zero_pad(data, self.zero_pad_shape)
        return data

    @staticmethod
    def _iter_frames(kspace: np.ndarray) -> Iterator[np.ndarray]:
        if kspace.ndim == 2:
            yield kspace
            return
        fe, pe = kspace.shape[:2]
        trailing = int(np.prod(kspace.shape[2:]))
        reshaped = np.reshape(kspace, (fe, pe, trailing), order="F")
        for idx in range(trailing):
            yield reshaped[:, :, idx]

    @staticmethod
    def _apply_hamming_window(data: np.ndarray) -> np.ndarray:
        rows, cols = data.shape
        wx = np.hamming(rows)
        wy = np.hamming(cols)
        return data * np.outer(wx, wy)

    @staticmethod
    def _zero_pad(data: np.ndarray, target_shape: Sequence[int]) -> np.ndarray:
        rows, cols = data.shape
        target_rows, target_cols = int(target_shape[0]), int(target_shape[1])
        if target_rows < rows or target_cols < cols:
            raise ValueError("zero_pad_shape must be larger than the input dimensions")
        row_pad_before = (target_rows - rows) // 2
        row_pad_after = target_rows - rows - row_pad_before
        col_pad_before = (target_cols - cols) // 2
        col_pad_after = target_cols - cols - col_pad_before
        return np.pad(
            data,
            ((row_pad_before, row_pad_after), (col_pad_before, col_pad_after)),
            mode="constant",
            constant_values=0,
        )

    @staticmethod
    def _reconstruct_image(kspace: np.ndarray) -> np.ndarray:
        return np.abs(np.fft.fftshift(np.fft.fft2(kspace)))

    @staticmethod
    def _scale_to_uint16(image: np.ndarray) -> np.ndarray:
        magnitude = np.abs(image)
        finite_mask = np.isfinite(magnitude)
        if not np.any(finite_mask):
            return np.zeros_like(magnitude, dtype=np.uint16)
        filtered = np.zeros_like(magnitude, dtype=float)
        filtered[finite_mask] = magnitude[finite_mask]
        max_val = float(filtered.max())
        if max_val <= 0:
            return np.zeros_like(filtered, dtype=np.uint16)
        normalized = filtered / max_val
        scaled = np.clip(normalized * 65535.0, 0, 65535)
        return scaled.astype(np.uint16)
