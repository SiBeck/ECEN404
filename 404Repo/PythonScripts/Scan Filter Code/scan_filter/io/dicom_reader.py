from __future__ import annotations

from pathlib import Path
from typing import Any, List, Optional

try:  # pragma: no cover - optional dependency during tests
    import pydicom
except ImportError:  # pragma: no cover
    pydicom = None

from ..core.data_models import MRIFrame, MRIFrameSeries


class DicomSeriesReader:
    """Load a folder of DICOM files into an MRIFrameSeries."""

    def __init__(self, timestamp_tag: str = "TriggerTime") -> None:
        self.timestamp_tag = timestamp_tag

    def read(self, folder: str | Path) -> MRIFrameSeries:
        if pydicom is None:  # pragma: no cover - exercised in integration
            raise RuntimeError("pydicom must be installed to read DICOM series")
        path = Path(folder)
        if not path.exists() or not path.is_dir():
            raise FileNotFoundError(f"DICOM folder not found: {folder}")

        datasets: List[Any] = []
        for file in sorted(path.glob("*.dcm")):
            datasets.append(pydicom.dcmread(file))

        if not datasets:
            raise ValueError(f"No DICOM files found inside {folder}")

        frames = []
        for idx, ds in enumerate(datasets):
            timestamp_ms = self._extract_timestamp(ds)
            frames.append(
                MRIFrame(
                    frame_id=idx,
                    timestamp_ms=timestamp_ms,
                    dataset=ds,
                    metadata={"source_path": str(path)},
                ),
            )
        return MRIFrameSeries(frames)

    def _extract_timestamp(self, dataset: Any) -> float:
        if self.timestamp_tag in dataset:
            return float(dataset.get(self.timestamp_tag))
        # fallback to AcquisitionTime (HHMMSS.frac) convert to ms
        acquisition_time = dataset.get("AcquisitionTime")
        if acquisition_time is None:
            raise ValueError("Dataset missing timestamp information")
        hh = int(acquisition_time[0:2])
        mm = int(acquisition_time[2:4])
        ss = float(acquisition_time[4:])
        total_seconds = hh * 3600 + mm * 60 + ss
        return total_seconds * 1000.0
