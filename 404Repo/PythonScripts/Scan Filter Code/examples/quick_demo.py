from __future__ import annotations

import argparse
import sys
import time
from dataclasses import dataclass
from pathlib import Path
import os
import subprocess
from typing import Dict, Iterable, List, Tuple

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

import pytest  # type: ignore

from scan_filter.config import ScanFilterConfig
from scan_filter.core.pipeline import PipelineArtifacts, ScanFilterPipeline
from examples.create_sample_inputs import (
    CARDIO_FILENAME,
    DICOM_DIRNAME,
    generate_cardiogram,
    generate_dicom_series,
)
from examples.demo_pipeline import DemoScenario, SCENARIOS
from examples.visualize_run import (
    export_frame_previews,
    load_frames,
    plot_cardiogram_with_frames,
    reader as viz_reader,
    write_summary,
)


@dataclass(frozen=True)
class BaselineResult:
    cardiogram_path: Path
    dicom_dir: Path
    output_dir: Path
    artifacts: PipelineArtifacts


@dataclass(frozen=True)
class ScenarioResult:
    scenario: DemoScenario
    cardiogram_path: Path
    dicom_dir: Path
    filtered_dir: Path
    artifacts: PipelineArtifacts


@dataclass(frozen=True)
class TestSummary:
    passed: bool
    duration_s: float


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the entire Scan Filter demo with one command")
    parser.add_argument("--config", default="config/default.yaml", help="Path to YAML config for the pipeline")
    parser.add_argument(
        "--baseline-root",
        default="examples/data",
        help="Where to store the baseline cardiogram/DICOM inputs",
    )
    parser.add_argument(
        "--baseline-output",
        default="output/sample_filtered",
        help="Destination for baseline filtered frames",
    )
    parser.add_argument(
        "--demo-root",
        default="examples/demo_runs",
        help="Folder that will contain scenario inputs, outputs, and visuals",
    )
    parser.add_argument(
        "--skip-visuals",
        action="store_true",
        help="Skip visualization export to save time",
    )
    parser.add_argument(
        "--skip-tests",
        action="store_true",
        help="Skip pytest execution at the end",
    )
    parser.add_argument(
        "--skip-scenarios",
        action="store_true",
        help="Skip generating multi-scenario datasets (baseline still runs)",
    )
    parser.add_argument(
        "--scenario",
        action="append",
        choices=[scenario.name for scenario in SCENARIOS],
        help="Run only these demo scenario names (repeat flag for multiple)",
    )
    parser.add_argument(
        "--lightning",
        action="store_true",
        help="Optimize defaults for a <4 minute demo (baseline only, visuals/tests skipped)",
    )
    parser.add_argument(
        "--report",
        default="demo_report.html",
        help="Path for the HTML report that summarizes visuals and validation",
    )
    parser.add_argument(
        "--skip-report",
        action="store_true",
        help="Skip generating the HTML report",
    )
    parser.add_argument(
        "--auto-open",
        action="store_true",
        help="After completion, open the HTML report (if created) and each scenario's visuals folder",
    )
    parser.add_argument(
        "--auto-open-report",
        action="store_true",
        help="Automatically open the generated HTML report",
    )
    parser.add_argument(
        "--auto-open-visuals",
        action="store_true",
        help="Automatically open each scenario's visuals directory",
    )
    parser.add_argument(
        "--max-frame-previews",
        type=int,
        default=2,
        help="Number of accepted/rejected frame previews to export per scenario",
    )
    return parser.parse_args()


def prepare_baseline_run(pipeline: ScanFilterPipeline, baseline_root: Path, baseline_output: Path) -> BaselineResult:
    baseline_root.mkdir(parents=True, exist_ok=True)
    cardio_path = baseline_root / CARDIO_FILENAME
    dicom_dir = baseline_root / DICOM_DIRNAME

    print("[1/4] Generating baseline sample inputs...")
    generate_cardiogram(cardio_path)
    generate_dicom_series(dicom_dir)

    print("[2/4] Executing baseline pipeline run...")
    artifacts = pipeline.run(cardio_path, dicom_dir, baseline_output)
    print(
        f"Baseline complete: accepted={artifacts.accepted_frames} rejected={artifacts.rejected_frames} "
        f"stable_cycles={artifacts.stable_cycles} output={artifacts.output_dir}\n",
    )
    return BaselineResult(cardiogram_path=cardio_path, dicom_dir=dicom_dir, output_dir=artifacts.output_dir, artifacts=artifacts)


