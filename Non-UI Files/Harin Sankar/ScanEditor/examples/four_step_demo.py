from __future__ import annotations

import argparse
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from scan_filter.config import ScanFilterConfig
from scan_filter.core.aligner import TimeAligner
from scan_filter.core.cycle_detection import CycleDetector
from scan_filter.core.filter_engine import FrameFilter
from scan_filter.io.csv_reader import CardiogramCsvReader
from scan_filter.io.dicom_reader import DicomSeriesReader
from scan_filter.io.dicom_writer import DicomSeriesWriter
from examples.create_sample_inputs import generate_cardiogram, generate_dicom_series

cardio_reader = CardiogramCsvReader()
dicom_reader = DicomSeriesReader()
dicom_writer = DicomSeriesWriter()


@dataclass(frozen=True)
class FourStepCase:
    name: str
    beats: int
    base_rr_ms: float
    frame_count: int
    spacing_ms: float
    cardio_noise: float
    dicom_shape: Tuple[int, int]
    spacing_noise_ms: float
    jitter_ms: float
    offset_ms: float
    modality: str
    seed: int


@dataclass
class CaseMetrics:
    name: str
    modality: str
    bpm: float
    frame_count: int
    resolution: str
    signal_min: float
    signal_max: float
    alignment_offset_ms: float
    residual_before_mean: float
    residual_before_std: float
    residual_after_mean: float
    residual_after_std: float
    cycles_total: int
    cycles_stable: int
    stable_ratio: float
    accepted_frames: int
    rejected_frames: int
    acceptance_rate: float
    output_dir: Path


def auto_open(paths: Sequence[Path], *, enabled: bool) -> None:
    if not enabled:
        return
    for path in paths:
        if not path.exists():
            continue
        target = path.resolve()
        try:
            if os.name == "nt":
                try:
                    os.startfile(target)  # type: ignore[attr-defined]
                except OSError:
                    subprocess.run(["cmd", "/c", "start", "", str(target)], check=False)
            elif sys.platform == "darwin":
                subprocess.run(["open", str(target)], check=False)
            else:
                subprocess.run(["xdg-open", str(target)], check=False)
        except Exception as exc:  # pragma: no cover
            print(f"Warning: could not open {target}: {exc}")


