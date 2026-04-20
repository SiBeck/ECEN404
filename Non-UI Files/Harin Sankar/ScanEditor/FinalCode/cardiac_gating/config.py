from __future__ import annotations  # Enable future annotations behavior.

from dataclasses import dataclass, field  # Dataclass helpers.
from pathlib import Path  # Path type support.
from typing import Any  # Generic type for raw YAML dict.

import yaml  # YAML parser.


@dataclass
class AlignmentConfig:
    max_offset_ms: float = 2000.0  # Max alignment offset used for warning/guard behavior.


@dataclass
class CycleDetectionConfig:
    window_ms: float = 800.0  # Moving-window size for baseline/noise estimation.
    min_peak_prominence: float = 0.4  # Peak threshold multiplier above local noise.
    stable_rr_variation_pct: float = 5.0  # Allowed RR variation around median.
    min_rr_ms: float = 300.0  # Lower physiologic RR bound.
    max_rr_ms: float = 2000.0  # Upper physiologic RR bound.


@dataclass
class FilteringConfig:
    stable_phase_fraction: float = 0.4  # Fraction of cycle accepted as stable phase.
    stable_phase_offset: float = 0.3  # Offset into cycle where stable phase begins.
    fallback_min_frames: int = 2  # Minimum accepted frames guaranteed via fallback.


@dataclass
class IOConfig:
    default_output_dir: str = "output/gated_dicom"  # Default output folder.
    mrd_frame_period_ms: float = 40.0  # Synthetic timestamp delta between MRD frames.
    mrd_start_time_ms: float = 0.0  # Synthetic timestamp for first frame.
    mrd_apply_hamming_window: bool = False  # Optional k-space Hamming apodization.
    mrd_zero_pad_fe: int | None = None  # Optional FE zero-pad target.
    mrd_zero_pad_pe: int | None = None  # Optional PE zero-pad target.


@dataclass
class ScanFilterConfig:
    alignment: AlignmentConfig = field(default_factory=AlignmentConfig)  # Alignment section.
    cycle_detection: CycleDetectionConfig = field(default_factory=CycleDetectionConfig)  # Cycle section.
    filtering: FilteringConfig = field(default_factory=FilteringConfig)  # Filtering section.
    io: IOConfig = field(default_factory=IOConfig)  # IO section.

    @staticmethod
    def from_dict(raw: dict[str, Any]) -> "ScanFilterConfig":
        return ScanFilterConfig(  # Build typed config from raw nested dict.
            alignment=AlignmentConfig(**raw.get("alignment", {})),
            cycle_detection=CycleDetectionConfig(**raw.get("cycle_detection", {})),
            filtering=FilteringConfig(**raw.get("filtering", {})),
            io=IOConfig(**raw.get("io", {})),
        )

    @classmethod
    def load(cls, path: str | Path) -> "ScanFilterConfig":
        with open(path, "r", encoding="utf-8") as handle:  # Open YAML file.
            data = yaml.safe_load(handle) or {}  # Parse YAML or fallback to empty dict.
        return cls.from_dict(data)  # Convert parsed dict into typed config.
