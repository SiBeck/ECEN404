from __future__ import annotations

import argparse
import math
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from scan_filter.config import ScanFilterConfig
from scan_filter.core.pipeline import ScanFilterPipeline


@dataclass(frozen=True)
class BatchCaseResult:
    case_name: str
    ok: bool
    input_frames: int
    accepted_frames: int
    rejected_frames: int
    output_dicom_count: int
    output_dir: Path
    error: str = ""


def _write_synthetic_mrd(
    path: Path,
    *,
    nfe: int,
    npe: int,
    frame_count: int,
    is_complex: bool,
    seed: int,
) -> None:
    rng = np.random.default_rng(seed)
    datatype_hex = "15" if is_complex else "05"
    shape = (nfe, npe, frame_count, 1, 1, 1)

    with path.open("wb") as handle:
        np.array([nfe, npe, frame_count, 1], dtype="<i4").tofile(handle)
        handle.write(b"\x00\x00")
        np.array([int(datatype_hex, 16)], dtype="<i2").tofile(handle)
        handle.write(b"\x00" * 132)
        np.array([1, 1], dtype="<i4").tofile(handle)
        if handle.tell() < 256:
            handle.write(b"\x00" * (256 - handle.tell()))

        comments = b"SYNTHETIC_MRD_BATCH_GATING"
        handle.write(comments + b"\x00" * (256 - len(comments)))

        if is_complex:
            real = rng.normal(0.0, 1.0, size=shape).astype(np.float32).ravel(order="F")
            imag = rng.normal(0.0, 1.0, size=shape).astype(np.float32).ravel(order="F")
            raw = np.empty(real.size * 2, dtype=np.float32)
            raw[0::2] = real
            raw[1::2] = imag
            raw.astype("<f4").tofile(handle)
        else:
            data = rng.normal(0.0, 1.0, size=shape).astype(np.float32).ravel(order="F")
            data.astype("<f4").tofile(handle)

        handle.write(b"\x00" * 120)
        handle.write(b"MRD_BATCH_GATING_PARAMETERS")


