# Scan Filter Integration — Validation & Testing Plan

## 1. Overview

This plan covers validation and testing for the scan filter integration across three layers:
- **Python layer** — `scan_filter` package + `scan_filter_bridge.py`
- **Python.NET bridge layer** — `PythonSetup.cs` + `ScanFilterService.cs`
- **WPF UI layer** — `MainViewModel.cs` + `MainWindow.xaml`

Testing proceeds bottom-up: verify the Python pipeline independently, then the bridge, then the full UI flow.

---

## 2. Prerequisites

| Requirement | How to verify |
|-------------|---------------|
| .NET 8 SDK installed | `dotnet --version` → `8.x.x` |
| Python 3.9+ installed and on PATH | `python3 --version` |
| Python dependencies installed | `pip install numpy pandas pydicom pyyaml matplotlib` (or activate venv in `Scan Filter Code/.venv`) |
| Project builds | `dotnet build 404Repo/DesktopApp/403DesktopApp.sln` succeeds with 0 errors |

---

## 3. Layer 1 — Python Unit Tests (Existing + New)

### 3.1 Run Existing `scan_filter` Tests

```bash
cd 404Repo/PythonScripts/Scan\ Filter\ Code
python -m pytest tests/ -v
```

**Expected:** All 3 test files pass:
- `test_alignment.py` — offset calculation correct
- `test_cycle_detection.py` — stable cycles detected from synthetic signal
- `test_filter_engine.py` — frames accepted/rejected based on stable phases

### 3.2 Generate Synthetic Test Data

```bash
cd 404Repo/PythonScripts/Scan\ Filter\ Code
python -c "
from examples.create_sample_inputs import generate_cardiogram, generate_dicom_series
generate_cardiogram('test_data/cardio.csv', beats=18, base_rr_ms=800.0)
generate_dicom_series('test_data/dicom_series', frame_count=12, start_ms=100.0, spacing_ms=200.0, seed=42)
print('Test data generated.')
"
```

**Expected:** `test_data/cardio.csv` and `test_data/dicom_series/` with 12 `.dcm` files are created.

### 3.3 Test `scan_filter_bridge.py` Standalone

```bash
cd 404Repo/PythonScripts

# Test validate_inputs with valid data
python -c "
from scan_filter_bridge import validate_inputs, run_scan_filter

# Valid inputs
result = validate_inputs(
    'Scan Filter Code/test_data/cardio.csv',
    'Scan Filter Code/test_data/dicom_series')
print('Validation:', result)
assert result['valid'] == True
assert result['dicom_file_count'] == 12

# Invalid CSV path
result = validate_inputs('nonexistent.csv', 'Scan Filter Code/test_data/dicom_series')
print('Invalid CSV:', result)
assert result['valid'] == False
assert result['csv_exists'] == False

# Invalid DICOM folder
result = validate_inputs('Scan Filter Code/test_data/cardio.csv', '/tmp/empty_dir')
print('Invalid folder:', result)
assert result['valid'] == False

print('All validate_inputs tests passed.')
"
```

### 3.4 Test Bridge `run_scan_filter` End-to-End

```bash
cd 404Repo/PythonScripts
python -c "
import tempfile, os
from scan_filter_bridge import run_scan_filter

output_dir = os.path.join(tempfile.mkdtemp(), 'filtered')
result = run_scan_filter(
    'Scan Filter Code/test_data/cardio.csv',
    'Scan Filter Code/test_data/dicom_series',
    output_dir)
print('Result:', result)
assert result['success'] == True, f'Pipeline failed: {result[\"error\"]}'
assert result['accepted_frames'] > 0, 'No frames accepted'
assert result['accepted_frames'] + result['rejected_frames'] == 12
assert os.path.isdir(result['output_dir'])
dcm_count = len([f for f in os.listdir(result['output_dir']) if f.endswith('.dcm')])
assert dcm_count == result['accepted_frames'], f'Expected {result[\"accepted_frames\"]} files, got {dcm_count}'
print(f'Pipeline succeeded: {result[\"accepted_frames\"]} accepted, {result[\"rejected_frames\"]} rejected')
print('End-to-end bridge test passed.')
"
```

