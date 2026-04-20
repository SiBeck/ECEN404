from __future__ import annotations

import argparse
import csv
import math
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from cardiac_gating.config import ScanFilterConfig
from cardiac_gating.pipeline import ScanFilterPipeline


@dataclass(frozen=True)
class CaseSpec:
    index: int
    nfe: int
    npe: int
    n3d: int
    nslice: int
    nechoes: int
    nexps: int
    is_complex: bool
    frame_period_ms: float
    profile: str
    cardio_profile: str
    seed: int

    @property
    def frame_count(self) -> int:
        return self.n3d * self.nslice * self.nechoes * self.nexps


@dataclass(frozen=True)
class CaseResult:
    case_name: str
    ok: bool
    frame_count: int
    accepted_frames: int
    rejected_frames: int
    stable_cycles: int
    output_dicom_count: int
    mrd_file: str
    cardio_file: str
    error: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate diverse MRD + cardiogram test cases and validate with gating",
    )
    parser.add_argument("--count", type=int, default=20, help="Number of cases to generate")
    parser.add_argument("--seed", type=int, default=20260330, help="Seed for reproducible randomization")
    parser.add_argument(
        "--root",
        default="data/test_cases",
        help="Output folder for generated cases",
    )
    parser.add_argument(
        "--config",
        default="config/default.yaml",
        help="Gating configuration used during validation",
    )
    parser.add_argument(
        "--skip-gating",
        action="store_true",
        help="Only generate files without running gating validation",
    )
    return parser.parse_args()


