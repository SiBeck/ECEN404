# Scan Filter: 5-Minute Demo Guide (Logic + Tests)

Goal: spend the first ~3 minutes walking through each Scan Filter stage *with its source file*, then spend the final ~2 minutes proving the design with the 100-case validation sweep and visualization tooling. Run everything from `C:\Users\harin\OneDrive\Desktop\ScanEditor`.

**Visual hook (start here):** Open `demo_steps/latest_validation/step_5_validation/case_42/viz/cardiogram_frames.png` alongside `demo_steps/latest_validation/step_5_validation/case_42/viz/accepted_frame_0.png`. The overlay shows cardiogram vs. MRI frame timestamps with accepted slices highlighted, while the PNG preview gives the audience a literal MRI image captured in the quiet phase. Reference these two images as the “destination” before diving into the code stages below.

## Stage Walkthrough (≈3 minutes total)

### 1. Synchronizer – Timeline Alignment (≈45 sec)
- **Explain:** “The MRI scanner and the cardiogram recorder start at slightly different times. Our synchronizer grabs the first MRI trigger, finds the closest cardiogram sample, and shifts every MRI timestamp so both streams live on one clock. From here on, cardiogram peaks and MRI frames line up perfectly.”
- **Code:** Open `scan_filter/core/aligner.py` (class `TimeAligner.align`). Highlight how it finds the nearest cardiogram sample to the first MRI trigger and shifts the entire series.
- **Command to mention (no need to run live):** alignment step inside the pipeline is triggered by `ScanFilterPipeline.run` in `scan_filter/core/pipeline.py`.
- **Visual:** `demo_steps/four_step_demo/step_2_alignment/step2_alignment_overview.png` (raw vs aligned frame markers plus residual histograms).

### 2. Cycle Detector – Stable Beat Selection (≈60 sec)
- **Explain:** “With the clocks aligned, we smooth the cardiogram, pick out each R-peak, and treat the gap between peaks as a cardiac cycle. We compare every RR interval to the typical heart rate; if it stays inside a tight tolerance we call that beat stable. Anything irregular—PVCs, arrhythmia bursts—gets labeled unstable and is ignored later.”
- **Code:** `scan_filter/core/cycle_detection.py` (`CycleDetector.detect`, `_find_peaks`, `_build_cycles`). Walk through how RR intervals are compared to the median ± tolerance.
- **Visual aid:** reference `demo_steps/latest_validation/step_5_validation/step5_validation_timeline.png` to show how stable/unstable outcomes differ across scenarios.
- **Visual:** `demo_steps/four_step_demo/step_3_cycles/step3_cycles_overview.png` (cycle stability bars + scatter) for a zoomed-in explanation before showing the 100-case timeline.

### 3. Stable-Phase Gate – Filter Engine (≈45 sec)
- **Explain:** “Inside every stable beat we carve out the quiet portion of the heartbeat—think mid-diastole. Two knobs decide where the window starts and how wide it is. Then we simply walk the aligned MRI frames: if a frame lands inside any stable window we keep it; otherwise it gets rejected.”
- **Code:** `scan_filter/core/filter_engine.py` (`FrameFilter.filter_frames`). Point out how it iterates aligned frame timestamps and returns accepted/rejected lists.
- **Optional tie-in:** mention `examples/visualize_run.py` for rendering accepted vs rejected overlays.
- **Visual:** `demo_steps/four_step_demo/step_4_filter/step4_filter_overview.png` (acceptance-rate bars + accepted vs rejected counts) and/or a per-case overlay from `examples/visualize_run.py` such as `demo_steps/latest_validation/step_5_validation/case_42/viz/cardiogram_frames.png`.

### 4. Orchestration – Putting It All Together (≈30 sec)
- **Explain:** “`ScanFilterPipeline` wires the whole chain together. It loads the cardiogram and DICOMs, runs the synchronizer, feeds the aligned signal into the cycle detector, and finally pushes accepted frames through the filter engine. Every scenario in our demo simply calls this one function.”
- **Code:** `scan_filter/core/pipeline.py` (`run`). Show how it instantiates `TimeAligner`, `CycleDetector`, `FrameFilter`, then writes accepted frames.
- **Visual:** `demo_steps/four_step_demo/step_1_ingest/step1_ingest_overview.png` (cardiogram intake + frame inventory) doubles as a system diagram while you explain the top-level flow.