def build_case_catalog() -> List[FourStepCase]:
    specs = [
        FourStepCase("case_01_fast_lownoise", 16, 620.0, 14, 150.0, 0.03, (64, 64), 6.0, 3.0, -80.0, "cine-balanced", 1),
        FourStepCase("case_02_fast_highnoise", 18, 600.0, 16, 140.0, 0.08, (48, 48), 10.0, 6.0, -120.0, "cine-balanced", 2),
        FourStepCase("case_03_mid_clean", 20, 780.0, 12, 190.0, 0.02, (64, 48), 4.0, 2.0, -40.0, "bSSFP", 3),
        FourStepCase("case_04_mid_modulated", 22, 820.0, 15, 210.0, 0.06, (80, 64), 8.0, 5.0, 30.0, "bSSFP", 4),
        FourStepCase("case_05_mid_sparse", 18, 900.0, 10, 260.0, 0.04, (96, 64), 12.0, 4.0, 60.0, "FLASH", 5),
        FourStepCase("case_06_brady_lownoise", 20, 980.0, 14, 240.0, 0.02, (64, 96), 5.0, 3.0, 45.0, "FLASH", 6),
        FourStepCase("case_07_brady_noisy", 22, 1040.0, 18, 230.0, 0.09, (96, 96), 14.0, 7.0, 90.0, "FLASH", 7),
        FourStepCase("case_08_arrhythmia", 24, 760.0, 17, 205.0, 0.12, (64, 80), 18.0, 9.0, -60.0, "cine-balanced", 8),
        FourStepCase("case_09_variable_spacing", 19, 780.0, 20, 160.0, 0.05, (80, 80), 25.0, 5.0, -100.0, "cine-balanced", 9),
        FourStepCase("case_10_high_frames", 18, 700.0, 22, 150.0, 0.04, (64, 64), 8.0, 4.0, -20.0, "cine-balanced", 10),
        FourStepCase("case_11_low_frames", 16, 820.0, 9, 280.0, 0.03, (48, 48), 6.0, 2.0, 40.0, "FLASH", 11),
        FourStepCase("case_12_low_snr", 20, 760.0, 14, 170.0, 0.1, (64, 64), 9.0, 6.0, -70.0, "bSSFP", 12),
        FourStepCase("case_13_ultra_fast", 18, 540.0, 16, 120.0, 0.05, (56, 56), 10.0, 5.0, -150.0, "cine-balanced", 13),
        FourStepCase("case_14_ultra_slow", 26, 1100.0, 18, 260.0, 0.04, (80, 64), 10.0, 3.0, 120.0, "FLASH", 14),
        FourStepCase("case_15_obese_patient", 22, 840.0, 15, 215.0, 0.07, (96, 96), 16.0, 8.0, -90.0, "HASTE", 15),
        FourStepCase("case_16_peds_small", 16, 520.0, 12, 130.0, 0.02, (48, 48), 8.0, 4.0, -110.0, "cine-balanced", 16),
        FourStepCase("case_17_implant_noise", 20, 780.0, 14, 195.0, 0.11, (64, 64), 20.0, 7.0, 70.0, "bSSFP", 17),
        FourStepCase("case_18_low_gating", 18, 760.0, 13, 185.0, 0.09, (64, 80), 15.0, 5.0, 85.0, "cine-balanced", 18),
        FourStepCase("case_19_sparse_gating", 24, 900.0, 11, 275.0, 0.05, (80, 96), 18.0, 6.0, 95.0, "FLASH", 19),
        FourStepCase("case_20_vendor_mix", 20, 800.0, 16, 200.0, 0.05, (72, 64), 10.0, 4.0, -30.0, "HASTE", 20),
    ]
    return specs


def cases_to_dataframe(cases: Sequence[FourStepCase]) -> pd.DataFrame:
    records = []
    for case in cases:
        records.append(
            {
                "case": case.name,
                "modality": case.modality,
                "beats": case.beats,
                "base_rr_ms": case.base_rr_ms,
                "bpm": round(60000.0 / case.base_rr_ms, 1),
                "frames": case.frame_count,
                "spacing_ms": case.spacing_ms,
                "resolution": f"{case.dicom_shape[0]}x{case.dicom_shape[1]}",
                "cardio_noise": case.cardio_noise,
                "spacing_noise_ms": case.spacing_noise_ms,
                "jitter_ms": case.jitter_ms,
                "offset_ms": case.offset_ms,
                "seed": case.seed,
            },
        )
    return pd.DataFrame(records)


def apply_cardio_noise(cardio_path: Path, scale: float, seed: int) -> None:
    if scale <= 0:
        return
    df = pd.read_csv(cardio_path)
    rng = np.random.default_rng(seed)
    df["signal"] += rng.normal(0.0, scale, size=len(df))
    df.to_csv(cardio_path, index=False)


def prepare_cases(cases: Sequence[FourStepCase], root: Path) -> Dict[str, Tuple[Path, Path]]:
    root.mkdir(parents=True, exist_ok=True)
    outputs: Dict[str, Tuple[Path, Path]] = {}
    for case in cases:
        case_dir = root / case.name
        cardio_path = case_dir / "cardiogram.csv"
        dicom_dir = case_dir / "dicom_series"
        generate_cardiogram(cardio_path, beats=case.beats, base_rr_ms=case.base_rr_ms)
        apply_cardio_noise(cardio_path, case.cardio_noise, seed=case.seed)
        generate_dicom_series(
            dicom_dir,
            frame_count=case.frame_count,
            start_ms=120.0,
            spacing_ms=case.spacing_ms,
            shape=case.dicom_shape,
            jitter_ms=case.jitter_ms,
            spacing_noise_ms=case.spacing_noise_ms,
            global_offset_ms=case.offset_ms,
            seed=case.seed,
        )
        outputs[case.name] = (cardio_path, dicom_dir)
    return outputs


