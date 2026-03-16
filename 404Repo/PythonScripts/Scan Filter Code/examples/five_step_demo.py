from __future__ import annotations

import argparse
import os
import random
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Optional, Sequence, Tuple

import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap
import numpy as np
import pandas as pd
import pydicom

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from scan_filter.config import ScanFilterConfig
from scan_filter.core.aligner import TimeAligner
from scan_filter.core.cycle_detection import CycleDetector
from scan_filter.core.pipeline import ScanFilterPipeline
from scan_filter.io.csv_reader import CardiogramCsvReader
from scan_filter.io.dicom_reader import DicomSeriesReader
from examples.create_sample_inputs import (
    CARDIO_FILENAME,
    DICOM_DIRNAME,
    generate_cardiogram,
    generate_dicom_series,
)
from examples.visualize_run import (
    export_frame_previews,
    load_frames,
    plot_cardiogram_with_frames,
    reader as viz_reader,
    write_summary,
)

cardio_reader = CardiogramCsvReader()
dicom_reader = DicomSeriesReader()


@dataclass(frozen=True)
class ValidationScenario:
    name: str
    beats: int
    base_rr_ms: float
    frames: int
    spacing_ms: float
    noise_scale: float = 0.0
    mode: str = "normal"  # normal | outlier | missing_cardio | corrupt_dicom
    jitter_ms: float = 0.0
    spacing_noise_ms: float = 0.0
    global_offset_ms: float = 0.0
    dicom_shape: Tuple[int, int] | None = None


STEP_NAMES = ["ingest", "alignment", "cycles", "filter", "validation"]


def auto_open(path: Path) -> None:
    target = path.resolve()
    print(f"Auto-opening {target}")
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


def ensure_inputs(root: Path) -> tuple[Path, Path]:
    cardio_path = root / CARDIO_FILENAME
    dicom_dir = root / DICOM_DIRNAME
    generate_cardiogram(cardio_path)
    generate_dicom_series(dicom_dir)
    return cardio_path, dicom_dir


def prepare_output_dir(base: Path, step: str) -> Path:
    step_dir = base / f"step_{step}"
    step_dir.mkdir(parents=True, exist_ok=True)
    return step_dir


def inject_alignment_noise(
    dicom_dir: Path,
    *,
    global_offset_ms: float = -200.0,
    jitter_ms: float = 12.0,
    spacing_noise_ms: float = 25.0,
) -> Optional[np.ndarray]:
    files = sorted(dicom_dir.glob("*.dcm"))
    if not files:
        return None
    rng = random.Random(42)
    prev_base = None
    prev_noisy = None
    baseline: List[float] = []
    for idx, file in enumerate(files):
        ds = pydicom.dcmread(file)
        base_trigger = float(ds.get("TriggerTime", 0.0))
        baseline.append(base_trigger)
        if idx == 0:
            jitter = rng.gauss(0.0, jitter_ms)
            noisy_trigger = base_trigger + global_offset_ms + jitter
        else:
            base_gap = base_trigger - float(prev_base)
            spacing_noise = rng.gauss(0.0, spacing_noise_ms)
            jitter = rng.gauss(0.0, jitter_ms)
            noisy_gap = max(5.0, base_gap + spacing_noise + jitter)
            noisy_trigger = float(prev_noisy) + noisy_gap
        ds.TriggerTime = float(noisy_trigger)
        prev_base = base_trigger
        prev_noisy = ds.TriggerTime
        ds.save_as(file)
    baseline_arr = np.array(baseline, dtype=float)
    if baseline_arr.size:
        baseline_arr = baseline_arr - baseline_arr[0]
    return baseline_arr


def _compute_frame_residuals(cardio_ts: np.ndarray, frame_ts: np.ndarray) -> np.ndarray:
    if cardio_ts.size == 0 or frame_ts.size == 0:
        return np.array([])
    right_idx = np.clip(np.searchsorted(cardio_ts, frame_ts), 0, cardio_ts.size - 1)
    left_idx = np.clip(right_idx - 1, 0, cardio_ts.size - 1)
    left_vals = cardio_ts[left_idx]
    right_vals = cardio_ts[right_idx]
    choose_left = np.abs(frame_ts - left_vals) <= np.abs(right_vals - frame_ts)
    nearest = np.where(choose_left, left_vals, right_vals)
    return frame_ts - nearest


