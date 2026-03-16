from scan_filter.core.aligner import TimeAligner
from scan_filter.core.data_models import CardioSample, CardioSeries, MRIFrame, MRIFrameSeries


def build_cardio_series(count: int, start_ms: float = 0.0, step_ms: float = 10.0) -> CardioSeries:
    samples = [CardioSample(timestamp_ms=start_ms + idx * step_ms, signal=0.5) for idx in range(count)]
    return CardioSeries(samples)


def build_frame_series(count: int, start_ms: float = -100.0, step_ms: float = 40.0) -> MRIFrameSeries:
    frames = [
        MRIFrame(frame_id=idx, timestamp_ms=start_ms + idx * step_ms, dataset=object(), metadata=None)
        for idx in range(count)
    ]
    return MRIFrameSeries(frames)


def test_time_alignment_applies_global_offset():
    cardio = build_cardio_series(50)
    frames = build_frame_series(10)
    aligner = TimeAligner(max_offset_ms=5000.0)

    result = aligner.align(cardio, frames)

    assert abs(result.offset_ms - 100.0) < 1e-6
    assert abs(result.aligned_series.frames[0].timestamp_ms - 0.0) < 1e-6