def _compute_residuals(cardio_ts: np.ndarray, frame_ts: np.ndarray) -> np.ndarray:
    if cardio_ts.size == 0 or frame_ts.size == 0:
        return np.array([])
    right_idx = np.clip(np.searchsorted(cardio_ts, frame_ts), 0, cardio_ts.size - 1)
    left_idx = np.clip(right_idx - 1, 0, cardio_ts.size - 1)
    left_vals = cardio_ts[left_idx]
    right_vals = cardio_ts[right_idx]
    choose_left = np.abs(frame_ts - left_vals) <= np.abs(right_vals - frame_ts)
    nearest = np.where(choose_left, left_vals, right_vals)
    return frame_ts - nearest


def run_four_step_demo(
    cases: Sequence[FourStepCase],
    inputs: Dict[str, Tuple[Path, Path]],
    config: ScanFilterConfig,
    output_root: Path,
) -> Tuple[pd.DataFrame, pd.DataFrame, pd.DataFrame, pd.DataFrame, List[CaseMetrics], List[pd.DataFrame]]:
    aligner = TimeAligner(config.alignment.max_offset_ms)
    cd = config.cycle_detection
    detector = CycleDetector(
        window_ms=cd.window_ms,
        min_peak_prominence=cd.min_peak_prominence,
        stable_rr_variation_pct=cd.stable_rr_variation_pct,
        min_rr_ms=cd.min_rr_ms,
        max_rr_ms=cd.max_rr_ms,
    )
    fcfg = config.filtering
    frame_filter = FrameFilter(
        stable_phase_fraction=fcfg.stable_phase_fraction,
        stable_phase_offset=fcfg.stable_phase_offset,
    )

    ingest_records: List[dict] = []
    alignment_records: List[dict] = []
    cycle_records: List[dict] = []
    filter_records: List[dict] = []
    metrics_bundle: List[CaseMetrics] = []
    previews: List[pd.DataFrame] = []

    filter_output_root = output_root / "step_4_filter" / "filtered_frames"
    filter_output_root.mkdir(parents=True, exist_ok=True)

    for case in cases:
        cardio_path, dicom_dir = inputs[case.name]
        cardio = cardio_reader.read(cardio_path)
        frames = dicom_reader.read(dicom_dir)
        cardio_df = cardio.to_dataframe()
        if len(previews) < 6:
            window = cardio_df[cardio_df["timestamp_ms"] <= 4000].copy()
            window["case"] = case.name
            previews.append(window)

        bpm = 60000.0 / case.base_rr_ms
        ingest_records.append(
            {
                "case": case.name,
                "modality": case.modality,
                "resolution": f"{case.dicom_shape[0]}x{case.dicom_shape[1]}",
                "frames": case.frame_count,
                "bpm": bpm,
                "signal_min": float(cardio_df["signal"].min()),
                "signal_max": float(cardio_df["signal"].max()),
            },
        )

        raw_residuals = _compute_residuals(cardio.timestamps, frames.timestamps)
        alignment = aligner.align(cardio, frames)
        aligned_residuals = _compute_residuals(cardio.timestamps, alignment.aligned_series.timestamps)
        alignment_records.append(
            {
                "case": case.name,
                "offset_ms": alignment.offset_ms,
                "residual_mean_before": float(raw_residuals.mean()) if raw_residuals.size else 0.0,
                "residual_std_before": float(raw_residuals.std()) if raw_residuals.size else 0.0,
                "residual_mean_after": float(aligned_residuals.mean()) if aligned_residuals.size else 0.0,
                "residual_std_after": float(aligned_residuals.std()) if aligned_residuals.size else 0.0,
            },
        )

        cycle_summary = detector.detect(cardio)
        cycle_records.append(
            {
                "case": case.name,
                "total_cycles": len(cycle_summary.cycles),
                "stable_cycles": len(cycle_summary.stable_cycles),
                "stable_ratio": (len(cycle_summary.stable_cycles) / len(cycle_summary.cycles)) * 100.0
                if cycle_summary.cycles
                else 0.0,
            },
        )

        filter_results = frame_filter.filter_frames(alignment.aligned_series, cycle_summary.stable_cycles)
        accepted = len(filter_results.accepted_frames)
        rejected = len(filter_results.rejected_frames)
        acceptance_rate = (accepted / (accepted + rejected)) * 100.0 if (accepted + rejected) else 0.0
        case_output = filter_output_root / case.name
        dicom_writer.write(filter_results.accepted_frames, case_output)
        filter_records.append(
            {
                "case": case.name,
                "accepted_frames": accepted,
                "rejected_frames": rejected,
                "acceptance_rate": acceptance_rate,
            },
        )

        metrics_bundle.append(
            CaseMetrics(
                name=case.name,
                modality=case.modality,
                bpm=bpm,
                frame_count=case.frame_count,
                resolution=f"{case.dicom_shape[0]}x{case.dicom_shape[1]}",
                signal_min=float(cardio_df["signal"].min()),
                signal_max=float(cardio_df["signal"].max()),
                alignment_offset_ms=alignment.offset_ms,
                residual_before_mean=float(raw_residuals.mean()) if raw_residuals.size else 0.0,
                residual_before_std=float(raw_residuals.std()) if raw_residuals.size else 0.0,
                residual_after_mean=float(aligned_residuals.mean()) if aligned_residuals.size else 0.0,
                residual_after_std=float(aligned_residuals.std()) if aligned_residuals.size else 0.0,
                cycles_total=len(cycle_summary.cycles),
                cycles_stable=len(cycle_summary.stable_cycles),
                stable_ratio=(len(cycle_summary.stable_cycles) / len(cycle_summary.cycles)) * 100.0
                if cycle_summary.cycles
                else 0.0,
                accepted_frames=accepted,
                rejected_frames=rejected,
                acceptance_rate=acceptance_rate,
                output_dir=case_output,
            ),
        )

    ingest_df = pd.DataFrame(ingest_records)
    alignment_df = pd.DataFrame(alignment_records)
    cycle_df = pd.DataFrame(cycle_records)
    filter_df = pd.DataFrame(filter_records)
    return ingest_df, alignment_df, cycle_df, filter_df, metrics_bundle, previews


