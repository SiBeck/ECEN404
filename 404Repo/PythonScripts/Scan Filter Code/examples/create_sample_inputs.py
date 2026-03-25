from __future__ import annotations

import argparse
import math
from datetime import datetime, timedelta
from pathlib import Path
from typing import Optional, Tuple

import numpy as np
import pandas as pd
from pydicom.dataset import Dataset, FileDataset
from pydicom.uid import UID, ExplicitVRLittleEndian, generate_uid

CARDIO_FILENAME = "cardiogram_sample.csv"
DICOM_DIRNAME = "dicom_sample"


def generate_cardiogram(destination: Path, beats: int = 18, base_rr_ms: float = 800.0) -> Path:
    """Generate a synthetic cardiogram with mild RR variability."""
    destination = Path(destination)  # Accept str from Python.NET callers
    sample_period_ms = 5.0
    total_ms = int(beats * base_rr_ms + 1000)
    timestamps = np.arange(0, total_ms, sample_period_ms, dtype=float)
    values = []
    rr_variation = np.sin(np.linspace(0, math.pi * 2, beats)) * 0.05
    beat_times = []
    cursor = 0.0
    for idx in range(beats):
        beat_times.append(cursor)
        cursor += base_rr_ms * (1.0 + rr_variation[idx])

    beat_idx = 0
    for ts in timestamps:
        # base waveform
        value = 0.2 * math.sin(ts / 120.0)
        if beat_idx < len(beat_times) and abs(ts - beat_times[beat_idx]) < 20.0:
            value += math.exp(-((ts - beat_times[beat_idx]) ** 2) / 100.0) * 2.5
        values.append(value)
        if beat_idx < len(beat_times) and ts >= beat_times[beat_idx] + 40.0:
            beat_idx += 1

    df = pd.DataFrame({"timestamp_ms": timestamps, "signal": values})
    destination.parent.mkdir(parents=True, exist_ok=True)
    df.to_csv(destination, index=False)
    return destination


MR_IMAGE_STORAGE_UID = UID("1.2.840.10008.5.1.4.1.1.4")


def build_file_meta(sop_instance_uid: str) -> Dataset:
    file_meta = Dataset()
    file_meta.MediaStorageSOPClassUID = MR_IMAGE_STORAGE_UID
    file_meta.MediaStorageSOPInstanceUID = sop_instance_uid
    file_meta.TransferSyntaxUID = ExplicitVRLittleEndian
    file_meta.ImplementationClassUID = generate_uid()
    return file_meta


def create_frame_dataset(
    meta: Dataset,
    study_uid: str,
    series_uid: str,
    sop_instance_uid: str,
    instance_number: int,
    shape: Tuple[int, int],
) -> FileDataset:
    ds = FileDataset("", {}, file_meta=meta, preamble=b"\x00" * 128)
    rows, cols = shape
    ds.Modality = "MR"
    ds.PatientName = "SCAN^FILTER"
    ds.PatientID = "SF001"
    ds.StudyInstanceUID = study_uid
    ds.SeriesInstanceUID = series_uid
    ds.SOPClassUID = MR_IMAGE_STORAGE_UID
    ds.SOPInstanceUID = sop_instance_uid
    ds.Rows = rows
    ds.Columns = cols
    ds.PixelSpacing = [1.0, 1.0]
    ds.BitsAllocated = 16
    ds.BitsStored = 16
    ds.HighBit = 15
    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.PixelRepresentation = 1
    ds.RescaleIntercept = 0
    ds.RescaleSlope = 1
    ds.InstanceNumber = instance_number
    return ds


def generate_dicom_series(
    destination: Path,
    frame_count: int = 12,
    start_ms: float = 100.0,
    spacing_ms: float = 200.0,
    *,
    shape: Optional[Tuple[int, int]] = None,
    jitter_ms: float = 0.0,
    spacing_noise_ms: float = 0.0,
    global_offset_ms: float = 0.0,
    seed: Optional[int] = None,
) -> Path:
    """Create a folder filled with synthetic DICOM frames.

    Extra keyword arguments make it easy to simulate different scanner behaviors
    (frame size, cadence jitter, or clock offsets) while keeping the original
    defaults backward compatible.
    """
    destination = Path(destination)  # Accept str from Python.NET callers
    destination.mkdir(parents=True, exist_ok=True)
    base_time = datetime.utcnow()
    study_uid = generate_uid()
    series_uid = generate_uid()
    rows, cols = shape if shape else (32, 32)
    rng = np.random.default_rng(seed)

    def _noise(scale: float) -> float:
        return float(rng.normal(0.0, scale)) if scale > 0 else 0.0

    current_time = start_ms + global_offset_ms
    for idx in range(frame_count):
        if idx == 0:
            trigger_time = current_time + _noise(jitter_ms)
        else:
            spacing_delta = spacing_ms + _noise(spacing_noise_ms)
            jitter_delta = _noise(jitter_ms)
            current_time += max(5.0, spacing_delta + jitter_delta)
            trigger_time = current_time
        acquisition = base_time + timedelta(milliseconds=trigger_time)
        sop_instance_uid = generate_uid()
        meta = build_file_meta(sop_instance_uid)
        ds = create_frame_dataset(
            meta=meta,
            study_uid=study_uid,
            series_uid=series_uid,
            sop_instance_uid=sop_instance_uid,
            instance_number=idx + 1,
            shape=(rows, cols),
        )
        ds.TriggerTime = float(trigger_time)
        ds.AcquisitionTime = acquisition.strftime("%H%M%S.%f")
        ds.ContentTime = ds.AcquisitionTime
        ramp = np.linspace(0.0, 1.0, rows * cols, dtype=np.float32).reshape(rows, cols)
        ramp += rng.normal(0.0, 0.05, size=ramp.shape)
        ramp = np.clip(ramp, 0.0, 1.0)
        pixels = (ramp * (1500 + idx * 200)).astype(np.uint16)
        ds.PixelData = pixels.tobytes()
        ds.save_as(destination / f"frame_{idx:04d}.dcm")
    return destination


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate sample inputs for the Scan Filter pipeline")
    parser.add_argument("output", nargs="?", default="examples/data", help="Destination directory for sample assets")
    parser.add_argument("--beats", type=int, default=18, help="Number of synthetic cardiac beats")
    parser.add_argument("--frames", type=int, default=12, help="Number of DICOM frames to generate")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    root = Path(args.output)
    cardio_path = root / CARDIO_FILENAME
    dicom_path = root / DICOM_DIRNAME

    generate_cardiogram(cardio_path, beats=args.beats)
    generate_dicom_series(dicom_path, frame_count=args.frames)

    print(f"Generated cardiogram CSV: {cardio_path}")
    print(f"Generated DICOM series: {dicom_path}")


if __name__ == "__main__":
    main()
