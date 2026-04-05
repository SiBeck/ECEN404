from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator, Sequence

import numpy as np

from .models import CardioSample, CardioSeries, MRIFrame, MRIFrameSeries


class CardiogramCsvReader:
    def __init__(self, timestamp_column: str = "timestamp_ms", value_column: str = "signal") -> None:
        self.timestamp_column = timestamp_column
        self.value_column = value_column

    def read(self, path: str | Path) -> CardioSeries:
        csv_path = Path(path)
        with csv_path.open("r", encoding="utf-8", newline="") as handle:
            reader = csv.DictReader(handle)
            if reader.fieldnames is None:
                raise ValueError("CSV file is missing a header row")
            if self.timestamp_column not in reader.fieldnames or self.value_column not in reader.fieldnames:
                raise ValueError(
                    f"CSV must contain '{self.timestamp_column}' and '{self.value_column}' columns"
                )

            samples: list[CardioSample] = []
            for row in reader:
                samples.append(
                    CardioSample(
                        timestamp_ms=float(row[self.timestamp_column]),
                        signal=float(row[self.value_column]),
                    )
                )
        return CardioSeries(samples)


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


def read_mrd(file_path: str | Path) -> tuple[np.ndarray, MRDHeader]:
    path = Path(file_path)
    if not path.exists():
        raise FileNotFoundError(f"MRD file not found: {file_path}")

    with path.open("rb") as handle:
        header_values = np.fromfile(handle, dtype="<i4", count=4)
        if header_values.size != 4:
            raise IOError("Unexpected end of file while reading MRD header")
        nfe, npe, n3d, nslice = map(int, header_values)

        handle.seek(2, 1)
        datatype_raw = np.fromfile(handle, dtype="<i2", count=1)
        if datatype_raw.size != 1:
            raise IOError("Missing MRD datatype entry")
        datatype_hex = f"{int(datatype_raw[0]):X}".zfill(2)
        is_complex = datatype_hex[0] == "1"

        handle.seek(132, 1)
        dim_values = np.fromfile(handle, dtype="<i4", count=2)
        if dim_values.size != 2:
            raise IOError("MRD echo or experiment counts missing")
        nechoes, nexps = map(int, dim_values)

        current = handle.tell()
        if current < 256:
            handle.seek(256 - current, 1)

        comments = handle.read(256).decode("ascii", errors="ignore").rstrip("\x00")
        num_points = nfe * npe * n3d * nslice * nechoes * nexps
        dtype_code = datatype_hex[1]

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
        elif not is_complex:
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

        handle.read(120)
        parameters = handle.read().decode("ascii", errors="ignore")

    header = MRDHeader(
        frequency_encoding=nfe,
        phase_encoding=npe,
        n3d=n3d,
        slices=nslice,
        echoes=nechoes,
        experiments=nexps,
        datatype_hex=datatype_hex,
        is_complex=is_complex,
        comments=comments,
        parameters=parameters,
    )
    return data, header


class MRDSolutionsReader:
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
            frames.append(
                MRIFrame(
                    frame_id=idx,
                    timestamp_ms=self.start_time_ms + idx * self.frame_period_ms,
                    dataset=prepared,
                    metadata={
                        **base_metadata,
                        "frame_index": idx,
                        "rows": int(prepared.shape[0]),
                        "columns": int(prepared.shape[1]),
                    },
                )
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
        return data * np.outer(np.hamming(rows), np.hamming(cols))

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