def run_demo_scenarios(
    pipeline: ScanFilterPipeline,
    demo_root: Path,
    scenarios: Iterable[DemoScenario],
) -> List[ScenarioResult]:
    print("[3/4] Building multi-scenario demo datasets...")
    demo_root.mkdir(parents=True, exist_ok=True)
    results: List[ScenarioResult] = []
    for scenario in scenarios:
        scenario_root = demo_root / scenario.name
        scenario_root.mkdir(parents=True, exist_ok=True)
        cardio_path = scenario_root / "cardiogram.csv"
        dicom_dir = scenario_root / "dicom_series"
        filtered_dir = scenario_root / "filtered_series"
        generate_cardiogram(cardio_path, beats=scenario.beats, base_rr_ms=scenario.base_rr_ms)
        generate_dicom_series(dicom_dir, frame_count=scenario.frames, spacing_ms=scenario.spacing_ms)
        artifacts = pipeline.run(cardio_path, dicom_dir, filtered_dir)
        print(
            f"Scenario {scenario.name}: frames={scenario.frames} accepted={artifacts.accepted_frames} "
            f"rejected={artifacts.rejected_frames} stable_cycles={artifacts.stable_cycles}",
        )
        results.append(
            ScenarioResult(
                scenario=scenario,
                cardiogram_path=cardio_path,
                dicom_dir=dicom_dir,
                filtered_dir=filtered_dir,
                artifacts=artifacts,
            ),
        )
    print()
    return results


def create_visuals(result: ScenarioResult, max_frame_previews: int) -> List[Path]:
    visuals_dir = result.filtered_dir.parent / "visuals"
    visuals_dir.mkdir(parents=True, exist_ok=True)
    cardio_df = viz_reader.read(result.cardiogram_path).to_dataframe()
    filtered_names = {path.name for path in result.filtered_dir.glob("*.dcm")}
    frames = load_frames(result.dicom_dir, filtered_names)
    artifacts: List[Path] = []
    artifacts.append(plot_cardiogram_with_frames(cardio_df, frames, visuals_dir))
    artifacts.extend(export_frame_previews(frames, visuals_dir, max_frame_previews))
    artifacts.append(write_summary(frames, visuals_dir))
    return artifacts


def run_visualizations(results: List[ScenarioResult], max_frame_previews: int) -> Tuple[Dict[str, List[Path]], Dict[str, Path]]:
    print("[4/4] Exporting visuals...")
    visuals: Dict[str, List[Path]] = {}
    visual_dirs: Dict[str, Path] = {}
    for result in results:
        artifacts = create_visuals(result, max_frame_previews)
        visuals[result.scenario.name] = artifacts
        visual_dirs[result.scenario.name] = result.filtered_dir.parent / "visuals"
        print(f"Visuals for {result.scenario.name}:")
        for artifact in artifacts:
            print(f" - {artifact}")
    print()
    return visuals, visual_dirs


def run_tests() -> TestSummary:
    print("Running pytest for extra assurance...")
    start = time.perf_counter()
    exit_code = pytest.main(["-q"])
    duration = time.perf_counter() - start
    if exit_code != 0:
        raise SystemExit(exit_code)
    print(f"Tests succeeded in {duration:.2f}s\n")
    return TestSummary(passed=True, duration_s=duration)


def rel_path(path: Path, base: Path) -> str:
    return os.path.relpath(path, base).replace("\\", "/")


def auto_open_path(path: Path) -> None:
    try:
        if os.name == "nt":
            os.startfile(path)  # type: ignore[attr-defined]
        elif sys.platform == "darwin":
            subprocess.run(["open", str(path)], check=False)
        else:
            subprocess.run(["xdg-open", str(path)], check=False)
    except Exception as exc:  # pragma: no cover - best-effort UX helper
        print(f"Warning: could not open {path}: {exc}")


