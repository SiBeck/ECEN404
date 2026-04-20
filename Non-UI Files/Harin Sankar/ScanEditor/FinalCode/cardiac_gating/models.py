from __future__ import annotations  # Future annotation behavior.

from dataclasses import dataclass, replace  # Dataclass primitives.
from typing import Any, Sequence  # Generic metadata + sequence interfaces.

import numpy as np  # Numeric array operations.


@dataclass(frozen=True)
class CardioSample:
    timestamp_ms: float  # Cardiogram sample timestamp.
    signal: float  # Cardiogram sample amplitude.


@dataclass
class CardioSeries:
    samples: Sequence[CardioSample]  # Underlying sample collection.

    @property
    def timestamps(self) -> np.ndarray:
        return np.array([sample.timestamp_ms for sample in self.samples], dtype=float)  # Time vector.

    @property
    def values(self) -> np.ndarray:
        return np.array([sample.signal for sample in self.samples], dtype=float)  # Signal vector.

    def estimate_sample_period_ms(self) -> float:
        timestamps = self.timestamps  # Materialize timestamp vector.
        if timestamps.size < 2:  # Need at least two samples for a delta.
            return 1.0  # Safe fallback period.
        return float(np.mean(np.diff(timestamps)))  # Mean adjacent timestamp delta.


@dataclass(frozen=True)
class CardiacCycle:
    start_ms: float  # Cycle start timestamp.
    peak_ms: float  # Representative peak timestamp.
    end_ms: float  # Cycle end timestamp.
    rr_ms: float  # RR interval length.
    is_stable: bool  # Stability classification.

    @property
    def duration_ms(self) -> float:
        return self.end_ms - self.start_ms  # Cycle duration helper.


@dataclass(frozen=True)
class MRIFrame:
    frame_id: int  # Source frame index.
    timestamp_ms: float  # Frame timestamp.
    dataset: Any  # Frame payload (k-space matrix).
    metadata: dict[str, Any] | None = None  # Optional metadata dictionary.

    def with_offset(self, offset_ms: float) -> "MRIFrame":
        return replace(self, timestamp_ms=self.timestamp_ms + offset_ms)  # Return shifted timestamp copy.


@dataclass
class MRIFrameSeries:
    frames: Sequence[MRIFrame]  # Ordered frame sequence.
    alignment_offset_ms: float = 0.0  # Applied global offset metadata.

    @property
    def timestamps(self) -> np.ndarray:
        return np.array([frame.timestamp_ms for frame in self.frames], dtype=float)  # Frame timestamp vector.

    def with_offset(self, offset_ms: float) -> "MRIFrameSeries":
        shifted = [frame.with_offset(offset_ms) for frame in self.frames]  # Shift each frame timestamp.
        return MRIFrameSeries(shifted, alignment_offset_ms=offset_ms)  # Return shifted series wrapper.

    def __iter__(self):
        return iter(self.frames)  # Enable direct iteration over frames.

    def __len__(self) -> int:
        return len(self.frames)  # Return number of frames.
