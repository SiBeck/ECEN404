from __future__ import annotations

from pathlib import Path
from typing import Any, Iterable

try:  # pragma: no cover
    import pydicom
    from pydicom.dataset import FileDataset, FileMetaDataset
    from pydicom.uid import ExplicitVRLittleEndian, SecondaryCaptureImageStorage, generate_uid
except ImportError:  # pragma: no cover
    pydicom = None

import numpy as np

from ..core.data_models import MRIFrame


class DicomSeriesWriter:
    """Persist selected MRI frames as a new DICOM series."""

    def write(self, frames: Iterable[MRIFrame], destination: str | Path) -> Path:
        if pydicom is None:  # pragma: no cover - exercised in integration
            raise RuntimeError("pydicom must be installed to write DICOM series")
        dest_path = Path(destination)
        dest_path.mkdir(parents=True, exist_ok=True)
        frames_list = list(frames)
        if not frames_list:
            raise ValueError("No frames provided to writer")

        mrd_frames = [frame for frame in frames_list if isinstance(frame.dataset, np.ndarray)]
        if mrd_frames and len(mrd_frames) != len(frames_list):
            raise ValueError("Mixed frame dataset types are not supported in a single write call")

        if mrd_frames:
            study_uid = generate_uid()
            series_uid = generate_uid()
            for frame in mrd_frames:
                image = self._reconstruct_uint16_from_mrd_frame(frame)
                ds = self._build_dataset_from_array(
                    image=image,
                    frame_index=frame.frame_id,
                    timestamp_ms=frame.timestamp_ms,
                    study_uid=study_uid,
                    series_uid=series_uid,
                )
                target_file = dest_path / f"frame_{frame.frame_id:04d}.dcm"
                ds.save_as(target_file)
            return dest_path

        written = 0
        for written, frame in enumerate(frames_list, start=1):
            ds: Any = frame.dataset
            target_file = dest_path / f"frame_{frame.frame_id:04d}.dcm"
            ds.save_as(target_file)
        return dest_path

    @staticmethod
    def _build_dataset_from_array(
        *,
        image: np.ndarray,
        frame_index: int,
        timestamp_ms: float,
        study_uid: str,
        series_uid: str,
    ) -> FileDataset:
        assert pydicom is not None  # for type checkers
        file_meta = FileMetaDataset()
        file_meta.MediaStorageSOPClassUID = SecondaryCaptureImageStorage
        file_meta.MediaStorageSOPInstanceUID = generate_uid()
        file_meta.TransferSyntaxUID = ExplicitVRLittleEndian
        file_meta.ImplementationClassUID = generate_uid()

        dataset = FileDataset("", {}, file_meta=file_meta, preamble=b"\0" * 128)
        dataset.SOPClassUID = file_meta.MediaStorageSOPClassUID
        dataset.SOPInstanceUID = file_meta.MediaStorageSOPInstanceUID
        dataset.StudyInstanceUID = study_uid
        dataset.SeriesInstanceUID = series_uid
        dataset.Modality = "MR"
        dataset.PatientName = "MRDSolutions^Scan"
        dataset.PatientID = "MRDSOL"
        dataset.InstanceNumber = frame_index + 1
        dataset.TriggerTime = str(float(timestamp_ms))
        dataset.AcquisitionTime = DicomSeriesWriter._format_acquisition_time(timestamp_ms)
        dataset.Rows, dataset.Columns = image.shape
        dataset.SamplesPerPixel = 1
        dataset.PhotometricInterpretation = "MONOCHROME2"
        dataset.BitsAllocated = 16
        dataset.BitsStored = 16
        dataset.HighBit = 15
        dataset.PixelRepresentation = 0
        dataset.PixelData = image.tobytes()
        dataset.PixelSpacing = ["1.0", "1.0"]
        return dataset

    @staticmethod
    def _reconstruct_uint16_from_mrd_frame(frame: MRIFrame) -> np.ndarray:
        kspace = np.asarray(frame.dataset)
        if not np.iscomplexobj(kspace):
            kspace = kspace.astype(np.float64, copy=False)
        image = np.abs(np.fft.fftshift(np.fft.fft2(kspace)))
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

    @staticmethod
    def _format_acquisition_time(timestamp_ms: float) -> str:
        total_seconds = max(0.0, timestamp_ms / 1000.0)
        hours = int(total_seconds // 3600) % 24
        minutes = int((total_seconds % 3600) // 60)
        seconds = total_seconds % 60.0
        return f"{hours:02d}{minutes:02d}{seconds:06.3f}"
