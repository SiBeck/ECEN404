# Scan Filter Demo Playbook

**Goal:** Run through the entire subsystem (inputs → processing → outputs → validation) in ~10 minutes.

---
## 0. One-Command Fast Track (recommended)
Use the quick orchestrator when you just need everything built without thinking about intermediate steps.
```powershell
.venv\Scripts\python.exe examples\quick_demo.py
```
- Generates fresh baseline inputs, runs the pipeline, creates all demo scenarios, exports visuals, runs `pytest`, **and writes `demo_report.html`** with links to every artifact.
- Add `--skip-visuals` or `--skip-tests` if you only need the data artifacts.
- Add `--scenario set_a_stable` (repeat flag for multiples) to restrict which scenario folders refresh.
- Add `--skip-scenarios` when you only care about the baseline walkthrough.
- Use `--lightning` to auto-enable baseline-only mode (skip scenarios, visuals, and tests) so you can execute a live demo in under 4 minutes.
- Change the report location with `--report docs/latest_demo.html`, or disable it via `--skip-report`.
- Pass `--auto-open` to automatically launch the HTML report plus every visuals folder the moment the run completes (perfect when you only have one monitor).
- Key outputs land in:
  - `output/sample_filtered` (baseline)
  - `examples/demo_runs/set_a_stable` and `set_b_variable` (each has inputs, filtered frames, and `visuals/`).
  - `demo_report.html`: a one-page showcase that embeds cardiogram overlays, accepted/rejected PNGs, links to summaries, and the pytest verdict.

### Lightning flow (<4 minutes on stage)
1. **Before the meeting (5–6 min, off-stage):**
  ```powershell
  .venv\Scripts\python.exe examples\quick_demo.py
  ```
  This pre-renders both scenarios plus visuals so you can reference them without waiting live. Keep `demo_report.html` open in a browser tab for instant visuals.
2. **During the demo (~3 min):**
  ```powershell
  .venv\Scripts\python.exe examples\quick_demo.py --lightning
  ```
  - Step 1–2 (~45 s): regenerates the baseline cardiogram/DICOM set and runs the pipeline (logs show accepted/rejected counts).
  - Step 3 (instant): skips heavy scenario regeneration, pointing you to the prebuilt folders from step 1.
  - Step 4 (instant): skips visuals & pytest to keep runtime tiny.
  - Talk track (~2 min): walk through `output/sample_filtered`, then flip to the `demo_report.html` tab to showcase the cardiogram overlay, accepted/rejected PNGs, and pytest badge that were rendered ahead of time.
3. **Optional variation:** Swap in a single scenario live by adding `--scenario set_a_stable` (still skips visuals/tests, but refreshes that dataset in ~60 s).

> Prefer the manual steps below when you want to narrate each operation live.

---
## 0.1 Five-command interactive walk-through
When you need a deterministic “one command → visuals pop → explain” rhythm, use the new step helper. Each invocation both performs the subsystem stage and opens the PNGs/summaries proving expected vs observed behavior.

1. **Ingest** – read cardiogram + raw DICOM, preview both assets.
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py ingest
  ```
2. **Align** – compute the global offset and display pre/post trigger markers.
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py alignment
  ```
3. **Detect** – highlight every cardiac cycle plus steady-state bands.
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py cycles
  ```
4. **Filter** – run the full pipeline, show accepted/rejected frame previews.
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py filter
  ```
5. **Validate @30 cases** – run diverse + adversarial inputs (outliers, missing cardiograms, corrupt DICOMs) and auto-plot the verdicts.
  ```powershell
  .venv\Scripts\python.exe examples\five_step_demo.py validation
  ```

Assets land under `demo_steps/step_<n>_*` and open automatically unless you pass `--no-open`. Use this sequence when you only have five keystrokes to prove the subsystem works end to end.

---
## 1. Prep (one-time per session)
1. Activate the project environment (optional if terminal already in `.venv`):
   ```powershell
   .venv\Scripts\activate
   ```
2. Ensure dependencies are installed:
   ```powershell
   pip install -e ".[dev]"
   ```

---
## 2. Generate Inputs
### A. Baseline dataset (quick walkthrough)
```powershell
.venv\Scripts\python.exe examples\create_sample_inputs.py
```
- Produces `examples/data/cardiogram_sample.csv` and `examples/data/dicom_sample/`.

### B. Multi-scenario datasets (for richer story)
```powershell
.venv\Scripts\python.exe examples\demo_pipeline.py --root examples\demo_runs
```
- Creates `set_a_stable` and `set_b_variable` folders, each with:
  - `cardiogram.csv`
  - `dicom_series/` (raw frames)
  - `filtered_series/` (outputs)

---
## 3. Run the Pipeline (baseline data)
```powershell
.venv\Scripts\python.exe main.py examples\data\cardiogram_sample.csv examples\data\dicom_sample --output output\sample_filtered
```
- Console prints: accepted/rejected counts, stable cycles, alignment offset, output path.
- Show `output/sample_filtered/` vs `examples/data/dicom_sample/`.

---
## 4. Visualize Scenarios
Run once per scenario you want to showcase (example: `set_a_stable`).
```powershell
.venv\Scripts\python.exe examples\visualize_run.py ^
  examples\demo_runs\set_a_stable\cardiogram.csv ^
  examples\demo_runs\set_a_stable\dicom_series ^
  examples\demo_runs\set_a_stable\filtered_series ^
  --output examples\demo_runs\set_a_stable\visuals
```
Artifacts to show:
- `cardiogram_frames.png`: ECG trace + color-coded frame timestamps
- `accepted_frame_0.png` / `rejected_frame_0.png`: DICOM previews
- `summary.txt`: table listing every frame timestamp and decision
Repeat for `set_b_variable` if desired.

---
## 5. Tell the Story (Talking Points)
1. **Problem & Inputs**: Show the cardiogram CSV (first few lines) and the DICOM folder.
2. **Alignment & Cycle Detection**: Describe time offset, moving-average thresholding, RR stability checks.
3. **Filtering**: Explain stable-phase window (`stable_phase_offset`, `stable_phase_fraction`).
4. **Outputs**: Compare raw vs filtered frames, reference `cardiogram_frames.png` and sample PNGs.
5. **Metrics**: Quote the console summary + `summary.txt` (accepted/rejected counts, stable cycles).
6. **Visual proof**: Display the PNGs inside `examples/demo_runs/<scenario>/visuals/`.

---
## 6. Testing & Validation
```powershell
.venv\Scripts\python.exe -m pytest
```
- `test_alignment.py`: verifies offsets
- `test_cycle_detection.py`: validates stable cycles on synthetic ECG
- `test_filter_engine.py`: ensures only stable-phase frames survive
Mention that green tests = automated proof of correctness.

---
## 7. Optional Extras
- Show `config/default.yaml` and tweak a parameter (e.g., `stable_phase_fraction`) to demonstrate configurability.
- Mention future work ideas if time allows (drift compensation, advanced detectors).

Keep this playbook open during the demo. Run commands sequentially, show the generated artifacts, and tie each step back to the algorithm explanations.
