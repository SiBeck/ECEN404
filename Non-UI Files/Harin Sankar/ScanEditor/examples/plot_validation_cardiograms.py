from __future__ import annotations

import argparse
import math
from pathlib import Path
from typing import Iterable, List, Tuple

import matplotlib.pyplot as plt
import pandas as pd

DEFAULT_ROOT = Path("demo_steps/latest_validation/step_5_validation")
DEFAULT_OUTPUT_DIR = DEFAULT_ROOT / "cardiogram_plots"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Plot cardiogram traces for validation scenarios")
    parser.add_argument(
        "--root",
        type=Path,
        default=DEFAULT_ROOT,
        help="Folder containing validation scenario subdirectories",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT_DIR,
        help="Destination folder for generated PNGs",
    )
    parser.add_argument(
        "--plots-per-figure",
        type=int,
        default=6,
        help="Number of cardiogram traces per figure",
    )
    parser.add_argument(
        "--cols",
        type=int,
        default=3,
        help="Number of subplot columns per figure",
    )
    return parser.parse_args()


def load_series(root: Path) -> Tuple[List[Tuple[str, pd.DataFrame]], List[str]]:
    if not root.exists():
        raise FileNotFoundError(f"Validation root not found: {root}")
    scenarios: List[Tuple[str, pd.DataFrame]] = []
    missing: List[str] = []
    for child in sorted(root.iterdir()):
        if not child.is_dir():
            continue
        cardio_csv = child / "cardiogram.csv"
        if cardio_csv.exists():
            df = pd.read_csv(cardio_csv)
            if {"timestamp_ms", "signal"}.issubset(df.columns):
                scenarios.append((child.name, df))
            else:
                missing.append(f"{child.name} (invalid columns)")
        else:
            missing.append(f"{child.name} (no cardiogram.csv)")
    if not scenarios:
        raise RuntimeError(f"No cardiogram files found under {root}")
    return scenarios, missing


def chunked(items: List[Tuple[str, pd.DataFrame]], size: int) -> Iterable[List[Tuple[str, pd.DataFrame]]]:
    for idx in range(0, len(items), size):
        yield items[idx : idx + size]


def render_figures(
    series: List[Tuple[str, pd.DataFrame]],
    output_dir: Path,
    plots_per_figure: int,
    cols: int,
) -> List[Path]:
    output_dir.mkdir(parents=True, exist_ok=True)
    saved: List[Path] = []
    rows = math.ceil(plots_per_figure / cols)
    for fig_idx, chunk in enumerate(chunked(series, plots_per_figure), start=1):
        fig, axes = plt.subplots(rows, cols, figsize=(cols * 4, rows * 2.5), squeeze=False)
        flat_axes = [ax for row in axes for ax in row]
        for ax, (name, df) in zip(flat_axes, chunk):
            ax.plot(df["timestamp_ms"], df["signal"], color="#1d4ed8", linewidth=0.9)
            ax.set_title(name, fontsize=9)
            ax.set_xlabel("Time (ms)", fontsize=8)
            ax.set_ylabel("Signal", fontsize=8)
            ax.grid(alpha=0.25)
        for extra_ax in flat_axes[len(chunk) :]:
            extra_ax.axis("off")
        fig.tight_layout()
        path = output_dir / f"cardiograms_page_{fig_idx:02d}.png"
        fig.savefig(path, dpi=170)
        plt.close(fig)
        saved.append(path)
    return saved


def main() -> None:
    args = parse_args()
    series, missing = load_series(args.root)
    image_paths = render_figures(series, args.output_dir, args.plots_per_figure, args.cols)
    if missing:
        print("Skipped scenarios (no usable cardiogram):")
        for name in missing:
            print(f" - {name}")
    print("\nGenerated cardiogram figures:")
    for path in image_paths:
        print(f" - {path}")


if __name__ == "__main__":
    main()
