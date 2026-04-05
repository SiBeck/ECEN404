from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

import numpy as np

from .config import ScanFilterConfig
from .dicom_writer import DicomSeriesWriter
from .logging_config import configure_logging
from .models import CardiacCycle, CardioSeries, MRIFrame, MRIFrameSeries
from .readers import CardiogramCsvReader, MRDSolutionsReader
from .statistics import moving_average, moving_std


@dataclass
class PipelineArtifacts:
    output_dir: Path
    accepted_frames: int
    rejected_frames: int
    stable_cycles: int
    alignment_offset_ms: float


@dataclass
class TimeAlignmentResult:
    aligned_series: MRIFrameSeries
    offset_ms: float


@dataclass
class CycleDetectionSummary:
    cycles: list[CardiacCycle]
    stable_cycles: list[CardiacCycle]


@dataclass
class FilterResult:
    accepted_frames: list[MRIFrame]
    rejected_frames: list[MRIFrame]
    stable_cycle_count: int


class TimeAligner:
    def __init__(self, max_offset_ms: float) -> None:
        self.max_offset_ms = max_offset_ms

    def align(self, cardio: CardioSeries, frames: MRIFrameSeries) -> TimeAlignmentResult:
        cardio_ts = cardio.timestamps
        frame_ts = frames.timestamps
        if cardio_ts.size == 0 or frame_ts.size == 0:
            raise ValueError("Cannot align empty series")
        first_frame_time = frame_ts[0]
        nearest_idx = int(np.argmin(np.abs(cardio_ts - first_frame_time)))
        offset_ms = float(cardio_ts[nearest_idx] - first_frame_time)
        aligned_series = frames.with_offset(offset_ms)
        return TimeAlignmentResult(aligned_series=aligned_series, offset_ms=offset_ms)


