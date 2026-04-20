from __future__ import annotations  # Future annotation behavior.

import csv  # CSV parsing.
from dataclasses import dataclass  # Dataclass helper for header model.
from pathlib import Path  # Path helper.
from typing import Iterator, Sequence  # Type hints.

import numpy as np  # Numeric arrays.

from .models import CardioSample, CardioSeries, MRIFrame, MRIFrameSeries  # Domain models.


class CardiogramCsvReader:
    def __init__(self, timestamp_column: str = "timestamp_ms", value_column: str = "signal") -> None:
        self.timestamp_column = timestamp_column  # Timestamp column name.
        self.value_column = value_column  # Signal column name.

    def read(self, path: str | Path) -> CardioSeries:
        csv_path = Path(path)  # Normalize to Path.
        with csv_path.open("r", encoding="utf-8", newline="") as handle:  # Open CSV file.
            reader = csv.DictReader(handle)  # Dict-based CSV reader.
            if reader.fieldnames is None:  # Header is required for DictReader mapping.
                raise ValueError("CSV file is missing a header row")
            if self.timestamp_column not in reader.fieldnames or self.value_column not in reader.fieldnames:  # Required columns check.
                raise ValueError(f"CSV must contain '{self.timestamp_column}' and '{self.value_column}' columns")

            samples: list[CardioSample] = []  # Parsed sample list.
            for row in reader:  # Parse each CSV row.
                samples.append(
                    CardioSample(
                        timestamp_ms=float(row[self.timestamp_column]),  # Parse timestamp.
                        signal=float(row[self.value_column]),  # Parse signal.
                    )
                )
        return CardioSeries(samples)  # Wrap parsed samples.


@dataclass(frozen=True)
class MRDHeader:
    frequency_encoding: int  # FE dimension.
    phase_encoding: int  # PE dimension.
    n3d: int  # 3D dimension.
    slices: int  # Slice dimension.
    echoes: int  # Echo dimension.
    experiments: int  # Experiment dimension.
    datatype_hex: str  # Datatype hex nibble pair.
    is_complex: bool  # Complex payload flag.
    comments: str  # Header comments string.
    parameters: str  # Trailing parameters string.


