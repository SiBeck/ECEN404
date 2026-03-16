from __future__ import annotations

from dataclasses import dataclass
from typing import List

import numpy as np

from .data_models import CardiacCycle, CardioSeries
from ..utils.statistics import moving_average, moving_std


@dataclass
class CycleDetectionSummary:
    cycles: List[CardiacCycle]
    stable_cycles: List[CardiacCycle]


class CycleDetector:
    """Detect cardiac cycles and flag the stable ones."""

    def __init__(
        self,
        window_ms: float,
        min_peak_prominence: float,
        stable_rr_variation_pct: float,
        min_rr_ms: float,
        max_rr_ms: float,
    ) -> None:
        self.window_ms = window_ms
        self.min_peak_prominence = min_peak_prominence
        self.stable_rr_variation_pct = stable_rr_variation_pct
        self.min_rr_ms = min_rr_ms
        self.max_rr_ms = max_rr_ms

    def detect(self, cardio: CardioSeries) -> CycleDetectionSummary:
        timestamps = cardio.timestamps
        values = cardio.values
        sample_period = cardio.estimate_sample_period_ms()
        window_samples = max(5, int(self.window_ms / max(sample_period, 1.0)))

        baseline = moving_average(values, window_samples)
        noise = moving_std(values, window_samples)
        adaptive_threshold = baseline + self.min_peak_prominence * np.maximum(noise, 1e-6)

        min_distance_samples = max(1, int(self.min_rr_ms / max(sample_period, 1.0)))
        peaks = self._detect_peaks(values, adaptive_threshold, min_distance_samples)
        if len(peaks) < 2:
            return CycleDetectionSummary([], [])

        cycles: List[CardiacCycle] = []
        rr_intervals = np.diff(timestamps[peaks])
        valid_rr = rr_intervals[(rr_intervals >= self.min_rr_ms) & (rr_intervals <= self.max_rr_ms)]
        rr_median = float(np.median(valid_rr)) if valid_rr.size else (float(np.median(rr_intervals)) if rr_intervals.size else 0.0)
        for left_idx, right_idx in zip(peaks[:-1], peaks[1:]):
            rr_ms = float(timestamps[right_idx] - timestamps[left_idx])
            is_rr_valid = self.min_rr_ms <= rr_ms <= self.max_rr_ms
            if not is_rr_valid or rr_median <= 0:
                is_stable = False
            else:
                delta = abs(rr_ms - rr_median)
                allowed = rr_median * (self.stable_rr_variation_pct / 100.0)
                is_stable = delta <= allowed
            start_idx = max(0, left_idx - (right_idx - left_idx) // 3)
            end_idx = min(len(values) - 1, right_idx + (right_idx - left_idx) // 3)
            cycles.append(
                CardiacCycle(
                    start_ms=float(timestamps[start_idx]),
                    peak_ms=float(timestamps[left_idx]),
                    end_ms=float(timestamps[end_idx]),
                    rr_ms=rr_ms,
                    is_stable=is_stable,
                ),
            )
        stable_cycles = [cycle for cycle in cycles if cycle.is_stable]
        return CycleDetectionSummary(cycles=cycles, stable_cycles=stable_cycles)

    def _detect_peaks(self, signal: np.ndarray, threshold: np.ndarray, min_distance: int) -> List[int]:
        peaks: List[int] = []
        for idx in range(1, len(signal) - 1):
            if signal[idx] <= threshold[idx]:
                continue
            if signal[idx] > signal[idx - 1] and signal[idx] >= signal[idx + 1]:
                if peaks and idx - peaks[-1] < min_distance:
                    if signal[idx] > signal[peaks[-1]]:
                        peaks[-1] = idx
                else:
                    peaks.append(idx)
        return peaks