def render_ingest_visuals(cardio_path: Path, dicom_dir: Path, dest: Path) -> List[Path]:
    cardio_series = cardio_reader.read(cardio_path)
    cardio_df = cardio_series.to_dataframe()
    frames_series = dicom_reader.read(dicom_dir)
    first_frame = sorted(dicom_dir.glob("*.dcm"))[0]
    ds = pydicom.dcmread(first_frame)
    pixels = ds.pixel_array.astype(float)
    pixels -= pixels.min()
    denom = pixels.max() or 1.0
    pixels /= denom

    outputs: List[Path] = []
    fig = plt.figure(figsize=(12, 8))
    grid = fig.add_gridspec(2, 2, height_ratios=[1.2, 1])
    window_limit = cardio_df["timestamp_ms"].min() + 4000
    window = cardio_df[cardio_df["timestamp_ms"] <= window_limit]
    ax_sig = fig.add_subplot(grid[0, :])
    ax_sig.plot(window["timestamp_ms"], window["signal"], color="#0f172a", linewidth=1.2)
    ax_sig.set_title("Cardiogram Intake (first 4 seconds)")
    ax_sig.set_xlabel("Time (ms)")
    ax_sig.set_ylabel("Signal")

    ax_img = fig.add_subplot(grid[1, 0])
    ax_img.imshow(pixels, cmap="gray")
    ax_img.set_title(f"Raw DICOM Frame: {first_frame.name}")
    ax_img.axis("off")

    ax_hist = fig.add_subplot(grid[1, 1])
    ax_hist.hist(cardio_df["signal"], bins=40, color="#2563eb", alpha=0.8)
    ax_hist.set_title("Signal Amplitude Distribution")
    ax_hist.set_xlabel("Normalized amplitude")
    ax_hist.set_ylabel("Samples")

    fig.suptitle("Step 1 – Input Ingestion Validation", fontsize=14)
    fig.tight_layout(rect=[0, 0, 1, 0.98])
    overview_path = dest / "step1_ingest_overview.png"
    fig.savefig(overview_path, dpi=160)
    plt.close(fig)
    outputs.append(overview_path)

    cardio_duration_ms = float(cardio_df["timestamp_ms"].iloc[-1] - cardio_df["timestamp_ms"].iloc[0]) if len(cardio_df) > 1 else 0.0
    sample_period = cardio_series.estimate_sample_period_ms()
    frame_spacing = np.diff(frames_series.timestamps)
    mean_spacing = float(frame_spacing.mean()) if frame_spacing.size else 0.0
    metrics = [
        ("Cardio samples", len(cardio_df)),
        ("Signal range", f"{cardio_df['signal'].min():.2f} to {cardio_df['signal'].max():.2f}"),
        ("Capture duration (s)", f"{cardio_duration_ms / 1000:.1f}"),
        ("Sample period (ms)", f"{sample_period:.2f}"),
        ("Frames detected", len(frames_series.frames)),
        ("Mean frame spacing (ms)", f"{mean_spacing:.2f}"),
        ("Pixel resolution", f"{getattr(ds, 'Rows', '?')}x{getattr(ds, 'Columns', '?')}"),
    ]

    fig_qc, (ax_table, ax_line) = plt.subplots(2, 1, figsize=(10, 6), gridspec_kw={"height_ratios": [1, 1]})
    ax_table.axis("off")
    ax_table.set_title("Input Health Metrics", loc="left")
    text_lines = [f"• {name}: {value}" for name, value in metrics]
    ax_table.text(0, 0.95, "\n".join(text_lines), fontsize=11, va="top")

    frame_indices = np.arange(len(frames_series.frames))
    ax_line.plot(frame_indices, frames_series.timestamps, color="#0ea5e9", marker="o", linewidth=1.2)
    ax_line.set_title("Frame trigger timeline")
    ax_line.set_xlabel("Frame #")
    ax_line.set_ylabel("Trigger time (ms)")
    ax_line.grid(alpha=0.3)

    qc_path = dest / "step1_ingest_qc.png"
    fig_qc.tight_layout()
    fig_qc.savefig(qc_path, dpi=160)
    plt.close(fig_qc)
    outputs.append(qc_path)

    metrics_path = dest / "step1_ingest_metrics.txt"
    metrics_path.write_text("\n".join(f"{k}: {v}" for k, v in metrics), encoding="utf-8")
    outputs.append(metrics_path)
    return outputs


