# FinalCode

This folder contains a trimmed, self-contained version of the cardiac gating workflow.

Included:
- cardiogram CSV input reader
- MR Solutions `.mrd` reader
- cardiac cycle detection and stable-phase frame filtering
- DICOM export for accepted MRD frames
- sample MRD and cardiogram input files

Not included:
- demo scripts
- plotting utilities
- DICOM input pipeline
- unrelated test scaffolding

## Inputs

The runner expects:
1. a cardiogram CSV file with `timestamp_ms` and `signal` columns
2. an MR Solutions `.mrd` file

## Install

```powershell
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
```

## Run with a test case

```powershell
python main.py test_cases/case_01/cardiogram.csv test_cases/case_01/te014_case_01.mrd --output test_cases/case_01/gated_dicom_rerun
```

To also export the full reconstructed series before gating:

```powershell
python main.py test_cases/case_01/cardiogram.csv test_cases/case_01/te014_case_01.mrd --output test_cases/case_01/gated_dicom_rerun --export-reconstructed test_cases/case_01/full_reconstructed
```

## Config

Default settings live in `config/default.yaml`.