def render_ingest_outputs(dest: Path, ingest_df: pd.DataFrame, previews: List[pd.DataFrame]) -> List[Path]:
    dest.mkdir(parents=True, exist_ok=True)
    outputs: List[Path] = []
    table_path = dest / "step1_ingest_metrics.csv"
    ingest_df.to_csv(table_path, index=False)
    outputs.append(table_path)

    if previews:
        overlay_df = pd.concat(previews, ignore_index=True)
        fig, axes = plt.subplots(1, 2, figsize=(14, 5))
        for case, group in overlay_df.groupby("case"):
            axes[0].plot(group["timestamp_ms"], group["signal"], linewidth=1.0, label=case)
        axes[0].set_title("Cardiogram intake window (first 4s)")
        axes[0].set_xlabel("Time (ms)")
        axes[0].set_ylabel("Signal")
        axes[0].legend(fontsize=8)

        axes[1].barh(ingest_df["case"], ingest_df["frames"], color="#0ea5e9")
        axes[1].set_xlabel("Frame count")
        axes[1].set_title("Frame inventory per case")
        axes[1].grid(axis="x", alpha=0.3)
        fig.tight_layout()
        overlay_path = dest / "step1_ingest_overview.png"
        fig.savefig(overlay_path, dpi=170)
        plt.close(fig)
        outputs.append(overlay_path)

    fig_dist, axes_dist = plt.subplots(1, 2, figsize=(12, 4))
    axes_dist[0].hist(ingest_df["bpm"], bins=10, color="#2563eb", alpha=0.8)
    axes_dist[0].set_title("Heart rate distribution")
    axes_dist[0].set_xlabel("BPM")
    axes_dist[0].set_ylabel("Cases")

    resolution_counts = ingest_df["resolution"].value_counts().sort_values(ascending=False)
    axes_dist[1].bar(resolution_counts.index, resolution_counts.values, color="#f97316")
    axes_dist[1].set_title("DICOM resolution mix")
    axes_dist[1].set_ylabel("Cases")
    axes_dist[1].tick_params(axis="x", rotation=45)
    fig_dist.tight_layout()
    dist_path = dest / "step1_ingest_distribution.png"
    fig_dist.savefig(dist_path, dpi=170)
    plt.close(fig_dist)
    outputs.append(dist_path)

    return outputs


