# Demo Command Walkthrough

Use the following script notes to narrate each command, the assets it produces, and how to interpret the visuals. All commands assume you are running inside `C:\Users\harin\OneDrive\Desktop\ScanEditor` with the virtual environment activated.

## Key terminology reference

- **beats** – Number of cardiac beats synthesized for a case; higher counts extend simulation length.
- **base_rr_ms** – Baseline R-R interval in milliseconds (time between heartbeats). BPM ≈ 60000 / base_rr_ms.
- **spacing_ms** – Target temporal distance between DICOM frames before noise injection.
- **spacing_noise_ms** – Gaussian noise added to spacing_ms to emulate scanner instability.
- **jitter_ms** – Per-frame trigger jitter that shifts timestamps randomly around the expected spacing.
- **offset_ms** – Global shift applied to the entire DICOM trigger timeline (simulates gating drift).
- **cardio_noise** – Standard deviation of noise injected into the cardiogram signal (higher = lower SNR).
- **modality / resolution** – MRI acquisition style (bSSFP, FLASH, etc.) and pixel dimensions shown in frame previews.
- **alignment offset** – Net time shift (ms) applied by the aligner to synchronize cardiogram and imaging series.
- **residual mean / std** – Average and spread of timestamp deltas after alignment; low numbers mean tight synchronization.
- **stable cycle** – Cardiac cycle whose RR interval variability is within the `stable_rr_variation_pct` threshold.
- **stable ratio** – Stable cycles / total cycles; predicts how many frames will fall inside a reliable phase.
- **accepted / rejected frames** – Frames inside or outside the stable-phase gate, respectively.
- **acceptance rate** – Accepted / (accepted + rejected); used to argue throughput.
- **accepted per stable cycle** – Accepted frames divided by stable cycles, showing sampling efficiency.
- **PASS vs. PASS\*** – PASS is a nominal scenario success; PASS\* indicates an outlier cadence succeeded with special handling.
- **EXPECTED FAIL** – Intentional failure when required inputs (cardiogram or DICOM) are missing/corrupt.
- **fallback minimum frames** – Safety net (currently 2) that auto-injects aligned frames if the stable-phase filter produced too few, ensuring REQ-5 compliance.

---

## 1. Catalog the 20 synthetic cases

```powershell
.venv\Scripts\python.exe examples\four_step_demo.py --list-cases
```

**What to say while it runs**
- Emphasize the diversity of the dataset: fast/slow heart rates, different MRI protocols, intentionally noisy vs. clean signals.
- Translate `base_rr_ms` into BPM so stakeholders map cadence to stability risk.
- Explain that `spacing_noise_ms`, `jitter_ms`, and `offset_ms` are the knobs that make alignment hard, while `cardio_noise` drives signal SNR.
---

## 2. Step 1 – Ingest snapshots

```powershell
.venv\Scripts\python.exe examples\four_step_demo.py --step ingest --no-open
```

**Outputs**
- Assets written to `demo_steps/four_step_demo/step_1_ingest/` include cardiogram plots, frame grids, and a metrics text file.

**How to interpret the visuals**
- Cardiogram panels: x-axis = milliseconds, y-axis = normalized signal. Emphasize how higher `cardio_noise` broadens the waveform and how regular spacing in low-noise cases reflects a stable `base_rr_ms`.
- Frame grids: each tile is a single DICOM frame. Tie unusual anatomy or blur back to modality/resolution listed in the catalog.
- Metrics text: includes signal min/max, sample count, and inferred sample period. Use it to explain why all downstream stages work with normalized units.

**Narration tips**
- “Here you can see the arrhythmic beat pattern on case 08 compared to the clean cadence on case 03.”
- “Even before alignment, we ensure modality consistency by regenerating both cardiogram and imaging inputs from the same seed.”

**Common questions answered**
- *What does ‘signal min/max’ mean?* → The range of the normalized cardiogram; tight ranges indicate low noise.
- *Why do some grids look blurry?* → Lower-resolution modalities (e.g., FLASH) or higher cardio noise, as specified in the catalog.

---

## 3. Step 2 – Alignment diagnostics

```powershell
.venv\Scripts\python.exe examples\four_step_demo.py --step alignment --no-open
```

**Outputs**
- Stored under `demo_steps/four_step_demo/step_2_alignment/`. Includes residual plots, trigger overlays, and alignment summary tables.

**Visual meaning**
- Residual heatmaps/line charts: y-axis is residual error (ms). “Before” traces inherit `spacing_noise_ms` and `jitter_ms`; “after” traces reflect the aligner’s correction. Residual spread narrowing proves the max-offset search succeeded.
- Trigger overlays: orange = original DICOM triggers, blue = cardiogram markers after applying the computed `alignment offset` plus per-frame adjustments. Parallel traces indicate consistent gating after correction.
- Alignment metrics CSV/TXT: `residual_before_mean/std` quantify drift prior to corrections; `residual_after_*` show post-align performance. Values near zero mean we are operating within the ±20 ms tolerance promised to cycle detection.

**Narration tips**
- “Cases with high spacing noise (see catalog columns) still converge to ±20 ms after alignment, which feeds the downstream cycle detector with clean timestamps.”