def render_alignment_visuals(
    cardio_path: Path,
    dicom_dir: Path,
    config: ScanFilterConfig,
    dest: Path,
    baseline_triggers: Optional[np.ndarray] = None,
) -> List[Path]:
    cardio = cardio_reader.read(cardio_path)
    frames = dicom_reader.read(dicom_dir)
    aligner = TimeAligner(config.alignment.max_offset_ms)
    alignment = aligner.align(cardio, frames)

    outputs: List[Path] = []
    fig, ax = plt.subplots(figsize=(12, 4))
    df = cardio.to_dataframe()
    ax.plot(df["timestamp_ms"], df["signal"], color="#003049", linewidth=1.0, label="Cardiogram")
    subset = frames.frames[:8]
    raw_times = [frame.timestamp_ms for frame in subset]
    aligned_times = [frame.timestamp_ms for frame in alignment.aligned_series.frames[:8]]
    for ts in raw_times:
        ax.axvline(ts, color="#f77f00", alpha=0.5, linestyle="--")
    for ts in aligned_times:
        ax.axvline(ts, color="#1b4332", alpha=0.9, linewidth=1.2)
    ax.set_title("Step 2 – Timeline Alignment")
    ax.set_xlabel("Time (ms)")
    ax.set_ylabel("Signal / Frame markers")
    ax.legend(["Cardiogram", "Raw frame time", "Aligned frame time"], loc="upper right")
    text = f"Computed offset: {alignment.offset_ms:.2f} ms (green lines show aligned trigger positions)"
    ax.text(0.02, 0.05, text, transform=ax.transAxes, fontsize=9, bbox=dict(facecolor="white", alpha=0.7))
    fig.tight_layout()
    overview_path = dest / "step2_alignment.png"
    fig.savefig(overview_path, dpi=160)
    plt.close(fig)
    outputs.append(overview_path)

    if baseline_triggers is not None and len(baseline_triggers) == len(frames.timestamps):
        raw_residuals = frames.timestamps - baseline_triggers
        aligned_residuals = alignment.aligned_series.timestamps - baseline_triggers
        residual_title = "Residuals vs. original trigger plan (ms)"
    else:
        raw_residuals = _compute_frame_residuals(cardio.timestamps, frames.timestamps)
        aligned_residuals = _compute_frame_residuals(cardio.timestamps, alignment.aligned_series.timestamps)
        residual_title = "Frame-to-cardiogram residuals (ms)"
    fig_val, (ax_hist, ax_stats) = plt.subplots(1, 2, figsize=(12, 4))
    bins = np.linspace(-200, 200, 30)
    if raw_residuals.size:
        ax_hist.hist(raw_residuals, bins=bins, alpha=0.4, label="Before", color="#f94144")
    if aligned_residuals.size:
        ax_hist.hist(aligned_residuals, bins=bins, alpha=0.7, label="After", color="#277da1")
    ax_hist.set_title(residual_title)
    ax_hist.set_xlabel("Residual (ms)")
    ax_hist.set_ylabel("Frame count")
    ax_hist.legend()

    ax_stats.axis("off")
    before_std = float(raw_residuals.std()) if raw_residuals.size else 0.0
    after_std = float(aligned_residuals.std()) if aligned_residuals.size else 0.0
    before_mean = float(raw_residuals.mean()) if raw_residuals.size else 0.0
    after_mean = float(aligned_residuals.mean()) if aligned_residuals.size else 0.0
    after_max = float(np.abs(aligned_residuals).max()) if aligned_residuals.size else 0.0
    stats_lines = [
        f"Global offset (ms): {alignment.offset_ms:.2f}",
        f"Residual mean before: {before_mean:.2f}",
        f"Residual mean after: {after_mean:.2f}",
        f"Residual std before: {before_std:.2f}",
        f"Residual std after: {after_std:.2f}",
        f"Max |residual| after: {after_max:.2f} ms",
        f"Frames evaluated: {len(frames.frames)}",
    ]
    if baseline_triggers is not None:
        stats_lines.append("Residuals measured vs original trigger plan")
    ax_stats.text(0, 0.9, "Alignment QA Metrics\n" + "\n".join(f"• {line}" for line in stats_lines), va="top")
    fig_val.tight_layout()
    qa_path = dest / "step2_alignment_validation.png"
    fig_val.savefig(qa_path, dpi=160)
    plt.close(fig_val)
    outputs.append(qa_path)

    metrics_path = dest / "step2_alignment_metrics.txt"
    metrics_path.write_text("\n".join(stats_lines), encoding="utf-8")
    outputs.append(metrics_path)
    return outputs


