from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from scan_filter.io.mrd_reader import MRDSolutionsReader, read_mrd, reconstruct_mrd_to_png

SAMPLE_PATH = Path(__file__).resolve().parents[1] / "mrd_code (1)" / "te014.mrd"
requires_sample = pytest.mark.skipif(not SAMPLE_PATH.exists(), reason="sample MRD file not present")


def _write_synthetic_mrd(
    path: Path,
    *,
    nfe: int,
    npe: int,
    n3d: int,
    nslice: int,
    nechoes: int,
    nexps: int,
    is_complex: bool,
    seed: int,
) -> int:
    rng = np.random.default_rng(seed)
    shape = (nfe, npe, n3d, nslice, nechoes, nexps)
    frame_count = max(1, n3d * nslice * nechoes * nexps)
    datatype_hex = "15" if is_complex else "05"  # float32 complex / float32 real

    with path.open("wb") as handle:
        np.array([nfe, npe, n3d, nslice], dtype="<i4").tofile(handle)
        handle.write(b"\x00\x00")
        np.array([int(datatype_hex, 16)], dtype="<i2").tofile(handle)
        handle.write(b"\x00" * 132)
        np.array([nechoes, nexps], dtype="<i4").tofile(handle)
        if handle.tell() < 256:
            handle.write(b"\x00" * (256 - handle.tell()))

        comments = b"SYNTHETIC_MRD_TEST"
        handle.write(comments + b"\x00" * (256 - len(comments)))

        if is_complex:
            real = rng.normal(0.0, 1.0, size=shape).astype(np.float32)
            imag = rng.normal(0.0, 1.0, size=shape).astype(np.float32)
            flat_real = real.ravel(order="F")
            flat_imag = imag.ravel(order="F")
            raw = np.empty(flat_real.size * 2, dtype=np.float32)
            raw[0::2] = flat_real
            raw[1::2] = flat_imag
            raw.astype("<f4").tofile(handle)
        else:
            data = rng.normal(0.0, 1.0, size=shape).astype(np.float32)
            data.ravel(order="F").astype("<f4").tofile(handle)

        handle.write(b"\x00" * 120)
        handle.write(b"NO_PARAMETERS")

    return frame_count


@requires_sample
def test_read_mrd_returns_expected_shape() -> None:
    data, header = read_mrd(SAMPLE_PATH)
    assert data.shape[0] == header.frequency_encoding
    assert data.shape[1] == header.phase_encoding
    assert header.frame_count == 1


@requires_sample
def test_mrd_reader_creates_frame_series() -> None:
    reader = MRDSolutionsReader(frame_period_ms=10.0, start_time_ms=5.0)
    series = reader.read(SAMPLE_PATH)
    assert len(series.frames) == 1
    frame = series.frames[0]
    assert frame.timestamp_ms == pytest.approx(5.0)
    assert isinstance(frame.dataset, np.ndarray)
    assert frame.dataset.shape == (128, 128)
    assert np.iscomplexobj(frame.dataset)


@requires_sample
def test_reconstruct_mrd_to_png_default_name(tmp_path: Path) -> None:
    local_mrd = tmp_path / "te014.mrd"
    local_mrd.write_bytes(SAMPLE_PATH.read_bytes())

    outputs = reconstruct_mrd_to_png(local_mrd)
    assert len(outputs) == 1
    assert outputs[0] == tmp_path / "te014.png"
    assert outputs[0].exists()


@requires_sample
def test_reconstruct_mrd_to_png_custom_output_file(tmp_path: Path) -> None:
    output_file = tmp_path / "custom_name.png"
    outputs = reconstruct_mrd_to_png(SAMPLE_PATH, output_path=output_file)
    assert len(outputs) == 1
    assert outputs[0] == output_file
    assert output_file.exists()


@pytest.mark.parametrize(
    ("case_name", "nfe", "npe", "n3d", "nslice", "nechoes", "nexps", "is_complex", "seed"),
    [
        ("rand_real_single", 64, 48, 1, 1, 1, 1, False, 101),
        ("rand_complex_single", 40, 36, 1, 1, 1, 1, True, 202),
        ("rand_real_multi", 32, 24, 2, 1, 1, 1, False, 303),
        ("rand_complex_multi", 24, 20, 1, 2, 1, 2, True, 404),
    ],
)
def test_reconstruct_random_synthetic_mrd_to_png(
    tmp_path: Path,
    case_name: str,
    nfe: int,
    npe: int,
    n3d: int,
    nslice: int,
    nechoes: int,
    nexps: int,
    is_complex: bool,
    seed: int,
) -> None:
    mrd_path = tmp_path / f"{case_name}.mrd"
    expected_frames = _write_synthetic_mrd(
        mrd_path,
        nfe=nfe,
        npe=npe,
        n3d=n3d,
        nslice=nslice,
        nechoes=nechoes,
        nexps=nexps,
        is_complex=is_complex,
        seed=seed,
    )
    outputs = reconstruct_mrd_to_png(mrd_path)

    assert len(outputs) == expected_frames
    for output in outputs:
        assert output.exists()
        assert output.suffix.lower() == ".png"
        assert output.stat().st_size > 0


def test_reconstruct_twenty_synthetic_mrd_inputs_and_collect_pngs(tmp_path: Path) -> None:
    inputs_dir = tmp_path / "inputs"
    outputs_dir = tmp_path / "png_outputs"
    inputs_dir.mkdir(parents=True, exist_ok=True)
    outputs_dir.mkdir(parents=True, exist_ok=True)

    created_pngs: list[Path] = []
    for idx in range(20):
        nfe = 24 + (idx % 5) * 8
        npe = 20 + (idx % 4) * 6
        mrd_path = inputs_dir / f"case_{idx:02d}.mrd"
        _write_synthetic_mrd(
            mrd_path,
            nfe=nfe,
            npe=npe,
            n3d=1,
            nslice=1,
            nechoes=1,
            nexps=1,
            is_complex=bool(idx % 2),
            seed=1000 + idx,
        )
        target_png = outputs_dir / f"case_{idx:02d}.png"
        outputs = reconstruct_mrd_to_png(mrd_path, output_path=target_png)
        assert len(outputs) == 1
        created_pngs.append(outputs[0])

    assert len(created_pngs) == 20
    for png_path in created_pngs:
        assert png_path.exists()
        assert png_path.suffix.lower() == ".png"
        assert png_path.stat().st_size > 0
