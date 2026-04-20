from __future__ import annotations  # Future annotation behavior.

from dataclasses import dataclass  # Dataclass helper.
from pathlib import Path  # Path helper.
from typing import Sequence  # Sequence typing.

import numpy as np  # Numeric operations.

from .config import ScanFilterConfig  # Config model.
from .dicom_writer import DicomSeriesWriter  # DICOM writer dependency.
from .logging_config import configure_logging  # Logger configuration helper.
from .models import CardiacCycle, CardioSeries, MRIFrame, MRIFrameSeries  # Domain models.
from .readers import CardiogramCsvReader, MRDSolutionsReader  # Input readers.
from .statistics import moving_average, moving_std  # Signal helper functions.


@dataclass
class PipelineArtifacts:
    output_dir: Path  # Destination output directory.
    accepted_frames: int  # Count of accepted frames.
    rejected_frames: int  # Count of rejected frames.
    stable_cycles: int  # Count of stable cycles.
    alignment_offset_ms: float  # Applied alignment offset.


@dataclass
class TimeAlignmentResult:
    aligned_series: MRIFrameSeries  # Timestamp-shifted frame series.
    offset_ms: float  # Applied offset.


@dataclass
class CycleDetectionSummary:
    cycles: list[CardiacCycle]  # All detected cycles.
    stable_cycles: list[CardiacCycle]  # Stable cycle subset.


@dataclass
class FilterResult:
    accepted_frames: list[MRIFrame]  # Accepted frame list.
    rejected_frames: list[MRIFrame]  # Rejected frame list.
    stable_cycle_count: int  # Stable cycle count summary.


class TimeAligner:
    def __init__(self, max_offset_ms: float) -> None:
        self.max_offset_ms = max_offset_ms  # Store configured max offset (currently warning/metadata role).

    def align(self, cardio: CardioSeries, frames: MRIFrameSeries) -> TimeAlignmentResult:
        cardio_ts = cardio.timestamps  # Cardiogram timestamps.
        frame_ts = frames.timestamps  # Frame timestamps.
        if cardio_ts.size == 0 or frame_ts.size == 0:  # Require non-empty inputs.
            raise ValueError("Cannot align empty series")
        first_frame_time = frame_ts[0]  # Alignment anchor (first frame).
        nearest_idx = int(np.argmin(np.abs(cardio_ts - first_frame_time)))  # Closest cardiogram sample index.
        offset_ms = float(cardio_ts[nearest_idx] - first_frame_time)  # Shift needed to align anchors.
        aligned_series = frames.with_offset(offset_ms)  # Shift all frame timestamps.
        return TimeAlignmentResult(aligned_series=aligned_series, offset_ms=offset_ms)  # Return alignment result.


