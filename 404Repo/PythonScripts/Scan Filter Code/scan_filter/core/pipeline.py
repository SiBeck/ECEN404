from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from ..config import ScanFilterConfig
from ..logging_config import configure_logging
from ..io.csv_reader import CardiogramCsvReader
from ..io.dicom_reader import DicomSeriesReader
from ..io.dicom_writer import DicomSeriesWriter
from .aligner import TimeAligner
from .cycle_detection import CycleDetector
from .filter_engine import FrameFilter
from .data_models import MRIFrame, MRIFrameSeries


@dataclass
class PipelineArtifacts:
    output_dir: Path
    accepted_frames: int
    rejected_frames: int
    stable_cycles: int
    alignment_offset_ms: float


class ScanFilterPipeline:
    def __init__(
        self,
        config: ScanFilterConfig,
        csv_reader: Optional[CardiogramCsvReader] = None,
        dicom_reader: Optional[DicomSeriesReader] = None,
        dicom_writer: Optional[DicomSeriesWriter] = None,
    ) -> None:
        self.config = config
        self.logger = configure_logging()
        self.csv_reader = csv_reader or CardiogramCsvReader()
        self.dicom_reader = dicom_reader or DicomSeriesReader()
        self.dicom_writer = dicom_writer or DicomSeriesWriter()
        self.aligner = TimeAligner(config.alignment.max_offset_ms)
        cd = config.cycle_detection
        self.cycle_detector = CycleDetector(
            window_ms=cd.window_ms,
            min_peak_prominence=cd.min_peak_prominence,
            stable_rr_variation_pct=cd.stable_rr_variation_pct,
            min_rr_ms=cd.min_rr_ms,
            max_rr_ms=cd.max_rr_ms,
        )
        fcfg = config.filtering
        self.frame_filter = FrameFilter(
            stable_phase_fraction=fcfg.stable_phase_fraction,
            stable_phase_offset=fcfg.stable_phase_offset,
        )

    def run(self, cardiogram_csv: str | Path, dicom_folder: str | Path, output_dir: Optional[str | Path] = None) -> PipelineArtifacts:
        self.logger.info("Loading cardiogram data from %s", cardiogram_csv)
        cardio = self.csv_reader.read(cardiogram_csv)

        self.logger.info("Loading DICOM series from %s", dicom_folder)
        frames = self.dicom_reader.read(dicom_folder)

        self.logger.info("Aligning timelines")
        alignment = self.aligner.align(cardio, frames)

        self.logger.info("Detecting stable cardiac cycles")
        cycles_summary = self.cycle_detector.detect(cardio)

        self.logger.info("Filtering frames that fall into stable phases")
        filter_results = self.frame_filter.filter_frames(alignment.aligned_series, cycles_summary.stable_cycles)

        accepted_frames = list(filter_results.accepted_frames)
        rejected_frames = list(filter_results.rejected_frames)
        accepted_frames, rejected_frames = self._ensure_minimum_frames(
            alignment.aligned_series,
            accepted_frames,
            rejected_frames,
            getattr(self.config.filtering, "fallback_min_frames", 0),
        )

        destination = Path(output_dir or self.config.io.default_output_dir)
        self.logger.info("Writing %s accepted frames to %s", len(accepted_frames), destination)
        written_dir = self.dicom_writer.write(accepted_frames, destination)

        return PipelineArtifacts(
            output_dir=written_dir,
            accepted_frames=len(accepted_frames),
            rejected_frames=len(rejected_frames),
            stable_cycles=len(cycles_summary.stable_cycles),
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
