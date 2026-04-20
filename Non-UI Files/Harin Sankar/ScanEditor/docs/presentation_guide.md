# Scan Filter Presentation Guide

Use this guide to walk a reviewer through the Scan Filter subsystem: why it exists, how it works, how it was validated, and how to show the results live.

## 1. Purpose & Overview
- **Goal:** Align cleaned cardiograms with MRI DICOM series and keep only the frames captured during stable cardiac phases.
- **Inputs:**
  - Cardiogram CSV with millisecond timestamps (`timestamp_ms`) and normalized signal amplitude (`signal`).
  - Folder of MRI DICOM frames that contain per-frame timestamps (`TriggerTime`).
- **Outputs:** A new DICOM folder (`filtered_series/`) plus plots and summaries showing which frames were accepted or rejected.

## 2. Architecture & Data Flow
```
cardiogram CSV ─┐
                │   csv_reader → CardioSeries
                │
                ├─> TimeAligner ────────────────────────────┐
                │                                            │
DICOM series ───┘   dicom_reader → MRIFrameSeries            │
                                                         CycleDetector (adaptive windowing)
                                                         │
                                                         FrameFilter (stable-phase gating)
                                                         │
                                                         dicom_writer → filtered frames
```
- `config/default.yaml` controls each stage (offset bounds, peak detection window, stable-phase fraction, etc.).
- Logging (`scan_filter/logging_config.py`) reports every stage for reproducibility.

## 3. Algorithms & Math
### 3.1 Time Alignment
- For each dataset, the aligner computes a global offset `Δt = argmin |cardio_ts - frame_ts₀|` within `max_offset_ms`.
- The MRI series is time-shifted by `Δt`, enabling direct comparison between ECG peaks and frame timestamps.

### 3.2 Cycle Detection
1. Estimate sample period `T_s` from ECG timestamps. Convert configured `window_ms` into samples.
2. Compute moving baseline `b(t)` and moving noise `σ(t)`:
   - `b(t) = avg(window(signal))`
   - `σ(t) = sqrt(avg(window((signal - b(t))²)))`
3. Adaptive detection: treat `signal(t)` as a peak if `signal(t) > b(t) + κ · σ(t)` and it is a local maximum separated by at least `min_rr_ms`.
4. RR intervals `rr_i = peak_{i+1} - peak_i` are compared against physiological bounds `[min_rr_ms, max_rr_ms]`.
5. Stability: compute `median(rr_valid)` and flag a cycle as stable if `|rr_i - median| ≤ median · (stable_rr_variation_pct / 100)`.

### 3.3 Frame Filtering
- Each stable cycle defines a phase window:
  - Duration `D = end_ms - start_ms`.
  - Stable phase starts at `start_ms + stable_phase_offset · D`.
  - Stable phase length `stable_phase_fraction · D`.
- A frame at timestamp `t_f` is accepted when `phase_start ≤ t_f ≤ min(end_ms, phase_start + fraction · D)`.
- Metrics captured per run: accepted count, rejected count, number of stable cycles, alignment offset.

## 4. Demo Commands (Copy/Paste)
1. **Regenerate baseline sample data**
   ```powershell
   .venv\Scripts\python.exe examples\create_sample_inputs.py
   ```
2. **Run end-to-end pipeline on the sample**
   ```powershell
   .venv\Scripts\python.exe main.py examples\data\cardiogram_sample.csv examples\data\dicom_sample --output output\sample_filtered
   ```
3. **Generate two richer scenarios simultaneously**
   ```powershell
   .venv\Scripts\python.exe examples\demo_pipeline.py --root examples\demo_runs
   ```
   - Produces `set_a_stable` (regular rhythm) and `set_b_variable` (faster rhythm) folders with `cardiogram.csv`, `dicom_series/`, `filtered_series/`.
4. **Create visuals for each scenario**
   ```powershell
   .venv\Scripts\python.exe examples\visualize_run.py ^
     examples\demo_runs\set_a_stable\cardiogram.csv ^
     examples\demo_runs\set_a_stable\dicom_series ^
     examples\demo_runs\set_a_stable\filtered_series ^
     --output examples\demo_runs\set_a_stable\visuals

   .venv\Scripts\python.exe examples\visualize_run.py ^
     examples\demo_runs\set_b_variable\cardiogram.csv ^
     examples\demo_runs\set_b_variable\dicom_series ^
     examples\demo_runs\set_b_variable\filtered_series ^
     --output examples\demo_runs\set_b_variable\visuals
   ```
   - Artifacts per scenario:
     - `cardiogram_frames.png`: ECG trace with accepted (green) vs rejected (orange) MRI frame timestamps.
     - `accepted_frame_*.png` / `rejected_frame_*.png`: DICOM previews for slides.
     - `summary.txt`: printable table of frame timestamps and decisions.

## 5. Testing & Validation
1. **Unit tests** (cover alignment math, cycle detection stability, frame filtering):
   ```powershell
   .venv\Scripts\python.exe -m pytest
   ```
   - `tests/test_alignment.py`: verifies offsets are applied correctly.
   - `tests/test_cycle_detection.py`: synthetic ECG ensures stable cycles are detected.
   - `tests/test_filter_engine.py`: confirms only stable-phase frames survive.
2. **Metrics to mention**
   - Alignment offset (ms) printed at end of `main.py` run.
   - Accepted vs rejected frame counts.
   - Number of stable cycles detected per scenario.
   - In `visuals/summary.txt`, list of every frame timestamp and its disposition.

## 6. Presentation Walkthrough
1. **Explain architecture** using Section 2 diagram.
2. **Describe algorithms** with Section 3 formulas; highlight adaptive thresholds and physiological gating.
3. **Show inputs**:
   - Display top rows of `cardiogram.csv`.
   - List DICOM filenames in `dicom_series/`.
4. **Run pipeline live** (Sections 4.1–4.3) and point to the logs showing each stage.
5. **Show outputs**:
   - Open `filtered_series/` in Explorer or a DICOM viewer.
   - Present `cardiogram_frames.png` and sample `accepted_frame_0.png` vs `rejected_frame_0.png`.
6. **Discuss metrics** using the auto-generated summary and the console output (`Accepted=X, Rejected=Y, Stable cycles=Z`).
7. **Validate with tests** by running pytest and showing the green report.

## 7. Tips for Highlighting Technical Merit
- Emphasize configurable parameters in `config/default.yaml` and how tuning them changes behaviour.
- Mention optional enhancements (dynamic drift handling, advanced detection) as future work.
- Bring up robustness: optional `pydicom` dependency handling, YAML-driven pipeline, and automated visual QA.
- Close with measurable results: “Scenario A retained 5/12 frames, scenario B retained 8/18 frames, both explained by the ECG overlay plots.”

Keep this guide open during the presentation—each section maps to a slide or demo segment.