class CycleDetector:
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
        signal = cardio.values
        sample_period_ms = cardio.estimate_sample_period_ms()
        window_samples = max(5, int(self.window_ms / max(sample_period_ms, 1.0)))

        baseline = moving_average(signal, window_samples)
        noise = moving_std(signal, window_samples)
        threshold = baseline + self.min_peak_prominence * np.maximum(noise, 1e-6)

        min_distance_samples = max(1, int(self.min_rr_ms / max(sample_period_ms, 1.0)))
        peaks = self._detect_peaks(signal, threshold, min_distance_samples)
        if len(peaks) < 2:
            return CycleDetectionSummary([], [])

        rr_intervals = np.diff(timestamps[peaks])
        valid_rr = rr_intervals[(rr_intervals >= self.min_rr_ms) & (rr_intervals <= self.max_rr_ms)]
        if valid_rr.size:
            rr_median = float(np.median(valid_rr))
        elif rr_intervals.size:
            rr_median = float(np.median(rr_intervals))
        else:
            rr_median = 0.0

        cycles: list[CardiacCycle] = []
        for left_idx, right_idx in zip(peaks[:-1], peaks[1:]):
            rr_ms = float(timestamps[right_idx] - timestamps[left_idx])
            is_rr_valid = self.min_rr_ms <= rr_ms <= self.max_rr_ms
            if not is_rr_valid or rr_median <= 0:
                is_stable = False
            else:
                allowed_variation = rr_median * (self.stable_rr_variation_pct / 100.0)
                is_stable = abs(rr_ms - rr_median) <= allowed_variation

            start_idx = max(0, left_idx - (right_idx - left_idx) // 3)
            end_idx = min(len(signal) - 1, right_idx + (right_idx - left_idx) // 3)
            cycles.append(
                CardiacCycle(
                    start_ms=float(timestamps[start_idx]),
                    peak_ms=float(timestamps[left_idx]),
                    end_ms=float(timestamps[end_idx]),
                    rr_ms=rr_ms,
                    is_stable=is_stable,
                )
            )

        stable_cycles = [cycle for cycle in cycles if cycle.is_stable]
        return CycleDetectionSummary(cycles, stable_cycles)

    @staticmethod
    def _detect_peaks(signal: np.ndarray, threshold: np.ndarray, min_distance: int) -> list[int]:
        peaks: list[int] = []
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


class FrameFilter:
    def __init__(self, stable_phase_fraction: float, stable_phase_offset: float) -> None:
        self.stable_phase_fraction = stable_phase_fraction
        self.stable_phase_offset = stable_phase_offset

    def filter_frames(self, frames: MRIFrameSeries, cycles: Sequence[CardiacCycle]) -> FilterResult:
        if not cycles:
            return FilterResult([], list(frames), 0)

        accepted: list[MRIFrame] = []
        rejected: list[MRIFrame] = []
        for frame in frames:
            if self._is_in_stable_phase(frame.timestamp_ms, cycles):
                accepted.append(frame)
            else:
                rejected.append(frame)
        stable_cycle_count = sum(1 for cycle in cycles if cycle.is_stable)
        return FilterResult(accepted, rejected, stable_cycle_count)

    def _is_in_stable_phase(self, timestamp_ms: float, cycles: Sequence[CardiacCycle]) -> bool:
        for cycle in cycles:
            if not cycle.is_stable:
                continue
            duration = max(cycle.duration_ms, 1.0)
            phase_start = cycle.start_ms + self.stable_phase_offset * duration
            phase_end = min(cycle.end_ms, phase_start + self.stable_phase_fraction * duration)
            if phase_start <= timestamp_ms <= phase_end:
                return True
        return False


class ScanFilterPipeline:
    def __init__(
        self,
        config: ScanFilterConfig,
        csv_reader: CardiogramCsvReader | None = None,
        dicom_writer: DicomSeriesWriter | None = None,
        mrd_reader: MRDSolutionsReader | None = None,
    ) -> None:
        self.config = config
        self.logger = configure_logging()
        self.csv_reader = csv_reader or CardiogramCsvReader()
        self.dicom_writer = dicom_writer or DicomSeriesWriter()
        io_cfg = config.io
        zero_pad_shape = None
        if io_cfg.mrd_zero_pad_fe is not None and io_cfg.mrd_zero_pad_pe is not None:
            zero_pad_shape = (int(io_cfg.mrd_zero_pad_fe), int(io_cfg.mrd_zero_pad_pe))
        self.mrd_reader = mrd_reader or MRDSolutionsReader(
            frame_period_ms=io_cfg.mrd_frame_period_ms,
            start_time_ms=io_cfg.mrd_start_time_ms,
            apply_hamming_window=io_cfg.mrd_apply_hamming_window,
            zero_pad_shape=zero_pad_shape,
        )
        self.aligner = TimeAligner(config.alignment.max_offset_ms)
        self.cycle_detector = CycleDetector(
            window_ms=config.cycle_detection.window_ms,
            min_peak_prominence=config.cycle_detection.min_peak_prominence,
            stable_rr_variation_pct=config.cycle_detection.stable_rr_variation_pct,
            min_rr_ms=config.cycle_detection.min_rr_ms,
            max_rr_ms=config.cycle_detection.max_rr_ms,
        )
        self.frame_filter = FrameFilter(
            stable_phase_fraction=config.filtering.stable_phase_fraction,
            stable_phase_offset=config.filtering.stable_phase_offset,
        )

    def run(
        self,
        cardiogram_csv: str | Path,
        mrd_file: str | Path,
        output_dir: str | Path | None = None,
    ) -> PipelineArtifacts:
        self.logger.info("Loading cardiogram data from %s", cardiogram_csv)
        cardio = self.csv_reader.read(cardiogram_csv)

        self.logger.info("Loading MRD scan from %s", mrd_file)
        frames = self.mrd_reader.read(mrd_file)

        self.logger.info("Aligning cardiogram and MRD frame timelines")
        alignment = self.aligner.align(cardio, frames)

        self.logger.info("Detecting stable cardiac cycles")
        cycle_summary = self.cycle_detector.detect(cardio)

        self.logger.info("Filtering frames inside stable cardiac phases")
        filter_result = self.frame_filter.filter_frames(alignment.aligned_series, cycle_summary.stable_cycles)

        accepted_frames = list(filter_result.accepted_frames)
        rejected_frames = list(filter_result.rejected_frames)
        accepted_frames, rejected_frames = self._ensure_minimum_frames(
            alignment.aligned_series,
            accepted_frames,
            rejected_frames,
            self.config.filtering.fallback_min_frames,
        )

        destination = Path(output_dir or self.config.io.default_output_dir)
        self.logger.info("Writing %s accepted frame(s) to %s", len(accepted_frames), destination)
        written_dir = self.dicom_writer.write(accepted_frames, destination)

        return PipelineArtifacts(
            output_dir=written_dir,
            accepted_frames=len(accepted_frames),
            rejected_frames=len(rejected_frames),
            stable_cycles=len(cycle_summary.stable_cycles),
            alignment_offset_ms=alignment.offset_ms,
        )

    def _ensure_minimum_frames(
        self,
        aligned_series: MRIFrameSeries,
        accepted_frames: list[MRIFrame],
        rejected_frames: list[MRIFrame],
        minimum: int,
    ) -> tuple[list[MRIFrame], list[MRIFrame]]:
        threshold = max(0, int(minimum))
        if threshold == 0 or len(accepted_frames) >= threshold:
            return accepted_frames, rejected_frames

        available = sorted(list(aligned_series), key=lambda frame: frame.timestamp_ms)
        if not available:
            return accepted_frames, rejected_frames

        accepted_ids = {id(frame) for frame in accepted_frames}
        added: list[MRIFrame] = []
        for frame in available:
            if id(frame) in accepted_ids:
                continue
            accepted_frames.append(frame)
            accepted_ids.add(id(frame))
            added.append(frame)
            if len(accepted_frames) >= threshold:
                break

        if added:
            self.logger.warning(
                "Stable-phase filter yielded fewer than %s frames; injecting %s fallback frame(s)",
                threshold,
                len(added),
            )
            added_ids = {id(frame) for frame in added}
            rejected_frames = [frame for frame in rejected_frames if id(frame) not in added_ids]

        return accepted_frames, rejected_frames