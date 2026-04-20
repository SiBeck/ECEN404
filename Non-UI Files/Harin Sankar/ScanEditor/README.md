# Scan Filter Subsystem

The Scan Filter aligns cleaned cardiogram CSV data with a DICOM MRI series, detects stable cardiac cycles, and exports a filtered DICOM series containing only frames acquired during stable cardiac phases.

## Features
- CSV-driven cardiogram ingestion with configurable column names
- Time alignment between ECG samples and MRI frames
- Adaptive windowing cycle detection that tolerates noisy beats
- Stable phase selection and DICOM frame filtering
- Optional MR Solutions *.mrd ingestion that reconstructs raw k-space into a synthetic DICOM stack
- Configurable YAML settings, structured logging, and unit tests

## Installation

```bash
python -m venv .venv
.\.venv\Scripts\activate
pip install -e .[dev]
```

## Configuration

Edit `config/default.yaml` or supply a different file via the `--config` flag. Key sections:
- `alignment`: max tolerated offset in milliseconds
- `cycle_detection`: adaptive window tuning and stability thresholds
- `filtering`: window inside each stable cycle treated as quiescent phase
- `io`: default output directory for filtered frames

## Usage

```bash
python main.py data/cardiogram.csv data/dicom_series --output output/filtered
```

This command prints a concise summary including accepted/rejected frames and writes the filtered DICOM series to the output folder.

### Using MR Solutions *.mrd files

You can feed the pipeline raw MR Solutions exports as long as you pass the `.mrd` file instead of a DICOM directory. The ingest path keeps MRD k-space frames for timing/alignment and cardiac gating, then performs reconstruction and DICOM conversion only for accepted frames at the final output stage.

```bash
python main.py data/cardiogram.csv "mrd_code (1)/te014.mrd" --output output/mrd_filtered
```

You can also use the dedicated MRD gating command (same processing, clearer intent):

```bash
python main.py gate-mrd data/cardiogram.csv "mrd_code (1)/te014.mrd" --output output/mrd_gated
```

If you want both outputs in one run:
- gated/filtered DICOM (undistorted stable-phase frames), and
- full reconstructed MRD->DICOM (all frames before gating),
use:

```bash
python main.py gate-mrd data/cardiogram.csv "mrd_code (1)/te014.mrd" --output output/mrd_gated --export-reconstructed output/mrd_reconstructed_full
```

Tune the generated frame timestamps by editing the `io.mrd_frame_period_ms` and `io.mrd_start_time_ms` entries inside `config/default.yaml`. Optional parameters let you enable a 2-D Hamming window or request zero-padding before the inverse FFT (`io.mrd_apply_hamming_window`, `io.mrd_zero_pad_fe`, and `io.mrd_zero_pad_pe`).

### Reconstruct an `.mrd` file directly to PNG

If you want a direct image export (for example `te014.mrd -> te014.png`), run:

```bash
python main.py mrd-to-png "mrd_code (1)/te014.mrd"
```

You can also choose an explicit output file for single-frame scans:

```bash
python main.py mrd-to-png "mrd_code (1)/te014.mrd" --output output/te014.png
```

PowerShell wrapper (single command):

```powershell
.\mrd2png.ps1 "mrd_code (1)\te014.mrd"
```

This writes `mrd_code (1)\te014.png` by default.  
To choose an explicit output path:

```powershell
.\mrd2png.ps1 "mrd_code (1)\te014.mrd" "output\te014.png"
```

### Batch stress test: 20 synthetic MRD inputs

To generate 20 synthetic `.mrd` files and reconstruct each into PNG:

```bash
python examples/generate_mrd_batch_demo.py --count 20
```

Outputs are written to:
- `examples/mrd_batch_demo/inputs` (20 generated `.mrd` files)
- `examples/mrd_batch_demo/png_outputs` (reconstructed `.png` files)

### End-to-end batch gating test (20 CSV + MRD pairs -> DICOM)

To generate 20 synthetic cases (each with cardiogram CSV + MRD scan), run cardiac gating, and write DICOM outputs per case:

```bash
python examples/run_mrd_csv_batch_gating.py --count 20
```

To auto-open one generated DICOM in the built-in viewer after the run:

```bash
python examples/run_mrd_csv_batch_gating.py --count 20 --open-dicom --open-case-index 0
```

To auto-open one DICOM from every passing case:

```bash
python examples/run_mrd_csv_batch_gating.py --count 20 --open-all-dicoms
```

To open only the first 5 passing cases:

```bash
python examples/run_mrd_csv_batch_gating.py --count 20 --open-all-dicoms --open-passed-limit 5
```

