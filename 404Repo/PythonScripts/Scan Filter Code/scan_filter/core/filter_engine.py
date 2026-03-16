from __future__ import annotations

from dataclasses import dataclass
from typing import List, Sequence

import numpy as np

from .data_models import CardiacCycle, MRIFrame, MRIFrameSeries


@dataclass
class FilterResult:
    accepted_frames: List[MRIFrame]
    rejected_frames: List[MRIFrame]
    stable_cycle_count: int


class FrameFilter:
    """Determine which MRI frames fall inside stable cardiac phases."""

    def __init__(self, stable_phase_fraction: float, stable_phase_offset: float) -> None:
        self.stable_phase_fraction = stable_phase_fraction
        self.stable_phase_offset = stable_phase_offset

    def filter_frames(self, frames: MRIFrameSeries, cycles: Sequence[CardiacCycle]) -> FilterResult:
        if not cycles:
            return FilterResult([], list(frames), 0)

        accepted: List[MRIFrame] = []
        rejected: List[MRIFrame] = []
        for frame in frames:
            if self._is_frame_in_stable_phase(frame.timestamp_ms, cycles):
                accepted.append(frame)
            else:
                rejected.append(frame)
        stable_cycles = sum(1 for cycle in cycles if cycle.is_stable)
        return FilterResult(accepted_frames=accepted, rejected_frames=rejected, stable_cycle_count=stable_cycles)

    def _is_frame_in_stable_phase(self, timestamp_ms: float, cycles: Sequence[CardiacCycle]) -> bool:
        for cycle in cycles:
            if not cycle.is_stable:
                continue
            duration = max(cycle.duration_ms, 1.0)
            phase_start = cycle.start_ms + self.stable_phase_offset * duration
            phase_end = min(cycle.end_ms, phase_start + self.stable_phase_fraction * duration)
            if phase_start <= timestamp_ms <= phase_end:
                return True
        return False