def render_cycles_visual(cardio_path: Path, config: ScanFilterConfig, dest: Path) -> List[Path]:
    cardio = cardio_reader.read(cardio_path)
    detector = CycleDetector(
        window_ms=config.cycle_detection.window_ms,
        min_peak_prominence=config.cycle_detection.min_peak_prominence,
        stable_rr_variation_pct=config.cycle_detection.stable_rr_variation_pct,
        min_rr_ms=config.cycle_detection.min_rr_ms,
        max_rr_ms=config.cycle_detection.max_rr_ms,
    )
    summary = detector.detect(cardio)

    outputs: List[Path] = []
    fig, ax = plt.subplots(figsize=(12, 4))
    df = cardio.to_dataframe()
    ax.plot(df["timestamp_ms"], df["signal"], color="#001219", linewidth=1.0)
    for cycle in summary.cycles:
        color = "#94d2bd" if cycle.is_stable else "#ee9b00"
        ax.axvspan(cycle.start_ms, cycle.end_ms, color=color, alpha=0.3)
    ax.set_title("Step 3 – Cycle & Steady-State Detection")
    ax.set_xlabel("Time (ms)")
    ax.set_ylabel("Signal")
    stable = len(summary.stable_cycles)
    total = len(summary.cycles)
    ax.text(
        0.02,
        0.92,
        f"Stable cycles: {stable} / {total}",
        transform=ax.transAxes,
        fontsize=10,
        bbox=dict(facecolor="white", alpha=0.7),
    )
    fig.tight_layout()
    overview_path = dest / "step3_cycles.png"
    fig.savefig(overview_path, dpi=160)
    plt.close(fig)
    outputs.append(overview_path)

    rr_values = np.array([cycle.rr_ms for cycle in summary.cycles]) if summary.cycles else np.array([])
    durations = np.array([cycle.duration_ms for cycle in summary.cycles]) if summary.cycles else np.array([])
    stable_mask = np.array([cycle.is_stable for cycle in summary.cycles]) if summary.cycles else np.array([])
    fig_val, (ax_rr, ax_stats) = plt.subplots(2, 1, figsize=(11, 6), gridspec_kw={"height_ratios": [1.5, 1]})
    if rr_values.size:
        idx = np.arange(len(rr_values))
        ax_rr.plot(idx, rr_values, marker="o", color="#003566", label="RR interval")
        if stable_mask.any():
            ax_rr.scatter(idx[stable_mask], rr_values[stable_mask], color="#2a9d8f", label="Stable", zorder=3)
        rr_median = float(np.median(rr_values))
        ax_rr.axhline(rr_median, color="#0096c7", linestyle="--", label="Median RR")
        tolerance = rr_median * (config.cycle_detection.stable_rr_variation_pct / 100.0)
        ax_rr.fill_between(idx, rr_median - tolerance, rr_median + tolerance, color="#a8dadc", alpha=0.4, label="Stable band")
        ax_rr.set_ylabel("RR (ms)")
        ax_rr.set_title("RR interval tracking")
        ax_rr.legend(loc="upper right")
    else:
        ax_rr.text(0.5, 0.5, "Insufficient peaks for RR analysis", ha="center", va="center")

    ax_stats.axis("off")
    stats_lines = [
        f"Total cycles: {total}",
        f"Stable cycles: {stable}",
        f"Stable ratio: {(stable / total * 100):.1f}%" if total else "Stable ratio: n/a",
    ]
    if durations.size:
        stats_lines.append(f"Mean cycle duration: {durations.mean():.1f} ms")
        stats_lines.append(f"Std cycle duration: {durations.std():.1f} ms")
    ax_stats.text(0, 0.9, "Cycle QA Metrics\n" + "\n".join(f"• {line}" for line in stats_lines), va="top")
    fig_val.tight_layout()
    qa_path = dest / "step3_cycles_validation.png"
    fig_val.savefig(qa_path, dpi=160)
    plt.close(fig_val)
    outputs.append(qa_path)

    metrics_path = dest / "step3_cycles_metrics.txt"
    metrics_path.write_text("\n".join(stats_lines), encoding="utf-8")
    outputs.append(metrics_path)
    return outputs


def render_filter_visuals(cardio_path: Path, dicom_dir: Path, config: ScanFilterConfig, dest: Path) -> List[Path]:
    pipeline = ScanFilterPipeline(config)
    filtered_dir = dest / "filtered_frames"
    artifacts = pipeline.run(cardio_path, dicom_dir, filtered_dir)
    cardio_df = viz_reader.read(cardio_path).to_dataframe()
    filtered_names = {path.name for path in Path(filtered_dir).glob("*.dcm")}
    frames = load_frames(dicom_dir, filtered_names)
    outputs: List[Path] = []
    outputs.append(plot_cardiogram_with_frames(cardio_df, frames, dest))
    outputs.extend(export_frame_previews(frames, dest, max_per_group=2))
    outputs.append(write_summary(frames, dest))
    summary_path = dest / "step4_summary.txt"
    summary_path.write_text(
        f"Accepted frames: {artifacts.accepted_frames}\n" f"Rejected frames: {artifacts.rejected_frames}\n" f"Stable cycles: {artifacts.stable_cycles}\n",
        encoding="utf-8",
    )
    outputs.append(summary_path)
    dashboard_path = dest / "step4_filter_dashboard.png"
    fig_dash, (ax_bar, ax_text) = plt.subplots(1, 2, figsize=(12, 4), gridspec_kw={"width_ratios": [1, 1]})
    ax_bar.bar(["Accepted", "Rejected"], [artifacts.accepted_frames, artifacts.rejected_frames], color=["#2b9348", "#f9844a"])
    ax_bar.set_ylabel("Frame count")
    ax_bar.set_title("Acceptance distribution")
    ax_bar.set_ylim(0, max(1, artifacts.accepted_frames + artifacts.rejected_frames))

    ax_text.axis("off")
    total_frames = artifacts.accepted_frames + artifacts.rejected_frames
    acceptance_rate = (artifacts.accepted_frames / total_frames * 100.0) if total_frames else 0.0
    per_cycle = (artifacts.accepted_frames / artifacts.stable_cycles) if artifacts.stable_cycles else 0.0
    text_lines = [
        f"Alignment offset: {artifacts.alignment_offset_ms:.2f} ms",
        f"Stable cycles: {artifacts.stable_cycles}",
        f"Acceptance rate: {acceptance_rate:.1f}%",
        f"Accepted per stable cycle: {per_cycle:.2f}",
        f"Output folder: {artifacts.output_dir}",
    ]
    ax_text.text(0, 0.9, "Filter QA Metrics\n" + "\n".join(f"• {line}" for line in text_lines), va="top")
    fig_dash.tight_layout()
    fig_dash.savefig(dashboard_path, dpi=160)
    plt.close(fig_dash)
    outputs.append(dashboard_path)

    metrics_path = dest / "step4_filter_metrics.txt"
    metrics_path.write_text("\n".join(text_lines), encoding="utf-8")
    outputs.append(metrics_path)
    return outputs


