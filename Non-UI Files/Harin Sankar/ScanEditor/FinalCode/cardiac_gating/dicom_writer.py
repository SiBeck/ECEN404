from __future__ import annotations  # Future annotation behavior.

from pathlib import Path  # Path utilities.
from typing import Iterable  # Iterable typing for frame input.

import numpy as np  # Numeric processing.
import pydicom  # Pydicom package import (runtime dependency).
from pydicom.dataset import FileDataset, FileMetaDataset  # Dataset classes.
from pydicom.uid import ExplicitVRLittleEndian, SecondaryCaptureImageStorage, generate_uid  # UID helpers.

from .models import MRIFrame  # Frame model.


class DicomSeriesWriter:
    def write(self, frames: Iterable[MRIFrame], destination: str | Path) -> Path:
        destination_path = Path(destination)  # Normalize output path.
        destination_path.mkdir(parents=True, exist_ok=True)  # Ensure output directory exists.

        frames_list = list(frames)  # Materialize frames iterable once.
        if not frames_list:  # Require at least one frame.
            raise ValueError("No frames provided to writer")

        study_uid = generate_uid()  # Shared study UID across output series.
        series_uid = generate_uid()  # Shared series UID across output series.
        for frame in frames_list:  # Write one DICOM per frame.
            image = self._reconstruct_uint16_from_mrd_frame(frame)  # Reconstruct uint16 image from k-space.
            dataset = self._build_dataset_from_array(  # Build DICOM dataset.
                image=image,
                frame_index=frame.frame_id,
                timestamp_ms=frame.timestamp_ms,
                study_uid=study_uid,
                series_uid=series_uid,
            )
            dataset.save_as(destination_path / f"frame_{frame.frame_id:04d}.dcm")  # Persist file.
        return destination_path  # Return output directory path.

    @staticmethod
    def _build_dataset_from_array(
        *,
        image: np.ndarray,
        frame_index: int,
        timestamp_ms: float,
        study_uid: str,
        series_uid: str,
    ) -> FileDataset:
        file_meta = FileMetaDataset()  # File meta container.
        file_meta.MediaStorageSOPClassUID = SecondaryCaptureImageStorage  # SC SOP class UID.
        file_meta.MediaStorageSOPInstanceUID = generate_uid()  # Unique SOP instance UID.
        file_meta.TransferSyntaxUID = ExplicitVRLittleEndian  # Transfer syntax.
        file_meta.ImplementationClassUID = generate_uid()  # Implementation UID.

        dataset = FileDataset("", {}, file_meta=file_meta, preamble=b"\0" * 128)  # Create DICOM file dataset.
        dataset.SOPClassUID = file_meta.MediaStorageSOPClassUID  # Main SOP class.
        dataset.SOPInstanceUID = file_meta.MediaStorageSOPInstanceUID  # Main SOP instance.
        dataset.StudyInstanceUID = study_uid  # Shared study UID.
        dataset.SeriesInstanceUID = series_uid  # Shared series UID.
        dataset.Modality = "MR"  # Modality label.
        dataset.PatientName = "MRDSolutions^Scan"  # Placeholder patient name.
        dataset.PatientID = "MRDSOL"  # Placeholder patient ID.
        dataset.InstanceNumber = frame_index + 1  # 1-based instance number.
        dataset.TriggerTime = str(float(timestamp_ms))  # Trigger time string.
        dataset.AcquisitionTime = DicomSeriesWriter._format_acquisition_time(timestamp_ms)  # DICOM TM timestamp.
        dataset.Rows, dataset.Columns = image.shape  # Image dimensions.
        dataset.SamplesPerPixel = 1  # Grayscale.
        dataset.PhotometricInterpretation = "MONOCHROME2"  # Pixel interpretation.
        dataset.BitsAllocated = 16  # 16-bit storage.
        dataset.BitsStored = 16  # 16 meaningful bits.
        dataset.HighBit = 15  # Highest bit index.
        dataset.PixelRepresentation = 0  # Unsigned pixels.
        dataset.PixelData = image.tobytes()  # Pixel payload.
        dataset.PixelSpacing = ["1.0", "1.0"]  # Placeholder spacing.
        return dataset  # Return dataset.

    @staticmethod
    def _reconstruct_uint16_from_mrd_frame(frame: MRIFrame) -> np.ndarray:
        kspace = np.asarray(frame.dataset)  # Convert frame dataset to ndarray.
        if not np.iscomplexobj(kspace):  # Ensure complex domain for FFT path.
            kspace = kspace.astype(np.float64, copy=False)
        image = np.abs(np.fft.fftshift(np.fft.fft2(kspace)))  # 2D FFT reconstruction with centered DC.
        magnitude = np.abs(image)  # Magnitude image.
        finite_mask = np.isfinite(magnitude)  # Valid finite pixels mask.
        if not np.any(finite_mask):  # All invalid -> return zero image.
            return np.zeros_like(magnitude, dtype=np.uint16)
        filtered = np.zeros_like(magnitude, dtype=float)  # Zero-filled float buffer.
        filtered[finite_mask] = magnitude[finite_mask]  # Keep finite magnitudes.
        max_value = float(filtered.max())  # Max magnitude for normalization.
        if max_value <= 0:  # Degenerate all-zero or negative case.
            return np.zeros_like(filtered, dtype=np.uint16)
        scaled = np.clip((filtered / max_value) * 65535.0, 0, 65535)  # Normalize to uint16 range.
        return scaled.astype(np.uint16)  # Convert to uint16 image.

    @staticmethod
    def _format_acquisition_time(timestamp_ms: float) -> str:
        total_seconds = max(0.0, timestamp_ms / 1000.0)  # Convert milliseconds to seconds.
        hours = int(total_seconds // 3600) % 24  # Hour component.
        minutes = int((total_seconds % 3600) // 60)  # Minute component.
        seconds = total_seconds % 60.0  # Seconds component.
        return f"{hours:02d}{minutes:02d}{seconds:06.3f}"  # DICOM TM format string.
