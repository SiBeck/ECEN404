# Scan Filter Demo – Logic Deep Dive

Use this note when you want to explain, line by line, what the demo code is doing. Each section walks through the core module(s), the key functions/classes, and the internal logic traces that matter for the five-minute presentation.

## 1. Synchronizer (Alignment) – `scan_filter/core/aligner.py`

1. **Inputs:** `CardioSeries` (timestamps + signal array) and `MRIFrameSeries` (list of frames with timestamps/pixels).
2. **`TimeAligner.align()` steps:**
   - Pull the numpy arrays: `cardio_ts`, `frame_ts`.
   - Guard clause: raise if either is empty.
   - Compute `first_frame_time = frame_ts[0]`.
   - Find the cardiogram sample closest to that first frame using `np.argmin(np.abs(cardio_ts - first_frame_time))`.
   - Offset = `cardio_ts[idx] - first_frame_time` (can be positive or negative).
   - Apply the offset by calling `frames.with_offset(offset)` so every DICOM frame timestamp is shifted.
   - Return `TimeAlignmentResult(aligned_series, offset)` so downstream code knows the applied correction.
3. **Why it matters:** Using a global offset keeps the math cheap and stable for synthetic data; the max offset guard still lets you detect runaway drift.

## 2. Cycle Detector – `scan_filter/core/cycle_detection.py`

`CycleDetector.detect(cardio)` returns a `CycleSummary` with raw cycles and filtered stable cycles. Internally:

1. **Pre-smoothing:** Rolling mean + rolling std over the signal to form an adaptive threshold: `baseline + (std * min_peak_prominence)`.
2. **Peak picking (`_find_peaks`):**
   - Iterate samples, track when the signal crosses the threshold, and enforce `min_rr_ms` converted into sample counts between peaks.
   - When two peaks are too close, keep the higher amplitude.
3. **Cycle construction (`_build_cycles`):**
   - Each consecutive peak pair becomes a cycle with start, peak, end, and `rr_ms`.
   - Adds small guard bands before/after the peaks so MRI timestamps have wiggle room.
4. **Stability grading:**
   - Take all `rr_ms` that fall between `min_rr_ms` and `max_rr_ms`.
   - Compute the median RR and allow ±(`stable_rr_variation_pct`/100 * median).
   - Mark cycles inside that tolerance as `is_stable=True`; others stay unstable.
5. **Output:**
   - `summary.cycles` contains every cycle.
   - `summary.stable_cycles` only includes the ones flagged stable.

## 3. Filter Engine – `scan_filter/core/filter_engine.py`

`FrameFilter.filter_frames(aligned_frames, stable_cycles)` decides which MRI frames survive.

1. **Stable window math:** `stable_phase_offset` (fraction of cycle duration) plus `stable_phase_fraction` define `[window_start, window_end]` for every stable cycle.
2. **Iterating frames:**
   - For each frame timestamp, loop through stable windows to see if the timestamp lies within any of them.
   - Accepted frames are stored with their original pixel data and metadata.
   - Rejected frames only keep metadata for reporting.
3. **Outputs:** `FilterArtifacts(accepted_frames, rejected_frames, stable_cycles)` so callers can inspect counts, percentages, or write images.

## 4. Pipeline Orchestration – `scan_filter/core/pipeline.py`

`ScanFilterPipeline.run(cardiogram_path, dicom_dir, filtered_dir)` wires everything together:

1. Load cardiogram CSV into `CardioSeries` (via `CardiogramCsvReader`).
2. Load DICOM folder into `MRIFrameSeries` (via `DicomSeriesReader`).
3. Instantiate:
   - `TimeAligner(config.alignment.max_offset_ms)`
   - `CycleDetector` with window, prominence, RR limits, stable tolerance
   - `FrameFilter` with stable-phase fraction/offset
4. Call `aligner.align(cardio, frames)` → get `aligned_series` + offset.
5. Call `detector.detect(cardio)` → get `CycleSummary`.
6. Feed `aligned_series` + `summary.stable_cycles` into `frame_filter.filter_frames` → accepted/rejected lists.
7. Write accepted frames to `filtered_dir` via `DicomSeriesWriter`.
8. Return a dataclass with counts (accepted, rejected, stable cycles) plus metrics used in reports.

## 5. Scenario Builder – `examples/five_step_demo.py`

`build_validation_scenarios()` produces 100 `ValidationScenario` entries:

1. Defines 12 population archetypes (pediatric, athlete, arrhythmic, etc.) each with ranges for beats, RR, noise, jitter, spacing, offsets, DICOM shapes.
2. Uses a deterministic RNG (`random.Random(20241130)`) so every run emits the same scenario list.
3. After the 100 nominal/outlier cases, appends 3 `missing_cardio` and 3 `corrupt_dicom` scenarios for EXPECTED FAIL checks.
4. `run_validation_batch()` loops through scenarios:
   - Generates cardiogram via `generate_cardiogram` + optional noise.
   - Generates DICOM frames via `generate_dicom_series` unless it’s a corrupt scenario.
   - Calls `pipeline.run()`; captures accepted frame counts and PASS/FAIL status.
   - Writes `step5_validation_results.csv`, `step5_validation_summary.png`, `step5_validation_timeline.png`, etc.

## 6. Visualization Helper – `examples/visualize_run.py`

Useful when spotlighting a single case:

1. Reads `cardiogram.csv` and DICOM directories.
2. Uses `load_frames()` to pair each DICOM file with its timestamp and acceptance flag.
3. `plot_cardiogram_with_frames()` draws the cardiogram trace with colored vertical lines for accepted vs rejected frames.
4. `export_frame_previews()` writes PNG thumbnails of up to N accepted/rejected frames for quick inspection.
5. `write_summary()` logs every frame name + timestamp + status to a text file.

## 7. 20-Case Legacy Demo – `examples/four_step_demo.py`

Still handy for a shorter end-to-end example:

1. `build_case_catalog()` lists 20 curated scenarios.
2. `prepare_cases()` synthesizes cardiograms and DICOMs on disk.
3. `run_four_step_demo()` runs ingest → alignment → cycles → filter, recording metrics at each stage.
4. `render_*_outputs()` functions produce CSV/PNG artifacts for every step, mirroring the structure in `demo_steps/four_step_demo/`.

---

Keep this deep-dive doc open alongside `five_min_demo.md`: the demo script tells you *what to say/execute*, and this file reminds you *how each step works internally* so you can answer detailed questions on the spot.