def build_validation_scenarios() -> List[ValidationScenario]:
    # Model 12 population archetypes (pediatric, endurance, arrhythmic, obese, implant, etc.)
    # and deterministically sample until we produce 100 diverse cardiogram/DICOM pairs.
    rng = random.Random(20241130)

    def pick(bounds: Tuple[float, float], *, as_int: bool = False) -> float | int:
        low, high = bounds
        if as_int:
            return rng.randint(int(low), int(high))
        return rng.uniform(float(low), float(high))

    profiles = [
        {
            "label": "pediatric_fast",
            "beats": (12, 16),
            "rr": (480.0, 620.0),
            "frames": (12, 18),
            "spacing": (110.0, 150.0),
            "noise": (0.01, 0.04),
            "jitter": (1.5, 6.0),
            "spacing_noise": (2.0, 8.0),
            "offset": (-150.0, -60.0),
            "shape_options": [(48, 48), (48, 64)],
        },
        {
            "label": "athlete_conditioned",
            "beats": (14, 20),
            "rr": (600.0, 760.0),
            "frames": (14, 22),
            "spacing": (150.0, 200.0),
            "noise": (0.01, 0.03),
            "jitter": (2.0, 6.0),
            "spacing_noise": (4.0, 12.0),
            "offset": (-90.0, 10.0),
            "shape_options": [(64, 64), (64, 80)],
        },
        {
            "label": "general_adult",
            "beats": (16, 22),
            "rr": (700.0, 860.0),
            "frames": (14, 20),
            "spacing": (170.0, 220.0),
            "noise": (0.02, 0.07),
            "jitter": (3.0, 10.0),
            "spacing_noise": (6.0, 18.0),
            "offset": (-60.0, 60.0),
            "shape_options": [(56, 56), (64, 72), (72, 64)],
        },
        {
            "label": "arrhythmia_high_jitter",
            "beats": (18, 24),
            "rr": (540.0, 820.0),
            "frames": (16, 22),
            "spacing": (150.0, 210.0),
            "noise": (0.04, 0.12),
            "jitter": (10.0, 24.0),
            "spacing_noise": (12.0, 34.0),
            "offset": (-180.0, 120.0),
            "shape_options": [(64, 80), (72, 80), (80, 64)],
            "mode_bias": "outlier",
        },
        {
            "label": "hypertensive_slow",
            "beats": (18, 24),
            "rr": (880.0, 1080.0),
            "frames": (16, 22),
            "spacing": (210.0, 260.0),
            "noise": (0.02, 0.06),
            "jitter": (2.0, 8.0),
            "spacing_noise": (6.0, 18.0),
            "offset": (10.0, 120.0),
            "shape_options": [(80, 64), (80, 80), (96, 64)],
        },
        {
            "label": "obese_high_noise",
            "beats": (20, 26),
            "rr": (780.0, 940.0),
            "frames": (14, 20),
            "spacing": (190.0, 230.0),
            "noise": (0.08, 0.16),
            "jitter": (6.0, 14.0),
            "spacing_noise": (10.0, 24.0),
            "offset": (-40.0, 80.0),
            "shape_options": [(96, 96), (96, 80)],
        },
        {
            "label": "implant_noise",
            "beats": (18, 24),
            "rr": (720.0, 880.0),
            "frames": (14, 20),
            "spacing": (180.0, 220.0),
            "noise": (0.10, 0.22),
            "jitter": (8.0, 18.0),
            "spacing_noise": (14.0, 28.0),
            "offset": (-120.0, 60.0),
            "shape_options": [(64, 64), (80, 80)],
            "mode_bias": "outlier",
        },
        {
            "label": "low_snr_sparse",
            "beats": (16, 22),
            "rr": (760.0, 900.0),
            "frames": (10, 16),
            "spacing": (210.0, 260.0),
            "noise": (0.12, 0.20),
            "jitter": (4.0, 12.0),
            "spacing_noise": (8.0, 28.0),
            "offset": (-50.0, 70.0),
            "shape_options": [(80, 96), (72, 90)],
        },
        {
            "label": "sedation_brady",
            "beats": (20, 26),
            "rr": (920.0, 1100.0),
            "frames": (16, 22),
            "spacing": (220.0, 280.0),
            "noise": (0.02, 0.05),
            "jitter": (1.5, 6.0),
            "spacing_noise": (4.0, 12.0),
            "offset": (40.0, 140.0),
            "shape_options": [(80, 64), (96, 64)],
        },
        {
            "label": "stress_test",
            "beats": (14, 18),
            "rr": (480.0, 640.0),
            "frames": (18, 24),
            "spacing": (130.0, 180.0),
            "noise": (0.03, 0.08),
            "jitter": (6.0, 14.0),
            "spacing_noise": (10.0, 24.0),
            "offset": (-190.0, -30.0),
            "shape_options": [(64, 64), (72, 72)],
        },
        {
            "label": "vendor_highres",
            "beats": (18, 24),
            "rr": (700.0, 860.0),
            "frames": (18, 24),
            "spacing": (150.0, 210.0),
            "noise": (0.02, 0.06),
            "jitter": (4.0, 10.0),
            "spacing_noise": (6.0, 18.0),
            "offset": (-80.0, 40.0),
            "shape_options": [(88, 88), (96, 112)],
        },
        {
            "label": "sparse_gating",
            "beats": (18, 24),
            "rr": (780.0, 940.0),
            "frames": (12, 18),
            "spacing": (240.0, 320.0),
            "noise": (0.04, 0.10),
            "jitter": (6.0, 16.0),
            "spacing_noise": (18.0, 36.0),
            "offset": (-40.0, 120.0),
            "shape_options": [(80, 96), (96, 96)],
            "mode_bias": "outlier",
        },
        {
            "label": "post_op_recovery",
            "beats": (16, 22),
            "rr": (780.0, 920.0),
            "frames": (14, 20),
            "spacing": (190.0, 240.0),
            "noise": (0.05, 0.12),
            "jitter": (4.0, 12.0),
            "spacing_noise": (10.0, 24.0),
            "offset": (-20.0, 90.0),
            "shape_options": [(72, 80), (80, 88)],
        },
    ]

    scenarios: List[ValidationScenario] = []
    total_nominal = 100
    for idx in range(total_nominal):
        profile = profiles[idx % len(profiles)]
        beats = pick(profile["beats"], as_int=True)
        base_rr = pick(profile["rr"], as_int=False)
        frames = pick(profile["frames"], as_int=True)
        spacing = pick(profile["spacing"], as_int=False)
        noise_scale = pick(profile["noise"], as_int=False)
        jitter = pick(profile["jitter"], as_int=False)
        spacing_noise = pick(profile["spacing_noise"], as_int=False)
        offset = pick(profile["offset"], as_int=False)
        shape_options = profile["shape_options"]
        shape = shape_options[idx % len(shape_options)] if len(shape_options) == 1 else shape_options[rng.randrange(len(shape_options))]
        mode = "outlier" if profile.get("mode_bias") == "outlier" or idx % 11 == 0 else "normal"

        scenarios.append(
            ValidationScenario(
                name=f"case_{idx + 1:02d}",
                beats=int(beats),
                base_rr_ms=float(base_rr),
                frames=int(frames),
                spacing_ms=float(spacing),
                noise_scale=float(noise_scale),
                mode=mode,
                jitter_ms=float(jitter),
                spacing_noise_ms=float(spacing_noise),
                global_offset_ms=float(offset),
                dicom_shape=shape,
            ),
        )

    for idx in range(3):
        scenarios.append(
            ValidationScenario(
                name=f"case_missing_{idx + 1:02d}",
                beats=18 + idx,
                base_rr_ms=820.0 + idx * 30.0,
                frames=14,
                spacing_ms=190.0,
                mode="missing_cardio",
                jitter_ms=12.0,
                spacing_noise_ms=20.0,
                dicom_shape=(64, 64),
            ),
        )
    for idx in range(3):
        scenarios.append(
            ValidationScenario(
                name=f"case_corrupt_{idx + 1:02d}",
                beats=19 + idx,
                base_rr_ms=780.0 + idx * 35.0,
                frames=14,
                spacing_ms=200.0,
                mode="corrupt_dicom",
                jitter_ms=14.0,
                spacing_noise_ms=24.0,
                dicom_shape=(80, 64),
            ),
        )
    return scenarios