def build_case_specs(count: int, seed: int) -> list[CaseSpec]:
    rng = np.random.default_rng(seed)
    mrd_profiles = [
        "radial_wave",
        "moving_blob",
        "checker_drift",
        "stripe_mix",
        "ring_burst",
        "dual_blob",
        "spiral_phase",
        "sparse_peaks",
    ]
    cardio_profiles = ["steady", "variable", "artifact", "ectopic"]
    dims = [(24, 24), (32, 28), (40, 36), (48, 40), (56, 48), (64, 56), (72, 64), (96, 72), (112, 84), (128, 96)]
    factors = [(1, 1, 1), (2, 1, 1), (1, 2, 1), (1, 1, 2), (2, 2, 1), (2, 1, 2)]

    specs: list[CaseSpec] = []
    for idx in range(count):
        nfe, npe = dims[idx % len(dims)]
        nslice, nechoes, nexps = factors[idx % len(factors)]
        frame_count_target = int(rng.integers(10, 65))
        divisor = max(1, nslice * nechoes * nexps)
        n3d = max(1, frame_count_target // divisor)
        specs.append(
            CaseSpec(
                index=idx,
                nfe=int(nfe),
                npe=int(npe),
                n3d=int(n3d),
                nslice=int(nslice),
                nechoes=int(nechoes),
                nexps=int(nexps),
                is_complex=bool((idx % 3) != 0),
                frame_period_ms=float(rng.uniform(25.0, 90.0)),
                profile=mrd_profiles[idx % len(mrd_profiles)],
                cardio_profile=cardio_profiles[idx % len(cardio_profiles)],
                seed=int(seed + idx * 97),
            )
        )
    return specs


def _grid(nfe: int, npe: int) -> tuple[np.ndarray, np.ndarray]:
    x = np.linspace(-1.0, 1.0, nfe)
    y = np.linspace(-1.0, 1.0, npe)
    return np.meshgrid(x, y, indexing="ij")


def _profile_frame(
    x: np.ndarray,
    y: np.ndarray,
    *,
    profile: str,
    progress: float,
    rng: np.random.Generator,
    is_complex: bool,
) -> np.ndarray:
    radius = np.sqrt(x**2 + y**2)
    theta = np.arctan2(y, x)

    if profile == "radial_wave":
        amplitude = np.exp(-((radius - (0.2 + 0.55 * progress)) ** 2) / (0.02 + 0.08 * (1.0 - progress)))
    elif profile == "moving_blob":
        cx = -0.7 + 1.4 * progress
        cy = 0.35 * np.sin(2.0 * np.pi * progress)
        amplitude = np.exp(-(((x - cx) ** 2) / 0.06 + ((y - cy) ** 2) / 0.08))
    elif profile == "checker_drift":
        amplitude = 0.5 + 0.5 * np.sin((10.0 + 8.0 * progress) * x) * np.cos((9.0 - 6.0 * progress) * y)
    elif profile == "stripe_mix":
        amplitude = np.abs(np.sin((8.0 + 4.0 * progress) * x + 2.5 * y) + 0.35 * np.sin(14.0 * y))
    elif profile == "ring_burst":
        ring = np.exp(-((radius - (0.3 + 0.3 * np.sin(2.0 * np.pi * progress))) ** 2) / 0.02)
        core = np.exp(-(radius**2) / (0.03 + 0.02 * progress))
        amplitude = ring + 0.6 * core
    elif profile == "dual_blob":
        c1 = np.exp(-(((x + 0.45 - 0.2 * progress) ** 2) + ((y + 0.25) ** 2)) / 0.05)
        c2 = np.exp(-(((x - 0.35 + 0.15 * progress) ** 2) + ((y - 0.15) ** 2)) / 0.07)
        amplitude = c1 + c2
    elif profile == "spiral_phase":
        amplitude = np.exp(-radius**2 / (0.22 + 0.18 * progress))
    elif profile == "sparse_peaks":
        amplitude = np.zeros_like(x)
        peak_count = 6 + int(10 * progress)
        for _ in range(peak_count):
            cx = rng.uniform(-1.0, 1.0)
            cy = rng.uniform(-1.0, 1.0)
            sx = rng.uniform(0.02, 0.08)
            sy = rng.uniform(0.02, 0.08)
            amplitude += np.exp(-(((x - cx) ** 2) / sx + ((y - cy) ** 2) / sy))
    else:
        amplitude = np.exp(-radius**2 / 0.25)

    amplitude = amplitude + rng.normal(0.0, 0.03 + 0.02 * progress, size=amplitude.shape)
    amplitude = np.clip(amplitude, 0.0, None)

    phase = 2.0 * np.pi * (0.5 * radius + 0.25 * np.sin(3.0 * theta + progress * 2.0 * np.pi))
    if profile == "spiral_phase":
        phase = phase + (4.0 + 3.0 * progress) * theta

    if is_complex:
        signal = amplitude * np.exp(1j * phase)
    else:
        signal = amplitude * np.cos(phase)

    return signal.astype(np.complex64 if is_complex else np.float32)


def build_mrd_tensor(spec: CaseSpec) -> np.ndarray:
    rng = np.random.default_rng(spec.seed)
    x, y = _grid(spec.nfe, spec.npe)
    frames: list[np.ndarray] = []
    frame_count = spec.frame_count
    for frame_idx in range(frame_count):
        progress = frame_idx / max(frame_count - 1, 1)
        frame = _profile_frame(
            x,
            y,
            profile=spec.profile,
            progress=progress,
            rng=rng,
            is_complex=spec.is_complex,
        )
        frames.append(frame)

    if spec.is_complex:
        stacked = np.stack([np.asarray(frame, dtype=np.complex64) for frame in frames], axis=2)
    else:
        stacked = np.stack([np.asarray(np.real(frame), dtype=np.float32) for frame in frames], axis=2)

    shaped = stacked.reshape(
        (spec.nfe, spec.npe, spec.n3d, spec.nslice, spec.nechoes, spec.nexps),
        order="F",
    )
    return shaped


def write_mrd(path: Path, spec: CaseSpec, data: np.ndarray) -> None:
    datatype_hex = "15" if spec.is_complex else "05"
    comments = (
        f"FINALCODE_CASE_{spec.index:02d}|{spec.profile}|"
        f"{spec.nfe}x{spec.npe}|frames={spec.frame_count}|complex={int(spec.is_complex)}"
    ).encode("ascii", errors="ignore")
    parameters = (
        f"profile={spec.profile};cardio={spec.cardio_profile};"
        f"frame_period_ms={spec.frame_period_ms:.3f};seed={spec.seed}"
    ).encode("ascii", errors="ignore")

    with path.open("wb") as handle:
        np.array([spec.nfe, spec.npe, spec.n3d, spec.nslice], dtype="<i4").tofile(handle)
        handle.write(b"\x00\x00")
        np.array([int(datatype_hex, 16)], dtype="<i2").tofile(handle)
        handle.write(b"\x00" * 132)
        np.array([spec.nechoes, spec.nexps], dtype="<i4").tofile(handle)

        if handle.tell() < 256:
            handle.write(b"\x00" * (256 - handle.tell()))

        if len(comments) >= 256:
            comments = comments[:255]
        handle.write(comments + b"\x00" * (256 - len(comments)))

        if spec.is_complex:
            flat = np.asarray(data, dtype=np.complex64).ravel(order="F")
            raw = np.empty(flat.size * 2, dtype=np.float32)
            raw[0::2] = np.real(flat).astype(np.float32)
            raw[1::2] = np.imag(flat).astype(np.float32)
            raw.astype("<f4").tofile(handle)
        else:
            np.asarray(data, dtype=np.float32).ravel(order="F").astype("<f4").tofile(handle)

        handle.write(b"\x00" * 120)
        handle.write(parameters)


def write_cardiogram(path: Path, spec: CaseSpec) -> None:
    rng = np.random.default_rng(spec.seed + 11)
    sample_period_ms = float(rng.uniform(4.0, 8.0))
    total_ms = max(8000.0, spec.frame_count * spec.frame_period_ms + 4000.0)
    timestamps = np.arange(0.0, total_ms, sample_period_ms)

    rr_base = float(rng.uniform(550.0, 1100.0))
    rr_jitter = float(rng.uniform(0.03, 0.14))
    beat_times: list[float] = []
    cursor = float(rng.uniform(0.0, 120.0))
    beat_idx = 0
    while cursor < total_ms + 1000.0:
        oscillation = 1.0 + rr_jitter * math.sin(2.0 * math.pi * beat_idx / float(rng.integers(5, 14)))
        rr = rr_base * oscillation + rng.normal(0.0, rr_base * rr_jitter * 0.4)
        if spec.cardio_profile == "variable":
            rr *= 1.0 + 0.08 * math.sin(beat_idx * 0.47)
        if spec.cardio_profile == "ectopic" and beat_idx % 9 == 5:
            rr *= 0.72
        beat_times.append(cursor)
        cursor += max(320.0, rr)
        beat_idx += 1

    values = np.zeros_like(timestamps)
    baseline = 0.15 * np.sin(timestamps / 900.0) + 0.05 * np.sin(timestamps / 130.0)
    values += baseline

    for bt in beat_times:
        qrs = np.exp(-((timestamps - bt) ** 2) / (2.0 * (9.0**2))) * rng.uniform(1.8, 2.8)
        t_wave = np.exp(-((timestamps - (bt + 130.0)) ** 2) / (2.0 * (22.0**2))) * rng.uniform(0.18, 0.38)
        values += qrs + t_wave

    noise_std = 0.025
    if spec.cardio_profile == "artifact":
        noise_std = 0.055
        burst_center = float(rng.uniform(total_ms * 0.25, total_ms * 0.75))
        burst = np.exp(-((timestamps - burst_center) ** 2) / (2.0 * (280.0**2)))
        values += burst * rng.uniform(0.4, 0.9)
    values += rng.normal(0.0, noise_std, size=timestamps.shape)

    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["timestamp_ms", "signal"])
        for ts, val in zip(timestamps, values):
            writer.writerow([f"{float(ts):.6f}", f"{float(val):.9f}"])


