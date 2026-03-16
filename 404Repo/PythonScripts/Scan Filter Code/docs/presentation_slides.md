# Scan Filter Pitch Deck

> Export tip: This Markdown is structured slide-by-slide. Paste into PowerPoint/Keynote or use a Markdown-to-slides tool (Marp, Pandoc, reveal.js). Each top-level heading is a new slide.

## 1. Title Slide
- **Project:** Scan Filter – Cardiac-aware MRI frame selection
- **Author:** _<Your Name>_
- **Date:** November 17, 2025
- **Tagline:** Align ECG + MRI timelines to harvest only stable cardiac frames

## 2. Problem Statement
- MRI cine series often contain frames captured during unstable cardiac phases
- Cardiogram (ECG) data is available but not automatically synchronized with images
- Radiologists spend time discarding jittery frames manually
- Need: automated subsystem that understands cardiac stability and filters DICOM stacks

## 3. Inputs & Outputs
- **Inputs**
  - Cleaned cardiogram CSV (`timestamp_ms`, `signal`)
  - DICOM MRI series with per-frame `TriggerTime`
- **Outputs**
  - Filtered DICOM folder containing only frames from quiescent phases
  - Visualization bundle (plots + sample frame PNGs)
  - Metrics: accepted/rejected counts, stable cycles, alignment offset

## 4. System Architecture
```
Cardiogram CSV → csv_reader → CardioSeries ┐
                                           │
DICOM series → dicom_reader → MRIFrameSeries ├─ TimeAligner (global offset)
                                           │
            config/default.yaml             ├─ CycleDetector (adaptive windowing)
                                           │
                                           └─ FrameFilter (stable-phase gating) → DICOM writer + visuals
```
- Configurable stages, structured logging, clean separation of IO/core/visualization modules

## 5. Algorithm – Time Alignment
- Estimate sample period and compute global offset `Δt = argmin |cardio_ts - first_frame_ts|`
- Apply `Δt` to every frame (`frame.timestamp += Δt`)
- Flag if |Δt| > `max_offset_ms` (configurable safety guard)
- Ensures ECG peaks line up with MRI trigger times before cycle analysis

## 6. Algorithm – Cycle Detection
1. Convert `window_ms` into sample count `N = window_ms / T_s`
2. Baseline `b(t) = moving_average(signal, N)`
3. Noise `σ(t) = moving_std(signal, N)`
4. Adaptive threshold: detect peak when `signal(t) > b(t) + κ·σ(t)` and it is a local maximum separated by ≥ `min_rr_ms`
5. RR interval validation: keep cycles where `min_rr_ms ≤ rr ≤ max_rr_ms`
6. Stability: `|rr - median(rr_valid)| ≤ median · (stable_rr_variation_pct / 100)`

## 7. Algorithm – Frame Filtering
- For each stable cycle:
  - Duration `D = end_ms - start_ms`
  - Stable window starts at `start_ms + stable_phase_offset·D`
  - Accept frames within `[phase_start, phase_start + stable_phase_fraction·D]`
- Summaries per run: accepted vs rejected frames, stable cycle count, alignment offset

## 8. Implementation Highlights
- Dataclasses for all models (`CardioSeries`, `MRIFrame`, `CardiacCycle`)
- YAML-driven configuration; swapping parameters does not require code changes
- Optional dependency handling for `pydicom`; clean logging via `logging_config`
- Utilities: moving statistics, adaptive window, DICOM reader/writer, visualization script

## 9. Demo Workflow (5 commands)
1. **Ingest inputs** – cardiogram + raw DICOM preview
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py ingest
  ```
2. **Align timelines** – offset visual pops up automatically
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py alignment
  ```
3. **Detect cycles** – ECG plot with stable windows
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py cycles
  ```
4. **Filter frames** – accepted vs rejected PNGs + summary text
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py filter
  ```
5. **Validate 30 scenarios** – mix of nominal, outlier, missing/ corrupt inputs
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py validation
  ```
Each command generates assets under `demo_steps/step_<n>_*` and opens them automatically (use `--no-open` only if you want to keep the screen static).

## 10. Visual Evidence
- `cardiogram_frames.png`: ECG overlay with accepted (green) vs rejected (orange) frames
- `accepted_frame_0.png` vs `rejected_frame_0.png`: pixel-space difference
- `summary.txt`: timestamp + decision table
- Show Explorer comparison: `dicom_series/` vs `filtered_series/`

## 11. Testing & Validation
- Unit tests (`pytest`):
  - `test_alignment.py`: verifies global offset application
  - `test_cycle_detection.py`: synthetic ECG ensures stable cycle detection
  - `test_filter_engine.py`: ensures only stable-phase frames survive
  ```powershell
  .venv\Scripts\python.exe -m pytest
  ```
- Metrics logged per run: accepted/rejected counts, stable cycles, offset

## 12. Measurable Results
| Scenario          | Frames In | Frames Out | Stable Cycles | Notes |
|-------------------|-----------|-----------|---------------|-------|
| `set_a_stable`    | 12        | 5         | 15            | Regular rhythm, moderate cadence |
| `set_b_variable`  | 18        | 8         | 20            | Faster rhythm, dense MRI sampling |
| Sample dataset    | 12        | 6         | 17            | Baseline reference |

## 13. Technical Merit
- Adaptive statistical signal processing (moving averages, std dev, RR variance thresholds)
- Deterministic pipeline with dependency injection and YAML-driven parameters
- Automatic visualization + timestamp audit trail
- Extensible design (e.g., plug in advanced alignment/arrhythmia detection)

## 14. Future Enhancements
- Dynamic drift compensation (per-segment alignment)
- Advanced peak detection (Pan–Tompkins, wavelets)
- Motion-based DICOM scoring to corroborate ECG decisions
- Performance profiling for long cine series

## 15. Closing & Q&A
- Recap: Align → Detect → Filter → Visualize → Validate
- Invite questions on algorithm choices, parameter tuning, or integration with larger imaging pipelines
- Offer live rerun or deep dive into code/tests on request
