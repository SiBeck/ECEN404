# CardiacGatingMRI

Retrospective k-space cardiac gating tool for MRI scans.  
Takes raw k-space `.mrd` files and an ECG cardiogram, identifies phase-encode lines acquired during cardiac motion, replaces them with clean lines from repeat scans, and outputs a motion-corrected image as both **PNG** and **DICOM**.

---

## What it does

MRI scanners acquire data in "k-space" (the spatial frequency domain). When the heart moves during acquisition, the affected k-space lines produce ghosting artefacts in the reconstructed image.

This tool:
1. Loads 10 repeated `.mrd` scans of the same slice
2. Reads an ECG cardiogram CSV to determine when the heart was moving
3. Flags each k-space line as "clean" or "corrupted" based on the cardiogram signal at its acquisition time
4. Builds a motion-free k-space by replacing corrupted lines from one scan with clean copies from the others
5. Reconstructs the corrected image via 2-D FFT and saves it as PNG + DICOM

---

## Quick start (pre-generated test data)

No installs required. The `publish/` folder contains a self-contained Windows executable that bundles the .NET 8 runtime.

```
.\publish\CardiacGatingMRI.exe --test-dir tests\test_data
```

Output is written to `tests\gated\`:
- `gated.png` — motion-corrected magnitude image
- `gated.dcm` — DICOM Secondary Capture file

---

## Folder structure

```
CardiacGatingMRI/
├── publish/               Self-contained executable (no .NET install needed)
│   └── CardiacGatingMRI.exe
├── tests/
│   ├── test_data/         Pre-generated test inputs
│   │   ├── cardiogram.csv         Synthetic ECG signal
│   │   ├── scan_00.mrd            10 corrupted k-space scans
│   │   ├── scan_00_linetimes.csv  Acquisition timestamps for each scan
│   │   ├── scan_00.png            Preview of each corrupted scan
│   │   └── ... (scan_01 – scan_09)
│   └── gated/             Output directory (created on run)
│       ├── gated.png
│       └── gated.dcm
├── Program.cs             Entry point / CLI
├── MrdReader.cs           Parses .mrd binary format
├── CardiogramReader.cs    Reads ECG CSV and classifies motion lines
├── GatingEngine.cs        Core line-replacement gating algorithm
├── ImageReconstructor.cs  2-D FFT reconstruction + PNG export
├── DicomWriter.cs         Writes DICOM Secondary Capture
├── TestDataGenerator.cs   Generates synthetic test scans from a reference .mrd
└── CardiacGatingMRI.csproj
```

---

## Usage modes

### Mode A — Generate new test data from a reference scan
Requires an original `.mrd` file (e.g. `te014.mrd`). Generates 10 synthetic corrupted scans, a cardiogram, and runs the full pipeline.

```
.\publish\CardiacGatingMRI.exe --test <path\to\te014.mrd> [--output <dir>]
```

### Mode B — Run with pre-generated test data (recommended for sharing)
Uses the `.mrd` files and `cardiogram.csv` already in a directory. No reference file needed.

```
.\publish\CardiacGatingMRI.exe --test-dir tests\test_data [--output <dir>]
```

### Mode C — Production use with your own data
Supply your own scans and cardiogram.

```
.\publish\CardiacGatingMRI.exe ^
    --cardiogram <cardio.csv> ^
    --scans <scan1.mrd> <scan2.mrd> ... ^
    [--line-times <times1.csv> <times2.csv> ...] ^
    [--base <index>] ^
    [--sensitivity <value>] ^
    [--output <dir>]
```

| Flag | Description | Default |
|---|---|---|
| `--cardiogram` | ECG CSV with columns `time_ms`, `signal` | required |
| `--scans` | One or more `.mrd` k-space files of the same slice | required |
| `--line-times` | Per-scan CSV of PE line acquisition times (ms). One file per scan. | 8 ms/line assumed |
| `--base` | Index of the scan used as the reconstruction template | `0` |
| `--sensitivity` | Motion threshold multiplier — lower flags more lines as corrupted (range 0.05–1.0) | `0.25` |
| `--output` | Output directory | `output_gating` |

---

## Cardiogram CSV format

```
# comment lines starting with # are ignored
time_ms,signal
0.000,0.012
2.000,0.045
...
```

---

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```
