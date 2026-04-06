from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

try:
    import yaml
except ImportError:
    yaml = None


@dataclass
class AlignmentConfig:
    max_offset_ms: float = 2000.0


@dataclass
class CycleDetectionConfig:
    window_ms: float = 800.0
    min_peak_prominence: float = 0.4
    stable_rr_variation_pct: float = 5.0
    min_rr_ms: float = 300.0
    max_rr_ms: float = 2000.0


@dataclass
class FilteringConfig:
    stable_phase_fraction: float = 0.4
    stable_phase_offset: float = 0.3
    fallback_min_frames: int = 2


@dataclass
class IOConfig:
    default_output_dir: str = "output/gated_dicom"
    mrd_frame_period_ms: float = 40.0
    mrd_start_time_ms: float = 0.0
    mrd_apply_hamming_window: bool = False
    mrd_zero_pad_fe: int | None = None
    mrd_zero_pad_pe: int | None = None


@dataclass
class ScanFilterConfig:
    alignment: AlignmentConfig = field(default_factory=AlignmentConfig)
    cycle_detection: CycleDetectionConfig = field(default_factory=CycleDetectionConfig)
    filtering: FilteringConfig = field(default_factory=FilteringConfig)
    io: IOConfig = field(default_factory=IOConfig)

    @staticmethod
    def from_dict(raw: dict[str, Any]) -> "ScanFilterConfig":
        return ScanFilterConfig(
            alignment=AlignmentConfig(**raw.get("alignment", {})),
            cycle_detection=CycleDetectionConfig(**raw.get("cycle_detection", {})),
            filtering=FilteringConfig(**raw.get("filtering", {})),
            io=IOConfig(**raw.get("io", {})),
        )

    @classmethod
    def load(cls, path: str | Path) -> "ScanFilterConfig":
        if yaml is None:
            raise ImportError(
                "pyyaml is required to load YAML config files. "
                "Install it with: pip install pyyaml"
            )
        with open(path, "r", encoding="utf-8") as handle:
            data = yaml.safe_load(handle) or {}
        return cls.from_dict(data)