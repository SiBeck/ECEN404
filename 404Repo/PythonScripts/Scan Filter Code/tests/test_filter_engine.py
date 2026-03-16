from scan_filter.core.data_models import CardiacCycle, MRIFrame, MRIFrameSeries
from scan_filter.core.filter_engine import FrameFilter


def build_cycles():
    return [
        CardiacCycle(start_ms=0.0, peak_ms=100.0, end_ms=600.0, rr_ms=500.0, is_stable=True),
        CardiacCycle(start_ms=600.0, peak_ms=700.0, end_ms=1100.0, rr_ms=500.0, is_stable=False),
    ]


def build_frames():
    frames = [
        MRIFrame(frame_id=0, timestamp_ms=150.0, dataset=object()),  # stable
        MRIFrame(frame_id=1, timestamp_ms=350.0, dataset=object()),  # stable
        MRIFrame(frame_id=2, timestamp_ms=800.0, dataset=object()),  # unstable cycle
    ]
    return MRIFrameSeries(frames)


def test_filter_engine_accepts_only_stable_frames():
    cycles = build_cycles()
    frames = build_frames()
    engine = FrameFilter(stable_phase_fraction=0.5, stable_phase_offset=0.1)

    result = engine.filter_frames(frames, cycles)

    assert [frame.frame_id for frame in result.accepted_frames] == [0, 1]
    assert [frame.frame_id for frame in result.rejected_frames] == [2]
