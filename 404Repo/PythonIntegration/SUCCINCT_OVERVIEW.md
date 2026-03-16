# Python.NET Integration — Succinct Overview

## What

Python.NET (`pythonnet`) lets C# call Python functions directly, in-process. This integration adapts the BioMetrix Python scripts so the .NET 8 desktop app can invoke them without spawning subprocesses.

## Changes Made

### Python Scripts (edited)

- **`image_viewer.py`** — Replaced bare `os.startfile()` with five functions returning dicts: `get_image_info()`, `load_image_bytes()`, `get_image_dimensions()`, `validate_image_file()`, `open_image_native()`.
- **`example.py`** — Replaced empty file with type-marshaling demos: `greet()`, `add()`, `multiply()`, `reverse_string()`, `make_patient_record()`, `fibonacci()`, `get_module_info()`.
- **`generate_dicom.py`** — Added five wrapper functions alongside existing code: `get_available_patterns()`, `generate_pixel_data()`, `generate_color_pixel_data()`, `build_dicom_and_report()`, `get_dicom_metadata()`.

### C# Test Project (new)

```
404Repo/PythonIntegration/
└── PythonIntegrationTests/    # .NET 8 console app + pythonnet 3.0.4
    ├── Program.cs             # Runs all suites, reports pass/fail
    ├── PythonSetup.cs         # Initializes Python runtime, sets sys.path
    ├── ExampleModuleTests.cs  # 9 tests — string, int, float, dict, list
    ├── ImageViewerTests.cs    # 9 tests — file metadata, bytes, dimensions
    └── GenerateDicomTests.cs  # 12 tests — pixel gen, DICOM create/read
```

**30 total test cases** covering all Python functions.

## How to Run

```bash
# Prerequisites: .NET 8 SDK, Python 3.8+, pip install numpy pydicom
cd 404Repo/PythonIntegration
dotnet restore && dotnet run --project PythonIntegrationTests
```

Set `PYTHONNET_PYDLL` if Python isn't auto-detected (e.g., `python311.dll` or `/usr/lib/libpython3.11.so`).
Set `PYTHONNET_PYHOME` if you get `No module named 'encodings'` (e.g., `C:\Users\you\anaconda3` or `/usr`).

## Key Concept

```csharp
PythonEngine.Initialize();              // Start CPython in-process
using (Py.GIL())                        // Hold the Global Interpreter Lock
{
    dynamic mod = Py.Import("module");   // Import a .py file
    dynamic result = mod.function(args); // Call it, get return value
    string val = result["key"].ToString(); // Access dict entries
}
PythonEngine.Shutdown();
```

All Python functions return dicts/lists/primitives that Python.NET automatically marshals to C# dynamic objects.
