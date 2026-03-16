from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, Iterator, Sequence, Tuple


def sliding_windows(values: Sequence[float], window: int, step: int = 1) -> Iterator[Tuple[int, Sequence[float]]]:
    if window <= 0:
        raise ValueError("window must be positive")
    if step <= 0:
        raise ValueError("step must be positive")
    for start in range(0, max(len(values) - window + 1, 1), step):
        yield start, values[start : start + window]


@dataclass
class AdaptiveWindow:
    size_ms: float
    min_size_ms: float = 200.0
    max_size_ms: float = 1200.0

    def clamp(self, ms: float) -> float:
        return max(self.min_size_ms, min(self.max_size_ms, ms))

    def next(self, previous_rr_ms: float, variance_pct: float) -> float:
        """Adjust window size based on prior RR variance."""
        scaled = previous_rr_ms * (1.0 + variance_pct)
        return self.clamp(scaled)
