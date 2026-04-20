from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .data_models import CardioSeries, MRIFrameSeries


@dataclass
class TimeAlignmentResult:
    aligned_series: MRIFrameSeries
    offset_ms: float


class TimeAligner:
    """Align cardiogram and MRI series by estimating a global time offset."""

    def __init__(self, max_offset_ms: float) -> None:
        self.max_offset_ms = max_offset_ms

    def align(self, cardio: CardioSeries, frames: MRIFrameSeries) -> TimeAlignmentResult:
        cardio_ts = cardio.timestamps
        frame_ts = frames.timestamps
        if cardio_ts.size == 0 or frame_ts.size == 0:
            raise ValueError("Cannot align empty series")

        first_frame_time = frame_ts[0]
        idx = int(np.argmin(np.abs(cardio_ts - first_frame_time)))
        offset = float(cardio_ts[idx] - first_frame_time)
        if abs(offset) > self.max_offset_ms:
            # offset still applied but caller can decide whether to continue
            pass
        aligned = frames.with_offset(offset)
        return TimeAlignmentResult(aligned_series=aligned, offset_ms=offset)