def apply_noise(cardio_path: Path, scale: float) -> None:
    if scale <= 0:
        return
    df = pd.read_csv(cardio_path)
    noise = np.random.normal(0.0, scale, size=len(df))
    df["signal"] += noise
    df.to_csv(cardio_path, index=False)


def create_corrupt_dicom_folder(target_dir: Path, count: int) -> None:
    target_dir.mkdir(parents=True, exist_ok=True)
    for idx in range(count):
        (target_dir / f"frame_{idx:04d}.dcm").write_text("NOT_A_DICOM", encoding="utf-8")


def evaluate_validation_requirements(summary_df: pd.DataFrame) -> pd.DataFrame:
    requirements = []
    success_mask = summary_df["status"].isin(["PASS", "PASS*"])
    success_count = int(success_mask.sum())
    requirements.append(
        {
            "id": "REQ-1",
            "requirement": ">=24 nominal/outlier scenarios must end in PASS or PASS*",
            "expected": ">=24 PASS/PASS*",
            "observed": f"{success_count} PASS/PASS*",
            "status": success_count >= 24,
        },
    )

    missing_cases = summary_df[summary_df["mode"] == "missing_cardio"]
    missing_ok = missing_cases.empty or bool((missing_cases["status"] == "EXPECTED FAIL").all())
    requirements.append(
        {
            "id": "REQ-2",
            "requirement": "All missing-cardio scenarios must raise EXPECTED FAIL",
            "expected": "missing_cardio -> EXPECTED FAIL",
            "observed": ", ".join(missing_cases["status"].tolist()) or "(none)",
            "status": missing_ok,
        },
    )

    corrupt_cases = summary_df[summary_df["mode"] == "corrupt_dicom"]
    corrupt_ok = corrupt_cases.empty or bool((corrupt_cases["status"] == "EXPECTED FAIL").all())
    requirements.append(
        {
            "id": "REQ-3",
            "requirement": "All corrupt-DICOM scenarios must raise EXPECTED FAIL",
            "expected": "corrupt_dicom -> EXPECTED FAIL",
            "observed": ", ".join(corrupt_cases["status"].tolist()) or "(none)",
            "status": corrupt_ok,
        },
    )

    pass_rows = summary_df[success_mask]
    avg_accept = float(pass_rows["accepted_frames"].mean()) if not pass_rows.empty else 0.0
    requirements.append(
        {
            "id": "REQ-4",
            "requirement": "PASS/PASS* runs must return >=4 frames on average",
            "expected": "Average accepted >= 4",
            "observed": f"{avg_accept:.2f} avg frames",
            "status": avg_accept >= 4.0,
        },
    )

    min_accept = float(pass_rows["accepted_frames"].min()) if not pass_rows.empty else 0.0
    requirements.append(
        {
            "id": "REQ-5",
            "requirement": "Every PASS/PASS* run must include at least 2 accepted frames",
            "expected": "Min accepted >= 2",
            "observed": f"min={min_accept:.0f}",
            "status": min_accept >= 2.0,
        },
    )

    return pd.DataFrame(requirements)


