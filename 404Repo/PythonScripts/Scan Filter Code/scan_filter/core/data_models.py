from __future__ import annotations

from dataclasses import dataclass, replace
from typing import Iterable, List, Optional, Sequence

import numpy as np
import pandas as pd


@dataclass(frozen=True)
class CardioSample:
    timestamp_ms: float
    signal: float


@dataclass
class CardioSeries:
    samples: Sequence[CardioSample]

    @property
    def timestamps(self) -> np.ndarray:
        return np.array([sample.timestamp_ms for sample in self.samples], dtype=float)

    @property
    def values(self) -> np.ndarray:
        return np.array([sample.signal for sample in self.samples], dtype=float)

    def estimate_sample_period_ms(self) -> float:
        ts = self.timestamps
        if ts.size < 2:
            return 1.0
        return float(np.mean(np.diff(ts)))

    def to_dataframe(self) -> pd.DataFrame:
        return pd.DataFrame({
            "timestamp_ms": self.timestamps,
            "signal": self.values,
        })

    @staticmethod
    def from_dataframe(df: pd.DataFrame) -> "CardioSeries":
        samples = [
            CardioSample(timestamp_ms=float(row.timestamp_ms), signal=float(row.signal))
            for row in df.itertuples(index=False)
        ]
        return CardioSeries(samples)


@dataclass(frozen=True)
class CardiacCycle:
    start_ms: float
    peak_ms: float
    end_ms: float
    rr_ms: float
    is_stable: bool

    @property
    def duration_ms(self) -> float:
        return self.end_ms - self.start_ms


@dataclass(frozen=True)
class MRIFrame:
    frame_id: int
    timestamp_ms: float
    dataset: any
    metadata: Optional[dict] = None

    def with_offset(self, offset_ms: float) -> "MRIFrame":
        return replace(self, timestamp_ms=self.timestamp_ms + offset_ms)


@dataclass
class MRIFrameSeries:
    frames: Sequence[MRIFrame]
    alignment_offset_ms: float = 0.0

    @property
    def timestamps(self) -> np.ndarray:
        return np.array([frame.timestamp_ms for frame in self.frames], dtype=float)

    def with_offset(self, offset_ms: float) -> "MRIFrameSeries":
        return MRIFrameSeries([frame.with_offset(offset_ms) for frame in self.frames], alignment_offset_ms=offset_ms)

    def __iter__(self):
        return iter(self.frames)

    def __len__(self) -> int:
        return len(self.frames)