def _write_synthetic_cardiogram(path: Path, *, beats: int, base_rr_ms: float, seed: int) -> None:
    rng = np.random.default_rng(seed)
    sample_period_ms = 5.0
    total_ms = int(beats * base_rr_ms + 1200)
    timestamps = np.arange(0, total_ms, sample_period_ms, dtype=float)

    rr_variation = np.sin(np.linspace(0, math.pi * 2, beats)) * 0.06
    beat_times: list[float] = []
    cursor = 0.0
    for idx in range(beats):
        beat_times.append(cursor)
        cursor += base_rr_ms * (1.0 + rr_variation[idx])

    values: list[float] = []
    beat_idx = 0
    for ts in timestamps:
        baseline = 0.15 * math.sin(ts / 110.0) + float(rng.normal(0.0, 0.015))
        spike = 0.0
        if beat_idx < len(beat_times):
            delta = ts - beat_times[beat_idx]
            if abs(delta) < 25.0:
                spike = math.exp(-(delta**2) / 120.0) * 2.3
        values.append(baseline + spike)
        if beat_idx < len(beat_times) and ts >= beat_times[beat_idx] + 45.0:
            beat_idx += 1

    pd.DataFrame({"timestamp_ms": timestamps, "signal": values}).to_csv(path, index=False)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate 20 MRD+CSV cases, run gating, and export DICOM outputs")
    parser.add_argument("--count", type=int, default=20, help="Number of synthetic test cases")
    parser.add_argument(
        "--root",
        default="examples/mrd_csv_batch_runs",
        help="Root output folder for generated inputs and gated DICOM outputs",
    )
    parser.add_argument("--config", default="config/default.yaml", help="YAML config used by ScanFilterPipeline")
    parser.add_argument(
        "--open-dicom",
        action="store_true",
        help="Open a generated DICOM file in the local viewer after the batch run",
    )
    parser.add_argument(
        "--open-case-index",
        type=int,
        default=0,
        help="Case index to open when --open-dicom is provided (default: 0)",
    )
    parser.add_argument(
        "--open-all-dicoms",
        action="store_true",
        help="Open one generated DICOM (frame_0000.dcm) for every passing case",
    )
    parser.add_argument(
        "--open-passed-limit",
        type=int,
        default=5,
        help="When opening passed cases, open at most this many viewers (default: 5)",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    root = Path(args.root)
    root.mkdir(parents=True, exist_ok=True)

    cfg = ScanFilterConfig.load(args.config)
    pipeline = ScanFilterPipeline(cfg)

    results: list[BatchCaseResult] = []
    for idx in range(args.count):
        case_name = f"case_{idx:02d}"
        case_root = root / case_name
        case_root.mkdir(parents=True, exist_ok=True)

        mrd_path = case_root / "scan.mrd"
        csv_path = case_root / "cardiogram.csv"
        gated_dir = case_root / "gated_dicom"

        nfe = 24 + (idx % 6) * 8
        npe = 24 + (idx % 5) * 6
        frame_count = 8 + (idx % 10) * 3
        beats = 14 + (idx % 7)
        base_rr_ms = 620.0 + (idx % 8) * 40.0
        is_complex = bool(idx % 2)

        _write_synthetic_mrd(
            mrd_path,
            nfe=nfe,
            npe=npe,
            frame_count=frame_count,
            is_complex=is_complex,
            seed=7000 + idx,
        )
        _write_synthetic_cardiogram(
            csv_path,
            beats=beats,
            base_rr_ms=base_rr_ms,
            seed=9000 + idx,
        )

        try:
            artifacts = pipeline.run(csv_path, mrd_path, gated_dir)
            dicom_count = len(list(gated_dir.glob("*.dcm")))
            ok = dicom_count > 0
            results.append(
                BatchCaseResult(
                    case_name=case_name,
                    ok=ok,
                    input_frames=frame_count,
                    accepted_frames=artifacts.accepted_frames,
                    rejected_frames=artifacts.rejected_frames,
                    output_dicom_count=dicom_count,
                    output_dir=gated_dir,
                    error="" if ok else "No DICOM output files were written",
                ),
            )
        except Exception as exc:  # surfaced in summary per case
            results.append(
                BatchCaseResult(
                    case_name=case_name,
                    ok=False,
                    input_frames=frame_count,
                    accepted_frames=0,
                    rejected_frames=0,
                    output_dicom_count=0,
                    output_dir=gated_dir,
                    error=str(exc),
                ),
            )

    print("=== MRD+CSV Cardiac Gating Batch Results ===")
    passed = 0
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        if result.ok:
            passed += 1
        print(
            f"{result.case_name}: {status} | "
            f"in_frames={result.input_frames} accepted={result.accepted_frames} "
            f"rejected={result.rejected_frames} out_dcm={result.output_dicom_count} "
            f"path={result.output_dir}"
            + (f" | error={result.error}" if result.error else ""),
        )

    print(f"Summary: {passed}/{len(results)} cases passed")
    print(f"Root folder: {root}")

    if args.open_all_dicoms and results:
        opened = 0
        for case_result in results:
            if opened >= max(0, int(args.open_passed_limit)):
                break
            if not case_result.ok:
                continue
            candidate = case_result.output_dir / "frame_0000.dcm"
            if not candidate.exists():
                print(f"Skipping open for {case_result.case_name}; missing {candidate}")
                continue
            print(f"Opening DICOM viewer for {case_result.case_name}: {candidate}")
            subprocess.Popen([sys.executable, "main.py", "view-dicom", str(candidate)], cwd=PROJECT_ROOT)
            opened += 1
        print(f"Opened viewers for {opened} case(s) (limit={max(0, int(args.open_passed_limit))})")
    elif args.open_dicom and results:
        case_index = max(0, min(int(args.open_case_index), len(results) - 1))
        case_result = results[case_index]
        candidate = case_result.output_dir / "frame_0000.dcm"
        if candidate.exists():
            print(f"Opening DICOM viewer for: {candidate}")
            subprocess.Popen([sys.executable, "main.py", "view-dicom", str(candidate)], cwd=PROJECT_ROOT)
        else:
            print(f"Could not open viewer; missing expected DICOM file: {candidate}")

    if passed != len(results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