def run_validation_batch(demo_root: Path, config: ScanFilterConfig, dest: Path) -> List[Path]:
    scenarios = build_validation_scenarios()
    pipeline = ScanFilterPipeline(config)
    results = []
    for scenario in scenarios:
        scenario_root = dest / scenario.name
        scenario_root.mkdir(parents=True, exist_ok=True)
        cardio_path = scenario_root / "cardiogram.csv"
        dicom_dir = scenario_root / "dicom_series"
        filtered_dir = scenario_root / "filtered"
        status = "PASS"
        message = ""
        accepted_frames = 0
        try:
            if scenario.mode != "missing_cardio":
                generate_cardiogram(cardio_path, beats=scenario.beats, base_rr_ms=scenario.base_rr_ms)
                apply_noise(cardio_path, scenario.noise_scale)
            if scenario.mode != "corrupt_dicom":
                generate_dicom_series(
                    dicom_dir,
                    frame_count=scenario.frames,
                    spacing_ms=scenario.spacing_ms,
                    jitter_ms=scenario.jitter_ms,
                    spacing_noise_ms=scenario.spacing_noise_ms,
                    global_offset_ms=scenario.global_offset_ms,
                    shape=scenario.dicom_shape,
                )
            else:
                create_corrupt_dicom_folder(dicom_dir, scenario.frames)
            if scenario.mode == "missing_cardio":
                cardio_path.unlink(missing_ok=True)
            artifacts = pipeline.run(cardio_path, dicom_dir, filtered_dir)
            accepted_frames = artifacts.accepted_frames
            if scenario.mode in {"outlier"}:
                status = "PASS*"  # outlier handled
                message = "Handled outlier cadence"
            else:
                message = "Nominal run"
        except Exception as exc:  # noqa: BLE001
            status = "EXPECTED FAIL" if scenario.mode in {"missing_cardio", "corrupt_dicom"} else "FAIL"
            message = str(exc)
        results.append((scenario.name, scenario.mode, status, message, accepted_frames))

    summary_df = pd.DataFrame(results, columns=["scenario", "mode", "status", "details", "accepted_frames"])
    summary_csv = dest / "step5_validation_results.csv"
    summary_df.to_csv(summary_csv, index=False)

    requirements_df = evaluate_validation_requirements(summary_df)
    requirements_csv = dest / "step5_validation_requirements.csv"
    requirements_df.to_csv(requirements_csv, index=False)
    requirements_txt = dest / "step5_validation_requirements.txt"
    requirements_txt.write_text(
        "Validation Requirements\n" + "\n".join(
            f"[{row['id']}] {row['requirement']} -> {'PASS' if row['status'] else 'FAIL'} ({row['observed']})"
            for row in requirements_df.to_dict("records")
        ),
        encoding="utf-8",
    )

    status_counts = summary_df["status"].value_counts().sort_index()
    fig, ax = plt.subplots(figsize=(8, 4))
    status_counts.plot(kind="bar", ax=ax, color="#457b9d")
    ax.set_title("Step 5 – 100-Case Validation Sweep")
    ax.set_ylabel("Number of scenarios")
    ax.set_xlabel("Status")
    for idx, value in enumerate(status_counts.values):
        ax.text(idx, value + 0.2, str(value), ha="center")
    fig.tight_layout()
    chart_path = dest / "step5_validation_summary.png"
    fig.savefig(chart_path, dpi=160)
    plt.close(fig)
    outputs = [chart_path, requirements_csv, requirements_txt]

    status_order = ["PASS", "PASS*", "EXPECTED FAIL", "FAIL"]
    status_palette = {"PASS": "#2b9348", "PASS*": "#55a630", "EXPECTED FAIL": "#f8961e", "FAIL": "#d00000"}
    mode_counts = summary_df.pivot_table(index="mode", columns="status", aggfunc="size", fill_value=0)
    mode_counts = mode_counts.reindex(columns=status_order, fill_value=0)
    fig_mode, ax_mode = plt.subplots(figsize=(10, 4))
    mode_counts.plot(
        kind="bar",
        stacked=True,
        color=[status_palette[s] for s in status_order],
        ax=ax_mode,
    )
    ax_mode.set_ylabel("Scenario count")
    ax_mode.set_title("Validation status by scenario type")
    ax_mode.legend(title="Status", bbox_to_anchor=(1.02, 1), loc="upper left")
    fig_mode.tight_layout()
    mode_chart_path = dest / "step5_validation_by_mode.png"
    fig_mode.savefig(mode_chart_path, dpi=160)
    plt.close(fig_mode)
    outputs.append(mode_chart_path)

    cmap = ListedColormap([status_palette[s] for s in status_order])
    status_indices = np.array([status_order.index(s) if s in status_order else len(status_order) - 1 for s in summary_df["status"]])
    fig_timeline, ax_timeline = plt.subplots(figsize=(14, 2.8))
    matrix = status_indices[np.newaxis, :]
    ax_timeline.imshow(matrix, aspect="auto", cmap=cmap)
    ax_timeline.set_yticks([])
    ax_timeline.set_xticks(range(len(summary_df)))
    ax_timeline.set_xticklabels(summary_df["scenario"], rotation=90, fontsize=7)
    ax_timeline.set_title("Scenario-by-scenario validation timeline")
    handles = [plt.Line2D([0], [0], marker="s", color="w", markerfacecolor=status_palette[s], markersize=10, label=s) for s in status_order]
    ax_timeline.legend(handles=handles, bbox_to_anchor=(1.02, 1), loc="upper left")
    fig_timeline.tight_layout()
    timeline_path = dest / "step5_validation_timeline.png"
    fig_timeline.savefig(timeline_path, dpi=160)
    plt.close(fig_timeline)
    outputs.append(timeline_path)

    outputs.append(summary_csv)
    return outputs