### 3.5 Bridge Error Handling Tests

| Test case | Input | Expected `result` |
|-----------|-------|-------------------|
| Missing CSV | `run_scan_filter('missing.csv', 'valid_dir', '/tmp/out')` | `success=False`, error mentions CSV |
| Missing DICOM folder | `run_scan_filter('valid.csv', '/nonexistent', '/tmp/out')` | `success=False`, error mentions folder |
| Empty DICOM folder | Create empty dir, pass as `dicom_folder` | `success=False`, error mentions no `.dcm` files |
| Malformed CSV | CSV with wrong column names | `success=False`, error mentions column names |
| Custom config | Pass valid YAML `config_path` | Uses custom parameters, `success=True` |
| No config file | `config_path='/nonexistent.yaml'` | Falls back to defaults, `success=True` |

---

## 4. Layer 2 — Python.NET Bridge Tests (C#)

### 4.1 Build Verification

```bash
dotnet build 404Repo/DesktopApp/403DesktopApp.sln
```

**Expected:** Build succeeds with 0 errors. Verify:
- `pythonnet 3.0.4` restored
- `UseWindowsForms` enabled (no `FolderBrowserDialog` compile errors)
- No ambiguous reference warnings between WPF and WinForms

### 4.2 PythonSetup Initialization Test

Verify that `PythonSetup.Initialize()` succeeds by running the existing integration test suite as a reference:

```bash
cd 404Repo/PythonIntegration
dotnet run --project PythonIntegrationTests/PythonIntegrationTests.csproj
```

**Expected:** All existing tests pass (confirms Python.NET runtime works).

### 4.3 Manual PythonSetup Verification Checklist

| Check | How to verify | Pass criteria |
|-------|---------------|---------------|
| `PythonSetup.Initialize()` succeeds | Debug output shows "Runtime initialized" | No exception thrown |
| `PythonScriptsPath` resolved | Debug output shows correct absolute path | Path ends with `404Repo/PythonScripts` |
| `ScanFilterCodePath` resolved | Debug output shows correct absolute path | Path ends with `PythonScripts/Scan Filter Code` |
| `sys.path` includes PythonScripts | `Py.Import("scan_filter_bridge")` succeeds | No `ModuleNotFoundError` |
| `sys.path` includes Scan Filter Code | `Py.Import("scan_filter")` succeeds | No `ModuleNotFoundError` |
| `PythonSetup.IsInitialized` is true | Check property after init | Returns `true` |
| `PythonSetup.Shutdown()` cleans up | Call after init | `IsInitialized` returns `false` |
| Idempotent initialization | Call `Initialize()` twice | No exception on second call |

### 4.4 ScanFilterService Integration Test

Create a test console app or add to existing test suite:

```csharp
// Test valid pipeline execution
var service = new ScanFilterService();
service.EnsurePythonInitialized();

string csv = Path.Combine(PythonSetup.ScanFilterCodePath, "test_data", "cardio.csv");
string dicom = Path.Combine(PythonSetup.ScanFilterCodePath, "test_data", "dicom_series");
string output = Path.Combine(Path.GetTempPath(), "test_filter_" + Guid.NewGuid().ToString("N")[..8]);

var result = await service.RunFilterAsync(csv, dicom, output);

Assert(result.Success, "Pipeline should succeed");
Assert(result.AcceptedFrames > 0, "Should accept frames");
Assert(result.AcceptedFrames + result.RejectedFrames == 12, "Total should be 12");
Assert(Directory.Exists(result.OutputDir), "Output dir should exist");
Assert(result.Error == "", "No error expected");
```

### 4.5 ScanFilterService Error Handling Tests

