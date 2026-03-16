from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import List

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from scan_filter.config import ScanFilterConfig
from scan_filter.core.pipeline import ScanFilterPipeline
from examples.create_sample_inputs import generate_cardiogram, generate_dicom_series


@dataclass(frozen=True)
class DemoScenario:
    name: str
    beats: int
    base_rr_ms: float
    frames: int
    spacing_ms: float


SCENARIOS: List[DemoScenario] = [
    DemoScenario(name="set_a_stable", beats=16, base_rr_ms=780.0, frames=12, spacing_ms=180.0),
    DemoScenario(name="set_b_variable", beats=22, base_rr_ms=640.0, frames=18, spacing_ms=140.0),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate demo datasets and run the Scan Filter pipeline")
    parser.add_argument(
        "--root",
        default="examples/demo_runs",
        help="Destination folder that will contain demo inputs and outputs",
    )
    parser.add_argument(
        "--config",
        default="config/default.yaml",
        help="YAML configuration file to apply during demo runs",
    )
    return parser.parse_args()


def run_scenario(pipeline: ScanFilterPipeline, root: Path, scenario: DemoScenario) -> None:
    scenario_root = root / scenario.name
    cardio_path = scenario_root / "cardiogram.csv"
    dicom_dir = scenario_root / "dicom_series"
    filtered_dir = scenario_root / "filtered_series"

    generate_cardiogram(cardio_path, beats=scenario.beats, base_rr_ms=scenario.base_rr_ms)
    generate_dicom_series(dicom_dir, frame_count=scenario.frames, spacing_ms=scenario.spacing_ms)

    artifacts = pipeline.run(cardio_path, dicom_dir, filtered_dir)
    print(
        f"Scenario {scenario.name}: input frames={scenario.frames}, accepted={artifacts.accepted_frames}, "
        f"rejected={artifacts.rejected_frames}, stable_cycles={artifacts.stable_cycles}, output={filtered_dir}",
    )


def main() -> None:
    args = parse_args()
    root = Path(args.root)
    root.mkdir(parents=True, exist_ok=True)

    config = ScanFilterConfig.load(args.config)
    pipeline = ScanFilterPipeline(config)

    for scenario in SCENARIOS:
        run_scenario(pipeline, root, scenario)


if __name__ == "__main__":
    main()