def generate_report(
    destination: Path,
    baseline: BaselineResult,
    scenarios: List[ScenarioResult],
    visuals: Dict[str, List[Path]],
    test_summary: TestSummary | None,
) -> Path:
    destination.parent.mkdir(parents=True, exist_ok=True)
    lines: List[str] = []
    lines.append("<!DOCTYPE html>")
    lines.append("<html lang='en'><head><meta charset='utf-8'><title>Scan Filter Demo Report</title>")
    lines.append(
        "<style>body{font-family:Segoe UI,Arial,sans-serif;padding:24px;background:#f8fafc;color:#0f172a;}h1{margin-top:0;}"
        "section{margin-bottom:32px;padding:20px;background:#fff;border-radius:12px;box-shadow:0 4px 16px rgba(15,23,42,0.08);}"
        "table{border-collapse:collapse;width:100%;margin-top:12px;}th,td{border:1px solid #e2e8f0;padding:8px;text-align:left;}"
        "img{max-width:480px;margin:8px 16px 0 0;border-radius:8px;border:1px solid #cbd5f5;}"
        "code{background:#e2e8f0;padding:2px 4px;border-radius:4px;}</style></head><body>",
    )
    lines.append("<h1>Scan Filter Demo Report</h1>")
    lines.append("<p>Generated at {}.</p>".format(time.strftime("%Y-%m-%d %H:%M:%S")))

    lines.append("<section><h2>Baseline Run</h2>")
    lines.append("<p>Input cardiogram + DICOM set processed into <code>{}</code>.</p>".format(rel_path(baseline.artifacts.output_dir, destination.parent)))
    lines.append("<table><tr><th>Accepted</th><th>Rejected</th><th>Stable cycles</th><th>Alignment offset (ms)</th></tr>")
    lines.append(
        "<tr><td>{}</td><td>{}</td><td>{}</td><td>{:.1f}</td></tr></table>".format(
            baseline.artifacts.accepted_frames,
            baseline.artifacts.rejected_frames,
            baseline.artifacts.stable_cycles,
            baseline.artifacts.alignment_offset_ms,
        ),
    )
    lines.append("</section>")

    if scenarios:
        lines.append("<section><h2>Demo Scenarios</h2>")
        for result in scenarios:
            lines.append(f"<h3>{result.scenario.name}</h3>")
            lines.append(
                "<p>Filtered frames written to <code>{}</code> (accepted {}, rejected {}, stable cycles {}).</p>".format(
                    rel_path(result.filtered_dir, destination.parent),
                    result.artifacts.accepted_frames,
                    result.artifacts.rejected_frames,
                    result.artifacts.stable_cycles,
                ),
            )
            imgs = [p for p in visuals.get(result.scenario.name, []) if p.suffix.lower() == ".png"]
            summaries = [p for p in visuals.get(result.scenario.name, []) if p.suffix.lower() == ".txt"]
            if imgs:
                lines.append("<div>")
                for img in imgs[:4]:
                    lines.append(
                        "<img src='{}' alt='{}'>".format(
                            rel_path(img, destination.parent),
                            img.name,
                        ),
                    )
                lines.append("</div>")
            if summaries:
                lines.append(
                    "<p>Summary table: <a href='{}'>{}</a></p>".format(
                        rel_path(summaries[0], destination.parent),
                        summaries[0].name,
                    ),
                )
        lines.append("</section>")

    if test_summary:
        lines.append("<section><h2>Validation</h2>")
        status = "✅ Passed" if test_summary.passed else "❌ Failed"
        lines.append(
            "<p>{} — pytest finished in {:.2f} s.</p>".format(
                status,
                test_summary.duration_s,
            ),
        )
        lines.append("</section>")

    lines.append("</body></html>")

    destination.write_text("\n".join(lines), encoding="utf-8")
    print(f"Demo report written to {destination}")
    return destination


def main() -> None:
    args = parse_args()
    config = ScanFilterConfig.load(args.config)
    pipeline = ScanFilterPipeline(config)

    scenario_lookup = {scenario.name: scenario for scenario in SCENARIOS}
    if args.scenario:
        selected_scenarios = [scenario_lookup[name] for name in args.scenario]
    else:
        selected_scenarios = SCENARIOS

    if args.lightning:
        args.skip_visuals = True
        args.skip_tests = True
        if not args.scenario:
            args.skip_scenarios = True
            selected_scenarios = []
        args.skip_report = True
        args.auto_open = False
        args.auto_open_report = False
        args.auto_open_visuals = False

    baseline_root = Path(args.baseline_root)
    baseline_output = Path(args.baseline_output)
    demo_root = Path(args.demo_root)
    report_path = Path(args.report).resolve() if args.report else None

    baseline = prepare_baseline_run(pipeline, baseline_root, baseline_output)
    scenario_results: List[ScenarioResult] = []
    if not args.skip_scenarios:
        scenario_results = run_demo_scenarios(pipeline, demo_root, selected_scenarios)
    else:
        print("[3/4] Skipping multi-scenario generation (per flag).\n")

    visuals: Dict[str, List[Path]] = {}
    visual_dirs: Dict[str, Path] = {}
    if scenario_results and not args.skip_visuals:
        visuals, visual_dirs = run_visualizations(scenario_results, args.max_frame_previews)
    elif scenario_results:
        print("Skipping visualization export (per --skip-visuals).\n")
    else:
        print("No scenario visuals to export.\n")

    test_summary: TestSummary | None = None
    if not args.skip_tests:
        test_summary = run_tests()
    else:
        print("Skipping pytest (per --skip-tests).\n")

    print("Demo assets ready:")
    print(f" - Baseline filtered frames: {baseline.artifacts.output_dir}")
    for result in scenario_results:
        print(f" - Scenario {result.scenario.name}: {result.filtered_dir} (visuals folder next to it)")
    print("\nOpen docs/demo_playbook.md for talking points and show the artifacts listed above.")

    report_file: Path | None = None
    if not args.skip_report and report_path is not None:
        report_file = generate_report(report_path, baseline, scenario_results, visuals, test_summary)

    auto_open_all = args.auto_open
    if report_file and (auto_open_all or args.auto_open_report):
        auto_open_path(report_file)
    if visual_dirs and (auto_open_all or args.auto_open_visuals):
        for directory in visual_dirs.values():
            auto_open_path(directory)


if __name__ == "__main__":
    main()
