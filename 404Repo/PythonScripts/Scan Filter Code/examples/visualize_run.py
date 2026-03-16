from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
from typing import List, Sequence

import matplotlib.pyplot as plt
import pandas as pd
import pydicom

from scan_filter.io.csv_reader import CardiogramCsvReader


@dataclass
class FrameInfo:
    path: Path
    timestamp_ms: float
    accepted: bool


reader = CardiogramCsvReader()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Visualize Scan Filter inputs and outputs")
    parser.add_argument("cardiogram_csv", help="Path to cardiogram CSV")
    parser.add_argument("dicom_dir", help="Directory with original DICOM frames")
    parser.add_argument("filtered_dir", help="Directory with filtered DICOM frames")
    parser.add_argument(
        "--output",
        default="examples/visualizations",
        help="Folder to write plots and previews",
    )
    parser.add_argument(
        "--max-frame-previews",
        type=int,
        default=2,
        help="How many accepted/rejected frames to export as PNG previews",
    )
    return parser.parse_args()


def load_frames(dicom_dir: Path, accepted_names: set[str]) -> List[FrameInfo]:
    frames: List[FrameInfo] = []
    for file in sorted(dicom_dir.glob("*.dcm")):
        ds = pydicom.dcmread(file)
        trigger = float(ds.get("TriggerTime", 0.0))
        frames.append(FrameInfo(path=file, timestamp_ms=trigger, accepted=file.name in accepted_names))
    return frames


def plot_cardiogram_with_frames(
    cardiogram: pd.DataFrame,
    frames: Sequence[FrameInfo],
    dest: Path,
) -> Path:
    fig, ax = plt.subplots(figsize=(12, 4))
    ax.plot(cardiogram["timestamp_ms"], cardiogram["signal"], label="Cardiogram", color="#005f73")
    for frame in frames:
        color = "#94d2bd" if frame.accepted else "#ee9b00"
        alpha = 0.9 if frame.accepted else 0.4
        ax.axvline(frame.timestamp_ms, color=color, alpha=alpha, linewidth=1.2)
    frame_times = [frame.timestamp_ms for frame in frames]
    if frame_times:
        min_time = min(cardiogram["timestamp_ms"].min(), min(frame_times))
        max_time = max(cardiogram["timestamp_ms"].max(), max(frame_times))
        ax.set_xlim(min_time, max_time)
    ax.set_xlabel("Time (ms)")
    ax.set_ylabel("Signal (norm)")
    ax.set_title("Cardiogram with MRI frame timings")
    ax.legend(["Cardiogram", "Accepted frame", "Rejected frame"], loc="upper right")
    fig.tight_layout()
    output_path = dest / "cardiogram_frames.png"
    fig.savefig(output_path, dpi=160)
    plt.close(fig)
    return output_path


def export_frame_previews(frames: Sequence[FrameInfo], dest: Path, max_per_group: int) -> List[Path]:
    exported: List[Path] = []
    groups = {
        "accepted": [frame for frame in frames if frame.accepted],
        "rejected": [frame for frame in frames if not frame.accepted],
    }
    for label, items in groups.items():
        for idx, frame in enumerate(items[: max_per_group]):
            ds = pydicom.dcmread(frame.path)
            pixel_array = ds.pixel_array.astype(float)
            # Normalize for display
            pixel_array -= pixel_array.min()
            denom = pixel_array.max() or 1.0
            pixel_array /= denom
            fig, ax = plt.subplots(figsize=(3, 3))
            ax.imshow(pixel_array, cmap="gray")
            ax.axis("off")
            ax.set_title(f"{label.capitalize()} #{idx} ({frame.timestamp_ms:.0f} ms)")
            out_path = dest / f"{label}_frame_{idx}.png"
            fig.savefig(out_path, dpi=180, bbox_inches="tight", pad_inches=0.05)
            plt.close(fig)
            exported.append(out_path)
    return exported


def write_summary(frames: Sequence[FrameInfo], dest: Path) -> Path:
    summary_path = dest / "summary.txt"
    accepted = sum(frame.accepted for frame in frames)
    rejected = len(frames) - accepted
    with open(summary_path, "w", encoding="utf-8") as handle:
        handle.write("Scan Filter Visualization Summary\n")
        handle.write(f"Total frames: {len(frames)}\n")
        handle.write(f"Accepted frames: {accepted}\n")
        handle.write(f"Rejected frames: {rejected}\n")
        handle.write("\nFrame timestamps (ms):\n")
        for frame in frames:
            status = "ACCEPTED" if frame.accepted else "REJECTED"
            handle.write(f"{frame.path.name}: {frame.timestamp_ms:.2f} ms ({status})\n")
    return summary_path


def main() -> None:
    args = parse_args()
    dest = Path(args.output)
    dest.mkdir(parents=True, exist_ok=True)

    cardio_df = reader.read(args.cardiogram_csv).to_dataframe()
    filtered_names = {path.name for path in Path(args.filtered_dir).glob("*.dcm")}
    frames = load_frames(Path(args.dicom_dir), filtered_names)

    plot_path = plot_cardiogram_with_frames(cardio_df, frames, dest)
    preview_paths = export_frame_previews(frames, dest, args.max_frame_previews)
    summary_path = write_summary(frames, dest)

    print("Visualization artifacts created:")
    print(" -", plot_path)
    for path in preview_paths:
        print(" -", path)
    print(" -", summary_path)


if __name__ == "__main__":
    main()