Outputs for each case are stored under:
- `examples/mrd_csv_batch_runs/case_XX/scan.mrd`
- `examples/mrd_csv_batch_runs/case_XX/cardiogram.csv`
- `examples/mrd_csv_batch_runs/case_XX/gated_dicom/*.dcm`

### View generated DICOM files

Use the built-in viewer command:

```bash
python main.py view-dicom "examples/mrd_csv_batch_runs/case_00/gated_dicom/frame_0000.dcm"
```

### One-command demo (everything)

Need the full story (baseline sample, multi-scenario runs, visuals, and tests) without juggling separate scripts? Run:

```bash
python examples/quick_demo.py
```

Add `--skip-visuals` or `--skip-tests` if you only care about the datasets themselves. The script refreshes:

- `output/sample_filtered` for the baseline walkthrough
- `examples/demo_runs/<scenario>` folders containing raw inputs, filtered frames, and visualization artifacts

Extras by default:
- Generates `demo_report.html`, an HTML dashboard that links to every visual (cardiogram overlay, accepted/rejected PNGs) and records the pytest validation result.
- Prints the same metrics you narrate live (accepted/rejected frames, stable cycles, alignment offset).

Helpful switches:
- `--lightning`: baseline-only path tailored for live demos under four minutes (skips scenarios, visuals, and pytest by default).
- `--scenario set_a_stable` (repeat for multiple): refresh only the named scenario(s) when you want a tighter story.
- `--skip-scenarios`: baseline plus CLI summary only—use after you have pre-generated scenario folders earlier in the day.
- `--skip-report`: turn off HTML report generation if you prefer console-only output.
- `--auto-open`: launch the HTML report and each visuals folder as soon as the run finishes (useful when presenting on a single screen).

### Five-interaction narrated demo

Need to walk through each subsystem stage live with exactly five terminal commands? Use the step runner, which auto-opens visuals for every milestone:

1. Inputs ingested & previewed
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py ingest
  ```
2. Timeline alignment proof
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py alignment
  ```
3. Cycle + steady-state detection
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py cycles
  ```
4. Frame filtering verdict (reuses visualization bundle)
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py filter
  ```
5. Thirty-scenario validation sweep
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py validation
  ```

Each command writes assets under `demo_steps/step_<n>_*` and immediately opens the relevant PNGs/summary files unless you pass `--no-open`. The validation step generates noise-heavy, missing-input, and corrupt-DICOM cases so you can narrate expected vs. observed behavior.

### Sample data

To create a small synthetic dataset that exercises the entire pipeline end-to-end:

```bash
python examples/create_sample_inputs.py
python main.py examples/data/cardiogram_sample.csv examples/data/dicom_sample --output output/sample_filtered
```

The generator script writes a cardiogram CSV with 5 ms sampling cadence and a DICOM folder containing 12 frames tagged with `TriggerTime` values that line up with the stable cardiac phases expected by the tests.

### Multi-scenario demo

If you need to showcase multiple runs (different cardiograms + DICOM inputs) and capture the resulting filtered series, use the demo harness:

```bash
python examples/demo_pipeline.py --root demo_runs
```

This produces two distinct scenarios under `demo_runs/`:

- `set_a_stable`: near-regular rhythm, moderate MRI cadence.
- `set_b_variable`: faster rhythm with denser MRI sampling.

Each scenario folder includes `cardiogram.csv`, the raw `dicom_series/`, and the resulting `filtered_series/`. Use any file browser, `Get-ChildItem demo_runs\set_a_stable\dicom_series`, or `pydicom` to inspect timestamps and verify the frames that survive the filter. Re-run the script with different `--root` values to capture multiple demonstrations side-by-side.

### Visualization toolkit

To capture polished figures that overlay the cardiogram with accepted vs rejected frames and export PNG previews of representative DICOM slices:

```bash
python examples/visualize_run.py \
  demo_runs/set_a_stable/cardiogram.csv \
  demo_runs/set_a_stable/dicom_series \
  demo_runs/set_a_stable/filtered_series \
  --output demo_runs/set_a_stable/visuals
```

The script produces:
- `cardiogram_frames.png`: ECG trace plus vertical lines for every MRI frame, color-coded by acceptance.
- `accepted_frame_*.png` / `rejected_frame_*.png`: quick-look tiles you can drop into slides.
- `summary.txt`: timestamp table to narrate the decision process.

Run it for each scenario to build a slide deck showing “input cardiogram + input DICOM stack → filtered DICOM set” with supporting visuals.

## Tests

```bash
pytest
```

## Project Layout

```
scan_filter/
  config.py
  logging_config.py
  core/
  io/
  utils/
config/
  default.yaml
main.py
README.md
```