class CycleDetector:
    def __init__(
        self,
        window_ms: float,
        min_peak_prominence: float,
        stable_rr_variation_pct: float,
        min_rr_ms: float,
        max_rr_ms: float,
    ) -> None:
        self.window_ms = window_ms  # Baseline/noise smoothing window in ms.
        self.min_peak_prominence = min_peak_prominence  # Adaptive threshold multiplier.
        self.stable_rr_variation_pct = stable_rr_variation_pct  # Allowed RR drift percentage.
        self.min_rr_ms = min_rr_ms  # Lower RR bound.
        self.max_rr_ms = max_rr_ms  # Upper RR bound.

    def detect(self, cardio: CardioSeries) -> CycleDetectionSummary:
        timestamps = cardio.timestamps  # Time axis.
        signal = cardio.values  # Signal axis.
        sample_period_ms = cardio.estimate_sample_period_ms()  # Estimated sampling period.
        window_samples = max(5, int(self.window_ms / max(sample_period_ms, 1.0)))  # Window size in samples.

        baseline = moving_average(signal, window_samples)  # Local signal baseline.
        noise = moving_std(signal, window_samples)  # Local signal noise estimate.
        threshold = baseline + self.min_peak_prominence * np.maximum(noise, 1e-6)  # Adaptive threshold.

        min_distance_samples = max(1, int(self.min_rr_ms / max(sample_period_ms, 1.0)))  # Peak refractory distance.
        peaks = self._detect_peaks(signal, threshold, min_distance_samples)  # Peak indices.
        if len(peaks) < 2:  # Need at least two peaks for cycle creation.
            return CycleDetectionSummary([], [])

        rr_intervals = np.diff(timestamps[peaks])  # Beat-to-beat intervals.
        valid_rr = rr_intervals[(rr_intervals >= self.min_rr_ms) & (rr_intervals <= self.max_rr_ms)]  # Physiologic RR subset.
        if valid_rr.size:
            rr_median = float(np.median(valid_rr))  # Median RR from valid subset.
        elif rr_intervals.size:
            rr_median = float(np.median(rr_intervals))  # Fallback median from all RR.
        else:
            rr_median = 0.0  # Safety fallback.

        cycles: list[CardiacCycle] = []  # Output cycle list.
        for left_idx, right_idx in zip(peaks[:-1], peaks[1:]):  # Build one cycle per adjacent peak pair.
            rr_ms = float(timestamps[right_idx] - timestamps[left_idx])  # Cycle RR interval.
            is_rr_valid = self.min_rr_ms <= rr_ms <= self.max_rr_ms  # RR validity check.
            if not is_rr_valid or rr_median <= 0:  # Invalid RR or no median baseline.
                is_stable = False
            else:
                allowed_variation = rr_median * (self.stable_rr_variation_pct / 100.0)  # Convert percent to ms tolerance.
                is_stable = abs(rr_ms - rr_median) <= allowed_variation  # Stability decision.

            start_idx = max(0, left_idx - (right_idx - left_idx) // 3)  # Extend cycle start before peak.
            end_idx = min(len(signal) - 1, right_idx + (right_idx - left_idx) // 3)  # Extend cycle end after peak.
            cycles.append(
                CardiacCycle(
                    start_ms=float(timestamps[start_idx]),
                    peak_ms=float(timestamps[left_idx]),
                    end_ms=float(timestamps[end_idx]),
                    rr_ms=rr_ms,
                    is_stable=is_stable,
                )
            )

        stable_cycles = [cycle for cycle in cycles if cycle.is_stable]  # Keep stable cycles only.
        return CycleDetectionSummary(cycles, stable_cycles)  # Return full and stable subsets.

    @staticmethod
    def _detect_peaks(signal: np.ndarray, threshold: np.ndarray, min_distance: int) -> list[int]:
        peaks: list[int] = []  # Peak index accumulator.
        for idx in range(1, len(signal) - 1):  # Skip boundaries for local-max test.
            if signal[idx] <= threshold[idx]:  # Must exceed adaptive threshold.
                continue
            if signal[idx] > signal[idx - 1] and signal[idx] >= signal[idx + 1]:  # Local maximum condition.
                if peaks and idx - peaks[-1] < min_distance:  # Too close to prior peak.
                    if signal[idx] > signal[peaks[-1]]:  # Keep stronger peak inside refractory region.
                        peaks[-1] = idx
                else:
                    peaks.append(idx)  # Accept new peak.
        return peaks  # Return detected peak indices.


class FrameFilter:
    def __init__(self, stable_phase_fraction: float, stable_phase_offset: float) -> None:
        self.stable_phase_fraction = stable_phase_fraction  # Width of stable window within cycle.
        self.stable_phase_offset = stable_phase_offset  # Start position of stable window within cycle.

    def filter_frames(self, frames: MRIFrameSeries, cycles: Sequence[CardiacCycle]) -> FilterResult:
        if not cycles:  # No cycles means no stable-window classification possible.
            return FilterResult([], list(frames), 0)

        accepted: list[MRIFrame] = []  # Accepted frame bucket.
        rejected: list[MRIFrame] = []  # Rejected frame bucket.
        for frame in frames:  # Evaluate each frame timestamp.
            if self._is_in_stable_phase(frame.timestamp_ms, cycles):
                accepted.append(frame)  # Keep frame.
            else:
                rejected.append(frame)  # Reject frame.
        stable_cycle_count = sum(1 for cycle in cycles if cycle.is_stable)  # Stable cycle count.
        return FilterResult(accepted, rejected, stable_cycle_count)  # Return filtering result.

    def _is_in_stable_phase(self, timestamp_ms: float, cycles: Sequence[CardiacCycle]) -> bool:
        for cycle in cycles:  # Check each cycle.
            if not cycle.is_stable:  # Ignore unstable cycles.
                continue
            duration = max(cycle.duration_ms, 1.0)  # Guard against zero/negative durations.
            phase_start = cycle.start_ms + self.stable_phase_offset * duration  # Stable phase start.
            phase_end = min(cycle.end_ms, phase_start + self.stable_phase_fraction * duration)  # Stable phase end.
            if phase_start <= timestamp_ms <= phase_end:  # Inclusion check.
                return True
        return False  # Not inside any stable phase window.


class ScanFilterPipeline:
    def __init__(
        self,
        config: ScanFilterConfig,
        csv_reader: CardiogramCsvReader | None = None,
        dicom_writer: DicomSeriesWriter | None = None,
        mrd_reader: MRDSolutionsReader | None = None,
    ) -> None:
        self.config = config  # Save full config object.
        self.logger = configure_logging()  # Initialize package logger.
        self.csv_reader = csv_reader or CardiogramCsvReader()  # CSV reader dependency.
        self.dicom_writer = dicom_writer or DicomSeriesWriter()  # DICOM writer dependency.
        io_cfg = config.io  # IO config alias.
        zero_pad_shape = None  # Optional zero-pad shape tuple.
        if io_cfg.mrd_zero_pad_fe is not None and io_cfg.mrd_zero_pad_pe is not None:  # Require both dims for zero-padding.
            zero_pad_shape = (int(io_cfg.mrd_zero_pad_fe), int(io_cfg.mrd_zero_pad_pe))
        self.mrd_reader = mrd_reader or MRDSolutionsReader(  # MRD reader dependency.
            frame_period_ms=io_cfg.mrd_frame_period_ms,
            start_time_ms=io_cfg.mrd_start_time_ms,
            apply_hamming_window=io_cfg.mrd_apply_hamming_window,
            zero_pad_shape=zero_pad_shape,
        )
        self.aligner = TimeAligner(config.alignment.max_offset_ms)  # Alignment stage.
        self.cycle_detector = CycleDetector(  # Cycle detection stage.
            window_ms=config.cycle_detection.window_ms,
            min_peak_prominence=config.cycle_detection.min_peak_prominence,
            stable_rr_variation_pct=config.cycle_detection.stable_rr_variation_pct,
            min_rr_ms=config.cycle_detection.min_rr_ms,
            max_rr_ms=config.cycle_detection.max_rr_ms,
        )
        self.frame_filter = FrameFilter(  # Frame filter stage.
            stable_phase_fraction=config.filtering.stable_phase_fraction,
            stable_phase_offset=config.filtering.stable_phase_offset,
        )

    def run(
        self,
        cardiogram_csv: str | Path,
        mrd_file: str | Path,
        output_dir: str | Path | None = None,
    ) -> PipelineArtifacts:
        self.logger.info("Loading cardiogram data from %s", cardiogram_csv)  # Stage log.
        cardio = self.csv_reader.read(cardiogram_csv)  # Read cardiogram.

        self.logger.info("Loading MRD scan from %s", mrd_file)  # Stage log.
        frames = self.mrd_reader.read(mrd_file)  # Read frame series.

        self.logger.info("Aligning cardiogram and MRD frame timelines")  # Stage log.
        alignment = self.aligner.align(cardio, frames)  # Align timelines.

        self.logger.info("Detecting stable cardiac cycles")  # Stage log.
        cycle_summary = self.cycle_detector.detect(cardio)  # Detect cycles.

        self.logger.info("Filtering frames inside stable cardiac phases")  # Stage log.
        filter_result = self.frame_filter.filter_frames(alignment.aligned_series, cycle_summary.stable_cycles)  # Filter frames.

        accepted_frames = list(filter_result.accepted_frames)  # Copy mutable accepted list.
        rejected_frames = list(filter_result.rejected_frames)  # Copy mutable rejected list.
        accepted_frames, rejected_frames = self._ensure_minimum_frames(  # Apply fallback minimum-frame policy.
            alignment.aligned_series,
            accepted_frames,
            rejected_frames,
            self.config.filtering.fallback_min_frames,
        )

        destination = Path(output_dir or self.config.io.default_output_dir)  # Resolve destination path.
        self.logger.info("Writing %s accepted frame(s) to %s", len(accepted_frames), destination)  # Stage log.
        written_dir = self.dicom_writer.write(accepted_frames, destination)  # Write accepted frames as DICOM.

        return PipelineArtifacts(  # Return run summary.
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
        threshold = max(0, int(minimum))  # Clamp minimum to non-negative integer.
        if threshold == 0 or len(accepted_frames) >= threshold:  # Fallback not needed.
            return accepted_frames, rejected_frames

        available = sorted(list(aligned_series), key=lambda frame: frame.timestamp_ms)  # Deterministic chronological order.
        if not available:  # No candidate frames available.
            return accepted_frames, rejected_frames

        accepted_ids = {id(frame) for frame in accepted_frames}  # Identity set for duplicate prevention.
        added: list[MRIFrame] = []  # Tracks frames injected by fallback.
        for frame in available:  # Scan frames in chronological order.
            if id(frame) in accepted_ids:  # Skip already accepted frame.
                continue
            accepted_frames.append(frame)  # Inject fallback frame.
            accepted_ids.add(id(frame))  # Mark as accepted.
            added.append(frame)  # Track injection.
            if len(accepted_frames) >= threshold:  # Stop after reaching minimum target.
                break

        if added:  # If fallback injected frames, update logs and rejected list.
            self.logger.warning(
                "Stable-phase filter yielded fewer than %s frames; injecting %s fallback frame(s)",
                threshold,
                len(added),
            )
            added_ids = {id(frame) for frame in added}  # Identity set of injected frames.
            rejected_frames = [frame for frame in rejected_frames if id(frame) not in added_ids]  # Remove injected frames from rejected bucket.

        return accepted_frames, rejected_frames  # Return adjusted frame lists.
