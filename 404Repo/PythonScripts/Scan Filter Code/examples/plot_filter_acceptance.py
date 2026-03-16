from __future__ import annotations

import argparse
from pathlib import Path

import matplotlib.pyplot as plt
import pandas as pd

DEFAULT_SUMMARY = Path("demo_steps/four_step_demo/four_step_case_summary.csv")
DEFAULT_OUTPUT = Path("demo_steps/four_step_demo/step_4_filter/accepted_vs_rejected.png")
DEFAULT_TABLE = Path("demo_steps/four_step_demo/step_4_filter/per_case_frame_disposition.csv")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Plot accepted vs rejected MRI frames per case")
    parser.add_argument(
        "--summary",
        type=Path,
        default=DEFAULT_SUMMARY,
        help="Path to four_step_case_summary.csv",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
        help="Destination for the stacked bar chart PNG",
    )
    parser.add_argument(
        "--table-output",
        type=Path,
        default=DEFAULT_TABLE,
        help="Optional CSV export for the per-case table",
    )
    return parser.parse_args()


def load_summary(summary_path: Path) -> pd.DataFrame:
    if not summary_path.exists():
        raise FileNotFoundError(f"Summary file not found: {summary_path}")
    df = pd.read_csv(summary_path)
    required_columns = {"case", "accepted_frames", "rejected_frames"}
    missing = required_columns - set(df.columns)
    if missing:
        raise ValueError(f"Summary file missing columns: {', '.join(sorted(missing))}")
    df = df.sort_values("case").reset_index(drop=True)
    return df


def render_chart(df: pd.DataFrame, output_path: Path) -> Path:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    positions = range(len(df))
    fig, ax = plt.subplots(figsize=(12, 6))
    ax.bar(positions, df["accepted_frames"], label="Accepted", color="#2b9348")
    ax.bar(
        positions,
        df["rejected_frames"],
        bottom=df["accepted_frames"],
        label="Rejected",
        color="#d00000",
    )
    ax.set_xticks(list(positions))
    ax.set_xticklabels(df["case"], rotation=90)
    ax.set_ylabel("Frame count")
    ax.set_title("Accepted vs. rejected MRI frames per case")
    ax.legend()
    for idx, (accepted, rejected) in enumerate(zip(df["accepted_frames"], df["rejected_frames"])):
        total = accepted + rejected
        ax.text(idx, accepted / 2, f"{accepted}", ha="center", va="center", color="white", fontsize=8)
        if rejected:
            ax.text(idx, accepted + rejected / 2, f"{rejected}", ha="center", va="center", color="white", fontsize=8)
        ax.text(idx, total + 0.5, f"{total}", ha="center", va="bottom", fontsize=7, color="#333333")
    fig.tight_layout()
    fig.savefig(output_path, dpi=170)
    plt.close(fig)
    return output_path


def print_summary(df: pd.DataFrame, table_output: Path | None) -> None:
    table = df[["case", "accepted_frames", "rejected_frames", "acceptance_rate"]].copy()
    if table_output:
        table_output.parent.mkdir(parents=True, exist_ok=True)
        table.to_csv(table_output, index=False)
    printable = table.copy()
    printable["acceptance_rate"] = printable["acceptance_rate"].map(lambda v: f"{float(v):.1f}%")
    print("\nPer-case frame disposition:")
    print(printable.to_string(index=False))
    if table_output:
        print(f"\nTable saved to {table_output}")


def main() -> None:
    args = parse_args()
    summary_df = load_summary(args.summary)
    chart_path = render_chart(summary_df, args.output)
    print_summary(summary_df, args.table_output)
    print(f"\nChart saved to {chart_path}")


if __name__ == "__main__":
    main()