# Low-level MRD binary reader returning ndarray payload + parsed header.
def read_mrd(file_path: str | Path) -> tuple[np.ndarray, MRDHeader]:
    path = Path(file_path)  # Normalize to Path.
    if not path.exists():  # Validate input path.
        raise FileNotFoundError(f"MRD file not found: {file_path}")

    with path.open("rb") as handle:  # Open MRD file as binary stream.
        header_values = np.fromfile(handle, dtype="<i4", count=4)  # Read first four int32 fields.
        if header_values.size != 4:  # Ensure full header read.
            raise IOError("Unexpected end of file while reading MRD header")
        nfe, npe, n3d, nslice = map(int, header_values)  # Unpack dimensions.

        handle.seek(2, 1)  # Skip 2-byte reserved gap.
        datatype_raw = np.fromfile(handle, dtype="<i2", count=1)  # Read datatype word.
        if datatype_raw.size != 1:  # Validate read.
            raise IOError("Missing MRD datatype entry")
        datatype_hex = f"{int(datatype_raw[0]):X}".zfill(2)  # Datatype as two-nibble hex.
        is_complex = datatype_hex[0] == "1"  # Leading nibble indicates complex payload.

        handle.seek(132, 1)  # Skip reserved block.
        dim_values = np.fromfile(handle, dtype="<i4", count=2)  # Read echoes and experiments.
        if dim_values.size != 2:  # Validate read.
            raise IOError("MRD echo or experiment counts missing")
        nechoes, nexps = map(int, dim_values)  # Unpack dims.

        current = handle.tell()  # Current stream position.
        if current < 256:  # Comments block starts at byte 256.
            handle.seek(256 - current, 1)  # Seek to comments start.

        comments = handle.read(256).decode("ascii", errors="ignore").rstrip("\x00")  # Read comment text.
        num_points = nfe * npe * n3d * nslice * nechoes * nexps  # Logical scalar count.
        dtype_code = datatype_hex[1]  # Trailing nibble indicates base dtype.

        if dtype_code == "0":
            base_dtype = np.dtype("<u1")  # Unsigned byte.
        elif dtype_code == "1":
            base_dtype = np.dtype("<i1")  # Signed byte.
        elif dtype_code in ("2", "3"):
            base_dtype = np.dtype("<i2")  # int16 variants.
        elif dtype_code == "4":
            base_dtype = np.dtype("<i4")  # int32.
        elif dtype_code == "5":
            base_dtype = np.dtype("<f4")  # float32.
        elif dtype_code == "6":
            base_dtype = np.dtype("<f8")  # float64.
        else:
            raise ValueError(f"Unsupported MRD datatype nibble: {dtype_code}")  # Unknown code.

        if num_points == 0:  # Empty payload edge case.
            data = np.array([], dtype=base_dtype)
        elif not is_complex:  # Real-valued payload path.
            raw = np.fromfile(handle, dtype=base_dtype, count=num_points)  # Read scalars.
            if raw.size != num_points:  # Validate complete read.
                raise IOError("Unexpected end of file while reading MRD data")
            data = raw.reshape((nfe, npe, n3d, nslice, nechoes, nexps), order="F")  # Fortran-order reshape.
        else:  # Complex payload path.
            raw = np.fromfile(handle, dtype=base_dtype, count=num_points * 2)  # Read interleaved real/imag scalars.
            if raw.size != num_points * 2:  # Validate complete read.
                raise IOError("Unexpected end of file while reading complex MRD data")
            real = raw[0::2].astype(np.float64)  # Even indices are real parts.
            imag = raw[1::2].astype(np.float64)  # Odd indices are imag parts.
            complex_data = real + 1j * imag  # Build complex vector.
            data = complex_data.reshape((nfe, npe, n3d, nslice, nechoes, nexps), order="F")  # Fortran-order reshape.

        handle.read(120)  # Skip trailing filename slot.
        parameters = handle.read().decode("ascii", errors="ignore")  # Read remaining parameter text.

    header = MRDHeader(  # Package parsed header fields.
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
    return data, header  # Return payload and metadata.


class MRDSolutionsReader:
    def __init__(
        self,
        *,
        frame_period_ms: float = 40.0,
        start_time_ms: float = 0.0,
        apply_hamming_window: bool = False,
        zero_pad_shape: tuple[int, int] | None = None,
    ) -> None:
        if frame_period_ms <= 0:  # Frame spacing must be positive.
            raise ValueError("frame_period_ms must be positive")
        if zero_pad_shape is not None:  # Validate optional zero-pad shape.
            rows, cols = zero_pad_shape
            if rows <= 0 or cols <= 0:
                raise ValueError("zero_pad_shape must contain positive dimensions")
        self.frame_period_ms = float(frame_period_ms)  # Store frame delta.
        self.start_time_ms = float(start_time_ms)  # Store first timestamp.
        self.apply_hamming_window = apply_hamming_window  # Store apodization toggle.
        self.zero_pad_shape = zero_pad_shape  # Store optional zero-pad target.

    def read(self, file_path: str | Path) -> MRIFrameSeries:
        path = Path(file_path)  # Normalize to Path.
        kspace, header = read_mrd(path)  # Parse MRD payload + header.
        frame_arrays = list(self._iter_frames(kspace))  # Flatten trailing dims into frame list.
        if not frame_arrays:  # Require at least one frame.
            raise ValueError(f"MRD file {file_path} did not contain any frames")

        base_metadata = {  # Shared metadata copied to each frame.
            "source_path": str(path),
            "source_type": "mrd_kspace",
            "mrd_datatype": header.datatype_hex,
            "mrd_comments": header.comments,
            "mrd_parameters": header.parameters,
            "kspace_shape": list(kspace.shape),
        }

        frames: list[MRIFrame] = []  # Output frame list.
        for idx, frame_kspace in enumerate(frame_arrays):  # Build each MRIFrame object.
            prepared = self._prepare_kspace(frame_kspace)  # Optional preprocessing pipeline.
            frames.append(
                MRIFrame(
                    frame_id=idx,
                    timestamp_ms=self.start_time_ms + idx * self.frame_period_ms,  # Synthetic timestamp.
                    dataset=prepared,
                    metadata={
                        **base_metadata,
                        "frame_index": idx,
                        "rows": int(prepared.shape[0]),
                        "columns": int(prepared.shape[1]),
                    },
                )
            )
        return MRIFrameSeries(frames)  # Wrap frames in series container.

    def _prepare_kspace(self, frame_kspace: np.ndarray) -> np.ndarray:
        data = np.array(frame_kspace, copy=True)  # Clone to avoid mutating source.
        if self.apply_hamming_window:  # Optional Hamming apodization.
            data = self._apply_hamming_window(data)
        if self.zero_pad_shape is not None:  # Optional center zero-padding.
            data = self._zero_pad(data, self.zero_pad_shape)
        return data  # Return prepared frame.

    @staticmethod
    def _iter_frames(kspace: np.ndarray) -> Iterator[np.ndarray]:
        if kspace.ndim == 2:  # Single-frame case.
            yield kspace
            return
        fe, pe = kspace.shape[:2]  # Spatial dims.
        trailing = int(np.prod(kspace.shape[2:]))  # Number of trailing frames.
        reshaped = np.reshape(kspace, (fe, pe, trailing), order="F")  # Collapse trailing dims in Fortran order.
        for idx in range(trailing):
            yield reshaped[:, :, idx]  # Yield one 2D frame.

    @staticmethod
    def _apply_hamming_window(data: np.ndarray) -> np.ndarray:
        rows, cols = data.shape  # Matrix shape.
        return data * np.outer(np.hamming(rows), np.hamming(cols))  # Separable 2D Hamming window.

    @staticmethod
    def _zero_pad(data: np.ndarray, target_shape: Sequence[int]) -> np.ndarray:
        rows, cols = data.shape  # Source shape.
        target_rows, target_cols = int(target_shape[0]), int(target_shape[1])  # Target shape.
        if target_rows < rows or target_cols < cols:  # Zero-pad cannot shrink.
            raise ValueError("zero_pad_shape must be larger than the input dimensions")
        row_pad_before = (target_rows - rows) // 2  # Top pad.
        row_pad_after = target_rows - rows - row_pad_before  # Bottom pad.
        col_pad_before = (target_cols - cols) // 2  # Left pad.
        col_pad_after = target_cols - cols - col_pad_before  # Right pad.
        return np.pad(  # Constant-zero center padding.
            data,
            ((row_pad_before, row_pad_after), (col_pad_before, col_pad_after)),
            mode="constant",
            constant_values=0,
        )
