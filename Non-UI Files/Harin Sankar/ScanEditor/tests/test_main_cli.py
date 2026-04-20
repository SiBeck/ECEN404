from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import pytest

import main as cli_main

SAMPLE_PATH = Path(__file__).resolve().parents[1] / "mrd_code (1)" / "te014.mrd"
pytestmark = pytest.mark.skipif(not SAMPLE_PATH.exists(), reason="sample MRD file not present")


def test_main_mrd_to_png_subcommand_generates_output(tmp_path: Path) -> None:
    output_file = tmp_path / "te014.png"
    result = subprocess.run(
        [
            sys.executable,
            "main.py",
            "mrd-to-png",
            str(SAMPLE_PATH),
            "--output",
            str(output_file),
        ],
        capture_output=True,
        text=True,
        cwd=Path(__file__).resolve().parents[1],
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert output_file.exists()


def test_main_mrd_to_png_default_name_next_to_input(tmp_path: Path) -> None:
    local_mrd = tmp_path / "te014.mrd"
    local_mrd.write_bytes(SAMPLE_PATH.read_bytes())

    result = subprocess.run(
        [
            sys.executable,
            "main.py",
            "mrd-to-png",
            str(local_mrd),
        ],
        capture_output=True,
        text=True,
        cwd=Path(__file__).resolve().parents[1],
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert (tmp_path / "te014.png").exists()


def test_parse_args_gate_mrd_subcommand() -> None:
    args = cli_main.parse_args(
        [
            "gate-mrd",
            "cardio.csv",
            "scan.mrd",
            "--output",
            "out\\gated",
            "--export-reconstructed",
            "out\\full_recon",
        ],
    )
    assert args.command == "gate-mrd"
    assert args.cardio_csv == "cardio.csv"
    assert args.mrd_file == "scan.mrd"
    assert args.output == "out\\gated"
    assert args.export_reconstructed == "out\\full_recon"