| Test case | Setup | Expected |
|-----------|-------|----------|
| Invalid CSV path | `RunFilterAsync("missing.csv", validDir, output)` | `Success=false`, `Error` contains "validation failed" |
| Invalid DICOM folder | `RunFilterAsync(validCsv, "/nonexistent", output)` | `Success=false`, `Error` contains "validation failed" |
| Python not initialized | Call `RunFilterAsync` without `EnsurePythonInitialized` | `Success=false`, `Error` contains Python error |
| Concurrent calls | Two `RunFilterAsync` calls simultaneously | Both complete (GIL serializes them) |

### 4.6 Type Marshalling Verification

| Python type | Expected C# type | How to verify |
|-------------|-------------------|---------------|
| `result["success"]` (bool) | `bool` | `(bool)result["success"]` doesn't throw |
| `result["accepted_frames"]` (int) | `int` | `(int)result["accepted_frames"]` doesn't throw |
| `result["alignment_offset_ms"]` (float) | `double` | `(double)result["alignment_offset_ms"]` doesn't throw |
| `result["output_dir"]` (str) | `string` | `(string)result["output_dir"]` doesn't throw |
| `result["error"]` (str) | `string` | Empty string on success, non-empty on failure |

---

## 5. Layer 3 — WPF UI / ViewModel Tests

### 5.1 MainViewModel Unit Tests (Logic Only)

These tests verify ViewModel logic without launching the WPF app. Can be run in a test project referencing `403DesktopApp`.

#### 5.1.1 Scan Filter Command State

| Test | Action | Expected |
|------|--------|----------|
| Command initially enabled | Check `RunScanFilterCommand.CanExecute(null)` | Returns `true` |
| Command disabled while running | Set `IsFilterRunning = true` | `CanExecute` returns `false` |
| Command re-enabled after completion | Set `IsFilterRunning = false` | `CanExecute` returns `true` |

#### 5.1.2 Filtered Series Navigation

| Test | Setup | Expected |
|------|-------|----------|
| LoadFilteredDicomFolder sets mode | Call with folder of 5 `.dcm` files | `_isFilteredSeriesMode = true`, `TotalFrames = 5`, `CurrentFrameIndex = 0` |
| NextFrame in filtered mode | Set `CurrentFrameIndex = 0`, `_filteredDicomFiles.Count = 5` | `CurrentFrameIndex = 1`, loads correct file |
| PreviousFrame at boundary | `CurrentFrameIndex = 0` | No change (CanGoPreviousFrame returns false) |
| LastFrame in filtered mode | `_filteredDicomFiles.Count = 5` | `CurrentFrameIndex = 4` |
| ClearImage resets filter mode | Call after loading filtered series | `_isFilteredSeriesMode = false`, `_filteredDicomFiles` empty, `FilterResultSummary = ""` |
| OpenImage resets filter mode | Open single file after filtered series | `_isFilteredSeriesMode = false`, `FilterResultSummary = ""` |

#### 5.1.3 Property Change Notifications

| Property | Triggers | Verify via `PropertyChanged` event |
|----------|----------|-------------------------------------|
| `IsFilterRunning` | Set to true/false | Event fires with property name |
| `FilterResultSummary` | Set to string | Event fires with property name |
| `StatusText` | Updated during filter | Event fires with property name |
| `FrameInfo` | `CurrentFrameIndex` or `TotalFrames` changes | Event fires |

### 5.2 Manual UI Testing Procedure

#### 5.2.1 Happy Path — Full Scan Filter Workflow