def run() -> None:
    args = parse_args()
    root = Path(args.root)
    root.mkdir(parents=True, exist_ok=True)

    specs = build_case_specs(args.count, args.seed)
    pipeline = None
    if not args.skip_gating:
        config = ScanFilterConfig.load(args.config)
        config.io.mrd_start_time_ms = 0.0
        pipeline = ScanFilterPipeline(config)

    results: list[CaseResult] = []
    for spec in specs:
        case_name = f"case_{spec.index + 1:02d}"
        case_dir = root / case_name
        case_dir.mkdir(parents=True, exist_ok=True)

        mrd_path = case_dir / f"te014_case_{spec.index + 1:02d}.mrd"
        csv_path = case_dir / "cardiogram.csv"
        gated_output_dir = case_dir / "gated_dicom"

        mrd_tensor = build_mrd_tensor(spec)
        write_mrd(mrd_path, spec, mrd_tensor)
        write_cardiogram(csv_path, spec)

        if pipeline is None:
            results.append(
                CaseResult(
                    case_name=case_name,
                    ok=True,
                    frame_count=spec.frame_count,
                    accepted_frames=0,
                    rejected_frames=0,
                    stable_cycles=0,
                    output_dicom_count=0,
                    mrd_file=str(mrd_path),
                    cardio_file=str(csv_path),
                    error="",
                )
            )
            continue

        try:
            artifacts = pipeline.run(csv_path, mrd_path, gated_output_dir)
            dicom_count = len(list(gated_output_dir.glob("*.dcm")))
            ok = dicom_count > 0
            results.append(
                CaseResult(
                    case_name=case_name,
                    ok=ok,
                    frame_count=spec.frame_count,
                    accepted_frames=artifacts.accepted_frames,
                    rejected_frames=artifacts.rejected_frames,
                    stable_cycles=artifacts.stable_cycles,
                    output_dicom_count=dicom_count,
                    mrd_file=str(mrd_path),
                    cardio_file=str(csv_path),
                    error="" if ok else "No DICOM files produced",
                )
            )
        except Exception as exc:
            results.append(
                CaseResult(
                    case_name=case_name,
                    ok=False,
                    frame_count=spec.frame_count,
                    accepted_frames=0,
                    rejected_frames=0,
                    stable_cycles=0,
                    output_dicom_count=0,
                    mrd_file=str(mrd_path),
                    cardio_file=str(csv_path),
                    error=str(exc),
                )
            )

    summary_path = root / "summary.csv"
    with summary_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(
            [
                "case_name",
                "ok",
                "frame_count",
                "accepted_frames",
                "rejected_frames",
                "stable_cycles",
                "output_dicom_count",
                "mrd_file",
                "cardio_file",
                "error",
            ]
        )
        for result in results:
            writer.writerow(
                [
                    result.case_name,
                    int(result.ok),
                    result.frame_count,
                    result.accepted_frames,
                    result.rejected_frames,
                    result.stable_cycles,
                    result.output_dicom_count,
                    result.mrd_file,
                    result.cardio_file,
                    result.error,
                ]
            )

    passed = sum(1 for result in results if result.ok)
    print(f"Generated {len(results)} cases in {root}")
    if pipeline is None:
        print("Gating validation skipped")
    else:
        print(f"Validation passed for {passed}/{len(results)} cases")
    print(f"Summary: {summary_path}")

    for result in results:
        status = "PASS" if result.ok else "FAIL"
        suffix = f" | error={result.error}" if result.error else ""
        print(
            f"{result.case_name}: {status} | frames={result.frame_count} "
            f"accepted={result.accepted_frames} rejected={result.rejected_frames} "
            f"stable={result.stable_cycles} dcm={result.output_dicom_count}{suffix}"
        )


if __name__ == "__main__":
    run()