def render_alignment_outputs(dest: Path, alignment_df: pd.DataFrame) -> List[Path]:
    dest.mkdir(parents=True, exist_ok=True)
    outputs: List[Path] = []
    csv_path = dest / "step2_alignment_metrics.csv"
    alignment_df.to_csv(csv_path, index=False)
    outputs.append(csv_path)

    fig, axes = plt.subplots(1, 2, figsize=(14, 5))
    axes[0].bar(alignment_df["case"], alignment_df["offset_ms"], color="#22c55e")
    axes[0].set_title("Alignment offsets per case")
    axes[0].set_ylabel("Offset (ms)")
    axes[0].tick_params(axis="x", rotation=90)
    axes[0].grid(axis="y", alpha=0.3)

    width = 0.35
    x = np.arange(len(alignment_df))
    axes[1].bar(x - width / 2, alignment_df["residual_std_before"], width=width, label="Before", color="#ef4444")
    axes[1].bar(x + width / 2, alignment_df["residual_std_after"], width=width, label="After", color="#3b82f6")
    axes[1].set_title("Residual spread before/after alignment")
    axes[1].set_ylabel("Std dev (ms)")
    axes[1].set_xticks(x)
    axes[1].set_xticklabels(alignment_df["case"], rotation=90)
    axes[1].legend()
    axes[1].grid(axis="y", alpha=0.3)

    fig.tight_layout()
    path = dest / "step2_alignment_overview.png"
    fig.savefig(path, dpi=170)
    plt.close(fig)
    outputs.append(path)
    return outputs


def render_cycle_outputs(dest: Path, cycle_df: pd.DataFrame) -> List[Path]:
    dest.mkdir(parents=True, exist_ok=True)
    outputs: List[Path] = []
    csv_path = dest / "step3_cycle_metrics.csv"
    cycle_df.to_csv(csv_path, index=False)
    outputs.append(csv_path)

    fig, axes = plt.subplots(1, 2, figsize=(13, 4))
    axes[0].bar(cycle_df["case"], cycle_df["stable_ratio"], color="#14b8a6")
    axes[0].set_ylabel("Stable ratio (%)")
    axes[0].set_title("Steady-state capture per case")
    axes[0].tick_params(axis="x", rotation=90)
    axes[0].grid(axis="y", alpha=0.3)

    axes[1].scatter(cycle_df["total_cycles"], cycle_df["stable_cycles"], color="#9333ea")
    axes[1].plot([cycle_df["total_cycles"].min(), cycle_df["total_cycles"].max()],
                 [cycle_df["total_cycles"].min(), cycle_df["total_cycles"].max()],
                 linestyle="--", color="#475569", linewidth=1.0)
    axes[1].set_xlabel("Total cycles")
    axes[1].set_ylabel("Stable cycles")
    axes[1].set_title("Cycle quality scatter")
    axes[1].grid(alpha=0.3)

    fig.tight_layout()
    path = dest / "step3_cycles_overview.png"
    fig.savefig(path, dpi=170)
    plt.close(fig)
    outputs.append(path)
    return outputs