### 5. Scenario Builder – How We Generate Inputs (≈30 sec)
- **Explain:** “To stress the system we synthesize 100 different patients: pediatrics with fast rhythms, obese patients with low SNR, arrhythmias with huge jitter, plus explicit fault injections with missing cardiograms and corrupt DICOMs. Each profile sets beats, RR interval, jitter, noise, and resolution, so we touch all the edge cases before we ever show real data.”
- **Code:** `examples/five_step_demo.py` (`build_validation_scenarios`). Mention the population profiles and how `run_validation_batch` loops through them with `ScanFilterPipeline`.
- **Visual:** `demo_steps/latest_validation/step_5_validation/step5_validation_summary.png` (status distribution) or `step5_validation_by_mode.png` to illustrate the breadth of populations before running the sweep live.

## Test & Evidence Sweep (≈2 minutes total)

### A. Quick 20-case warm-up (≈20 sec)
```powershell
.venv\Scripts\python.exe examples\four_step_demo.py --no-open
```
- **Show:** Point to `demo_steps\four_step_demo` artifacts if someone wants the legacy ingest→filter walkthrough; otherwise note that this command is available for a shorter dry run.

### B. Run the 100-case validation once (≈45 sec)
```powershell
.venv\Scripts\python.exe examples\five_step_demo.py validation --no-open --output-root demo_steps\latest_validation
```
- **Show:** `demo_steps\latest_validation\step_5_validation\step5_validation_summary.png` and `step5_validation_timeline.png` for overall disposition.
- **Narrate:** 94 nominal/outlier PASS/PASS* runs + 6 EXPECTED FAIL fault injections.

### C. Surface aggregate stats (≈30 sec)
```powershell
.venv\Scripts\python.exe -c "import pandas as pd; df=pd.read_csv(r'demo_steps\latest_validation\step_5_validation\step5_validation_results.csv'); print('Status counts:\n', df['status'].value_counts()); print('\nAccepted-frame distribution (min/avg/max):', df['accepted_frames'].min(), round(df['accepted_frames'].mean(), 2), df['accepted_frames'].max()); print('\nModes seen:', df['mode'].value_counts())"
```
- **Show:** Console output + the CSV for anyone who wants to slice further.

### D. Spotlight an individual case (≈30 sec)
```powershell
.venv\Scripts\python.exe examples\visualize_run.py demo_steps\latest_validation\step_5_validation\case_42\cardiogram.csv demo_steps\latest_validation\step_5_validation\case_42\dicom_series demo_steps\latest_validation\step_5_validation\case_42\filtered --output demo_steps\latest_validation\step_5_validation\case_42\viz
```
- **Show:** `cardiogram_frames.png`, accepted/rejected thumbnails, and `summary.txt` inside `case_42\viz` (swap the case ID for pediatric vs obese vs arrhythmic examples).

### E. Close with contractual requirements (≈15 sec)
```powershell
Get-Content demo_steps\latest_validation\step_5_validation\step5_validation_requirements.txt
```
- **Show:** Read the PASS verdicts for REQ-1…REQ-5; point to `examples/five_step_demo.py::evaluate_validation_requirements` for the logic.

That’s the entire five-minute arc: introduce each subsystem alongside its code, then prove it works using the full 100-case sweep and per-case evidence artifacts.

## Bonus: 20-Case End-to-End Demo Command
If you need the original 20-case walkthrough (ingest → filter) as a quick sanity check, run:

```powershell
.venv\Scripts\python.exe examples\four_step_demo.py --no-open
```

This synthesizes the classic 20 scenarios, regenerates every step’s CSV/PNG under `demo_steps\four_step_demo`, and drops per-case filtered frames inside `demo_steps\four_step_demo\step_4_filter\filtered_frames`. Use this when you want a shorter run that still proves the start-to-finish pipeline.