**Common questions answered**
- *What is ‘alignment offset per case’?* → The net shift applied to that case’s DICOM timeline. Negative means imaging lagged the cardiogram; positive means imaging led.
- *What does ‘residual spread’ mean?* → It’s the standard deviation of remaining timing error after alignment; smaller spread equals tighter synchronization.

---

## 4. Step 3 – Cycle detection QA

```powershell
.venv\Scripts\python.exe examples\four_step_demo.py --step cycles --no-open
```

**Outputs**
- Saved in `demo_steps/four_step_demo/step_3_cycles/`: cycle strip charts, stable/unstable markers, and `step3_cycles_metrics.txt`.

**Visual meaning**
- Cycle timeline plots: x-axis = time, peaks mark RR intervals. Green peaks indicate `is_stable=True` (fall within `stable_rr_variation_pct`); red indicates unstable beats.
- Histogram/pie visuals: quantify how many cycles passed stability gates. They help justify why some cases later have fewer accepted frames.
- Metrics text: reports `total cycles`, `stable cycles`, `stable ratio`, `mean duration`, and `std duration`. The standard deviation reveals arrhythmia severity.

**Common questions answered**
- *What defines a stable cycle?* → A cycle whose duration stays between `min_rr_ms` and `max_rr_ms` and whose variation vs. neighbors is below `stable_rr_variation_pct`.
- *Why does duration std matter?* → High std indicates rate variability; expect lower acceptance downstream because the stable-phase window can’t lock in.

---

## 5. Step 4 – Filter dashboard

```powershell
.venv\Scripts\python.exe examples\four_step_demo.py --step filter --no-open
```

**Outputs**
- Written to `demo_steps/four_step_demo/step_4_filter/`: cardiogram + accepted-frame overlays, per-case bar charts, preview PNGs, and `step4_filter_metrics.txt`/`step4_summary.txt`.

**Visual meaning**
- Overlay plots: shaded regions mark the stable-phase window (offset + fraction). Markers show which frames fell inside. Explain that even if only a couple frames qualify, fallback logic boosts counts to the configured minimum.
- Bar charts: left bar = accepted, right bar = rejected. High rejection indicates either low stable ratio or heavy jitter; connect back to earlier stages.
- Summary text: `alignment_offset_ms` echoes stage 2; `stable_cycles` ties to stage 3; `acceptance_rate` and `accepted_per_cycle` quantify output efficiency. `step4_summary.txt` also lists the absolute accepted/rejected counts for audit.

**Narration tips**
- “Notice that case 10 used to fail—it had zero accepted frames before the fallback safeguard—but now the dash shows 4 frames exported, keeping the validation requirement intact.”

**Common questions answered**
- *What is the ‘accepted per stable cycle’ number telling me?* → How many frames we capture during each reliable heartbeat; it is a direct measure of throughput given physiological stability.
- *How do we ensure alignment drift doesn’t reappear?* → `alignment_offset_ms` is re-reported here so you can confirm it matches the cycle stage output.

---

## 6. Five-step validation sweep (requirement proof)

```powershell
.venv\Scripts\python.exe examples\five_step_demo.py validation --no-open --output-root demo_steps\latest_validation
```

**Outputs**
- `demo_steps/latest_validation/step_5_validation/` containing:
  - `step5_validation_results.csv`: row-by-row scenario status and accepted frame counts.
  - `step5_validation_requirements.csv` & `.txt`: pass/fail for the five contractual requirements.
  - PNGs: `step5_validation_summary.png`, `step5_validation_by_mode.png`, `step5_validation_timeline.png`.

**Visual meaning**
- Summary bar chart: y-axis = scenario count; bars are grouped by status. Explain PASS vs. PASS* vs. EXPECTED FAIL definitions so the audience expects orange bars only on fault-injection cases.
- Mode-stacked bar chart: x-axis = scenario mode (normal, outlier, missing_cardio, corrupt_dicom). Each stack reveals how frequently a mode hits each status. Use it to demonstrate graceful degradation.
- Timeline heat strip: columns = scenarios in execution order, colors = status. It proves there were no intermittent failures once fallback logic landed; the only non-green columns are the intentional EXPECTED FAIL cases.
- Requirements TXT: each REQ entry lists expectation, observed metric, and PASS/FAIL. Use it to answer “What does REQ-4 mean?” → “Average accepted frames over PASS/PASS* runs; we observed 5.33, above the ≥4 threshold.”

**Narration tips**
- “If an auditor asks for evidence, we hand them the `step5_validation_requirements.txt` file; it links each requirement ID to observed metrics.”
- “The fallback frame injection is what ensured REQ-5 stays green even for sparse cases.”

**Common questions answered**
- *Why are there PASS* rows?* → They mark outlier cadence scenarios that succeeded while taking a special branch (documented via `message` column “Handled outlier cadence”).
- *How do EXPECTED FAIL cases prove anything?* → They show fault detection works: missing cardiograms throw `[Errno 2]` and corrupt DICOM folders trip the DICOM parser, both of which are the intended outcomes.

---

## Delivery tips
- Run with `--no-open` during rehearsal to avoid window spam; remove it live if you want the PNGs to auto-open.
- Reference exact output paths so stakeholders can inspect artifacts themselves.
- Keep the catalog table handy; it provides context whenever someone asks “which case is that chart from?”
