import numpy as np

from scan_filter.core.cycle_detection import CycleDetector
from scan_filter.core.data_models import CardioSample, CardioSeries


def synthetic_signal(peaks: int, rr_ms: float) -> CardioSeries:
    timestamps = np.arange(0, peaks * rr_ms, 10.0)
    signal = np.sin(timestamps / 200.0)
    # inject stronger R-peaks every rr_ms
    for idx in range(0, len(timestamps), int(rr_ms // 10)):
        signal[idx] = 2.0
    samples = [CardioSample(timestamp_ms=float(t), signal=float(v)) for t, v in zip(timestamps, signal)]
    return CardioSeries(samples)


def test_cycle_detector_flags_stable_cycles():
    cardio = synthetic_signal(peaks=8, rr_ms=800.0)
    detector = CycleDetector(
        window_ms=400.0,
        min_peak_prominence=0.3,
        stable_rr_variation_pct=5.0,
        min_rr_ms=500.0,
        max_rr_ms=1200.0,
    )

    summary = detector.detect(cardio)

    assert len(summary.cycles) >= 5
    assert len(summary.stable_cycles) == len(summary.cycles)