def render_filter_outputs(dest: Path, filter_df: pd.DataFrame) -> List[Path]:
    dest.mkdir(parents=True, exist_ok=True)
    outputs: List[Path] = []
    csv_path = dest / "step4_filter_metrics.csv"
    filter_df.to_csv(csv_path, index=False)
    outputs.append(csv_path)

    fig, axes = plt.subplots(1, 2, figsize=(14, 5))
    axes[0].bar(filter_df["case"], filter_df["acceptance_rate"], color="#facc15")
    axes[0].set_ylabel("Acceptance rate (%)")
    axes[0].set_title("Frame acceptance per case")
    axes[0].tick_params(axis="x", rotation=90)
    axes[0].set_ylim(0, 110)
    axes[0].grid(axis="y", alpha=0.3)

    width = 0.4
    x = np.arange(len(filter_df))
    axes[1].bar(x - width / 2, filter_df["accepted_frames"], width=width, label="Accepted", color="#16a34a")
    axes[1].bar(x + width / 2, filter_df["rejected_frames"], width=width, label="Rejected", color="#dc2626")
    axes[1].set_title("Frame disposition counts")
    axes[1].set_ylabel("Frames")
    axes[1].set_xticks(x)
    axes[1].set_xticklabels(filter_df["case"], rotation=90)
    axes[1].legend()
    axes[1].grid(axis="y", alpha=0.3)

    fig.tight_layout()
    path = dest / "step4_filter_overview.png"
    fig.savefig(path, dpi=170)
    plt.close(fig)
    outputs.append(path)
    return outputs


def build_summary_csv(dest: Path, metrics: List[CaseMetrics]) -> Path:
    records = [
        {
            "case": m.name,
            "modality": m.modality,
            "bpm": m.bpm,
            "resolution": m.resolution,
            "frames": m.frame_count,
            "signal_min": m.signal_min,
            "signal_max": m.signal_max,
            "alignment_offset_ms": m.alignment_offset_ms,
            "residual_std_before": m.residual_before_std,
            "residual_std_after": m.residual_after_std,
            "stable_ratio": m.stable_ratio,
            "accepted_frames": m.accepted_frames,
            "rejected_frames": m.rejected_frames,
            "acceptance_rate": m.acceptance_rate,
            "output_dir": m.output_dir,
        }
        for m in metrics
    ]
    df = pd.DataFrame(records)
    summary_path = dest / "four_step_case_summary.csv"
    df.to_csv(summary_path, index=False)
    return summary_path


STEP_CHOICES = ["ingest", "alignment", "cycles", "filter", "all"]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run a 4-step, 20-case Scan Filter demo")
    parser.add_argument("--config", default="config/default.yaml", help="Path to Scan Filter config")
    parser.add_argument("--cases-root", default="demo_steps/four_step_demo/cases", help="Where to store generated inputs")
    parser.add_argument("--output-root", default="demo_steps/four_step_demo", help="Destination folder for visuals")
    parser.add_argument("--step", choices=STEP_CHOICES, default="all", help="Restrict rendering/auto-open to a single stage")
    parser.add_argument("--no-open", action="store_true", help="Do not auto-open generated artifacts")
    parser.add_argument("--list-cases", action="store_true", help="Print the case catalog and exit")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = ScanFilterConfig.load(args.config)
    cases = build_case_catalog()
    cases_df = cases_to_dataframe(cases)

    if args.list_cases:
        print("Four-step demo case catalog:")
        print(cases_df.to_string(index=False))
        return
    inputs = prepare_cases(cases, Path(args.cases_root))
    output_root = Path(args.output_root)

    ingest_df, alignment_df, cycle_df, filter_df, metrics_bundle, previews = run_four_step_demo(
        cases,
        inputs,
        config,
        output_root,
    )

    stage_outputs: List[Path] = []
    selected = args.step
    if selected in {"ingest", "all"}:
        stage_outputs.extend(render_ingest_outputs(output_root / "step_1_ingest", ingest_df, previews))
    if selected in {"alignment", "all"}:
        stage_outputs.extend(render_alignment_outputs(output_root / "step_2_alignment", alignment_df))
    if selected in {"cycles", "all"}:
        stage_outputs.extend(render_cycle_outputs(output_root / "step_3_cycles", cycle_df))
    if selected in {"filter", "all"}:
        stage_outputs.extend(render_filter_outputs(output_root / "step_4_filter", filter_df))

    if selected == "all":
        stage_outputs.append(build_summary_csv(output_root, metrics_bundle))

    print("4-step demo complete. Generated artifacts:")
    for path in stage_outputs:
        print(f" - {path}")
    auto_open(stage_outputs, enabled=not args.no_open)


if __name__ == "__main__":
    main()