**Prerequisites:** Synthetic test data generated (Section 3.2).

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Launch app | LoginWindow appears |
| 2 | Log in with MD001 / demo123 | MainWindow appears |
| 3 | Click "Image Viewer" tab | Image Viewer toolbar visible |
| 4 | Verify "Scan Filter" button | Green button visible in toolbar, between zoom and frame nav |
| 5 | Click "Scan Filter" | OpenFileDialog appears titled "Select Cardiogram CSV File" |
| 6 | Select `test_data/cardio.csv` | FolderBrowserDialog appears titled "Select DICOM Series Folder" |
| 7 | Select `test_data/dicom_series/` | Status bar shows "Initializing Python runtime..." then "Running scan filter pipeline..." |
| 8 | Wait for completion | Status shows "Scan filter complete. N frames accepted." |
| 9 | Verify filter summary | Italic text shows "Accepted: N, Rejected: M, Stable cycles: K" |
| 10 | Verify image displayed | First filtered DICOM frame rendered in viewer |
| 11 | Verify frame counter | Shows "Frame 1 of N" |
| 12 | Click Next Frame (►) | Advances to frame 2, image changes |
| 13 | Click Last Frame (►\|) | Jumps to last frame |
| 14 | Click First Frame (\|◄) | Returns to frame 1 |
| 15 | Use Ctrl+Scroll to zoom | Zoom applies to filtered frame |
| 16 | Click "Clear" | Image cleared, filter summary cleared, status shows "Image cleared" |

#### 5.2.2 Cancel Dialogs

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Scan Filter" | CSV dialog opens |
| 2 | Click Cancel | No error, status unchanged, button remains enabled |
| 3 | Click "Scan Filter" again | CSV dialog opens |
| 4 | Select valid CSV | Folder dialog opens |
| 5 | Click Cancel | No error, status unchanged, button remains enabled |

#### 5.2.3 Button Disabled During Execution

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Start scan filter with valid inputs | Button becomes disabled (grayed out) |
| 2 | Try clicking "Scan Filter" again | Nothing happens (command CanExecute = false) |
| 3 | Wait for completion | Button re-enables |

#### 5.2.4 Switch Between Modes

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run scan filter, load filtered frames | Filtered mode active, frames displayed |
| 2 | Click "Open Image" | OpenFileDialog appears |
| 3 | Select a single multi-frame DICOM | Switches to single-file mode, filter summary cleared |
| 4 | Navigate frames | Uses multi-frame navigation (within single file) |
| 5 | Run scan filter again | Switches back to filtered mode |

---

## 6. Error Handling & Edge Case Tests

### 6.1 Python Runtime Errors

| Test case | How to trigger | Expected behavior |
|-----------|----------------|-------------------|
| Python not installed | Unset PATH to python | Status: "Scan filter error: ..." with clear message |
| Missing numpy/pandas | Uninstall from venv | Status: "Scan filter failed: ..." with import error |
| Corrupt Python DLL | Set `PYTHONNET_PYDLL` to invalid file | Status: error on initialization |

### 6.2 Invalid Input Data

| Test case | Input | Expected |
|-----------|-------|----------|
| CSV with wrong columns | CSV with columns "time,value" instead of "timestamp_ms,signal" | Status: "Scan filter failed: ..." mentioning column names |
| Empty CSV | 0-byte CSV file | Status: "Scan filter failed: ..." |
| Binary file as CSV | Select a `.dcm` file as CSV | Status: "Scan filter failed: ..." with parse error |
| Empty DICOM folder | Folder with no `.dcm` files | Status: "Scan filter failed: Input validation failed: No .dcm files found" |
| Non-DICOM files in folder | Folder with `.txt` files | Status: "Scan filter failed: Input validation failed: No .dcm files found" |
| DICOM files without TriggerTime | DICOM files missing timing tags | Status: "Scan filter failed: ..." with timestamp extraction error |

### 6.3 Output Edge Cases

| Test case | Condition | Expected |
|-----------|-----------|----------|
| All frames rejected | Very strict filter parameters | Fallback: minimum 2 frames output (per `fallback_min_frames`) |
| All frames accepted | Very loose filter parameters | All 12 frames in output, all navigable |
| Single frame in output | Only 1 frame passes filter | No frame navigation controls (or "Frame 1 of 1") |
| Output dir already exists | Re-run filter (different GUID prevents this) | GUID in temp path prevents collision |
| Disk full | Temp directory on full disk | Status: "Scan filter failed: ..." with write error |