def run_step(step: str, args: argparse.Namespace, config: ScanFilterConfig) -> Sequence[Path]:
    data_root = Path(args.data_root)
    output_root = Path(args.output_root)
    targets: List[Path] = []
    if step == "ingest":
        cardio_path, dicom_dir = ensure_inputs(data_root)
        dest = prepare_output_dir(output_root, "1_ingest")
        targets.extend(render_ingest_visuals(cardio_path, dicom_dir, dest))
    elif step == "alignment":
        cardio_path, dicom_dir = ensure_inputs(data_root)
        baseline_triggers = inject_alignment_noise(dicom_dir)
        dest = prepare_output_dir(output_root, "2_alignment")
        targets.extend(render_alignment_visuals(cardio_path, dicom_dir, config, dest, baseline_triggers))
    elif step == "cycles":
        cardio_path, _ = ensure_inputs(data_root)
        dest = prepare_output_dir(output_root, "3_cycles")
        targets.extend(render_cycles_visual(cardio_path, config, dest))
    elif step == "filter":
        cardio_path, dicom_dir = ensure_inputs(data_root)
        dest = prepare_output_dir(output_root, "4_filter")
        targets.extend(render_filter_visuals(cardio_path, dicom_dir, config, dest))
    elif step == "validation":
        dest = prepare_output_dir(output_root, "5_validation")
        targets.extend(run_validation_batch(Path(args.demo_root), config, dest))
    else:  # pragma: no cover
        raise ValueError(f"Unsupported step: {step}")
    return targets


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Five-step interactive demo helper")
    parser.add_argument("step", choices=STEP_NAMES, help="Which demo step to run")
    parser.add_argument("--config", default="config/default.yaml", help="Path to YAML config")
    parser.add_argument("--data-root", default="examples/data", help="Folder for sample cardiogram + DICOM inputs")
    parser.add_argument("--demo-root", default="examples/demo_runs", help="Folder for scenario outputs")
    parser.add_argument("--output-root", default="demo_steps", help="Destination folder for step visuals")
    parser.add_argument("--no-open", action="store_true", help="Do not auto-open generated visuals")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = ScanFilterConfig.load(args.config)
    targets = run_step(args.step, args, config)
    print(f"Step '{args.step}' completed. Generated:")
    for target in targets:
        print(f" - {target}")
    if not args.no_open:
        for target in targets:
            if target.exists():
                auto_open(target if target.is_file() else target.parent)


if __name__ == "__main__":
    main()
