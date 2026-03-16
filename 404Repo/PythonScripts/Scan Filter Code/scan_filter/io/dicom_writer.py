from __future__ import annotations

from pathlib import Path
from typing import Any, Iterable

try:  # pragma: no cover
    import pydicom
except ImportError:  # pragma: no cover
    pydicom = None

from ..core.data_models import MRIFrame


class DicomSeriesWriter:
    """Persist selected MRI frames as a new DICOM series."""

    def write(self, frames: Iterable[MRIFrame], destination: str | Path) -> Path:
        if pydicom is None:  # pragma: no cover - exercised in integration
            raise RuntimeError("pydicom must be installed to write DICOM series")
        dest_path = Path(destination)
        dest_path.mkdir(parents=True, exist_ok=True)
        written = 0
        for written, frame in enumerate(frames, start=1):
            ds: Any = frame.dataset
            target_file = dest_path / f"frame_{frame.frame_id:04d}.dcm"
            ds.save_as(target_file)
        if written == 0:
            raise ValueError("No frames provided to writer")
        return dest_path
