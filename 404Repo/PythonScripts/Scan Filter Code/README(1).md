# Scan Filter Subsystem

The Scan Filter aligns cleaned cardiogram CSV data with a DICOM MRI series, detects stable cardiac cycles, and exports a filtered DICOM series containing only frames acquired during stable cardiac phases.

## Features
- CSV-driven cardiogram ingestion with configurable column names
- Time alignment between ECG samples and MRI frames
- Adaptive windowing cycle detection that tolerates noisy beats
- Stable phase selection and DICOM frame filtering
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