### 6.4 Timing & Performance

| Test case | Setup | What to measure |
|-----------|-------|-----------------|
| Small series (12 frames) | Default test data | Should complete in < 5 seconds |
| Medium series (100 frames) | Generate with `frame_count=100` | Should complete in < 30 seconds |
| Large series (500+ frames) | Generate with `frame_count=500` | Monitor memory usage, completion time |
| Rapid repeated runs | Click Scan Filter, complete, click again immediately | No state leaks, each run independent |

---

## 7. Regression Tests

After any changes to the integration, verify these existing features still work:

| Feature | Test |
|---------|------|
| Login flow | Login with MD001/demo123 → MainWindow opens |
| Open single DICOM | Open Image → select single `.dcm` file → renders correctly |
| Multi-frame navigation | Open multi-frame DICOM → arrow keys navigate frames |
| Zoom | Ctrl+scroll zooms image in/out (range 10%-1000%) |
| Pan | Click-drag pans the image |
| Clear | Clear button resets all state |
| Keyboard shortcuts | Left/Right arrows, Shift+Left/Right |
| App exit | Close window → app exits cleanly (no hanging Python process) |

---

## 8. Test Data Requirements

### 8.1 Synthetic Data (for automated/repeatable tests)

Generate using `examples/create_sample_inputs.py`:

| Dataset | Parameters | Purpose |
|---------|------------|---------|
| Standard | 18 beats, 800ms RR, 12 frames, seed=42 | Happy path testing |
| Many frames | 18 beats, 800ms RR, 100 frames, seed=42 | Performance testing |
| Few frames | 5 beats, 800ms RR, 3 frames, seed=42 | Edge case: minimal input |
| High jitter | 18 beats, 800ms RR, 12 frames, jitter_ms=50 | Robustness: noisy timing |
| Wide spacing | 18 beats, 800ms RR, 6 frames, spacing_ms=1000 | Edge case: sparse frames |

### 8.2 Real Clinical Data (if available)

- Cardiac MRI DICOM series with TriggerTime tags
- Corresponding ECG/cardiogram CSV export
- **Note:** Real data may contain PHI — use only de-identified data for testing

---

## 9. Automated Test Implementation Roadmap

### Phase 1: Python Tests (run with `pytest`)
- [x] Existing unit tests for aligner, cycle detection, filter engine
- [ ] New: `tests/test_bridge.py` — tests for `scan_filter_bridge.py` functions
- [ ] New: `tests/test_pipeline_integration.py` — end-to-end pipeline with synthetic data

### Phase 2: C# Integration Tests (new test project)
- [ ] `ScanFilterBridgeTests.cs` — Python.NET calls to bridge functions
- [ ] `PythonSetupTests.cs` — initialization, shutdown, path resolution
- [ ] `ScanFilterServiceTests.cs` — async service with valid/invalid inputs

### Phase 3: ViewModel Tests (new test project)
- [ ] `MainViewModelFilterTests.cs` — command state, property notifications, navigation modes
- [ ] Uses mocked `ScanFilterService` to avoid Python dependency in unit tests

---

## 10. Test Execution Checklist

Use this checklist for each release/PR:

```
[ ] Python unit tests pass (pytest)
[ ] Synthetic test data generated successfully
[ ] scan_filter_bridge standalone tests pass
[ ] .NET solution builds with 0 errors
[ ] PythonSetup initializes without errors
[ ] ScanFilterService happy path succeeds
[ ] UI happy path: CSV → folder → filter → view frames
[ ] UI cancel paths: cancel at CSV dialog, cancel at folder dialog
[ ] UI error path: invalid CSV shows error
[ ] UI error path: empty DICOM folder shows error
[ ] Frame navigation works in filtered mode
[ ] Clear resets filtered mode completely
[ ] Open single file after filter resets mode
[ ] Zoom/pan works on filtered frames
[ ] App exits cleanly (no hanging processes)
[ ] Existing DICOM viewer features not broken (regression)
```
