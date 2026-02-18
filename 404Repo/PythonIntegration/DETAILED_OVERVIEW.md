# Python.NET Integration — Detailed Overview

## 1. What Is Python.NET?

Python.NET (`pythonnet`) is a bridge between the .NET CLR and the CPython runtime. It allows C# code to import Python modules, call Python functions, and receive return values — all within the same process. Data is marshaled automatically between Python types (str, int, float, dict, list, bytes) and their .NET counterparts (string, int, double, PyDict, PyList, byte[]).

The NuGet package is `pythonnet` (version 3.0.4 in this project). It requires a CPython installation (3.8+) on the host machine.

## 2. Why Use Python.NET in BioMetrix?

The BioMetrix desktop application is a .NET 8 WPF app written in C#. The project also includes Python utility scripts for DICOM file generation, image inspection, and testing. Rather than running Python as a separate subprocess and parsing stdout, Python.NET lets C# call Python functions directly with:

- Type-safe return values (no string parsing)
- In-process execution (no subprocess overhead)
- Access to Python's scientific computing ecosystem (numpy, pydicom) from C#

## 3. What Changed in the Python Scripts

### 3.1 `image_viewer.py` (Dummy python code/)

**Before:** A single-line script calling `os.startfile("sample_picture.png")` — a side effect that could not return data to a caller.

**After:** A module with five callable functions:

| Function | Returns | Purpose |
|----------|---------|---------|
| `get_image_info(path)` | `dict` | File metadata: name, extension, size, existence |
| `load_image_bytes(path)` | `bytes` or `None` | Raw file bytes for loading into C# BitmapImage |
| `get_image_dimensions(path)` | `dict` | Width, height, format (PNG header parsing) |
| `validate_image_file(path)` | `dict` | Magic-byte signature check (PNG, JPEG, GIF, BMP, WEBP) |
| `open_image_native(path)` | `dict` | Wraps os.startfile() with error handling |

All functions return dictionaries with string keys and primitive values, which Python.NET marshals into `PyDict` objects accessible from C# via indexing (`result["key"]`).

The original `os.startfile()` behavior is preserved in `open_image_native()` but wrapped with error handling and return values.

### 3.2 `example.py`

**Before:** An empty file with only a comment.

**After:** A demonstration module with functions that exercise every common data type crossing the Python.NET boundary:

| Function | Input Types | Return Type | Demonstrates |
|----------|-------------|-------------|-------------|
| `greet(name)` | str | str | String marshaling |
| `add(a, b)` | int, int | int | Integer marshaling |
| `multiply(a, b)` | float, float | float | Float marshaling |
| `reverse_string(text)` | str | str | String round-tripping |
| `make_patient_record(...)` | str, str, int | dict | Dictionary marshaling |
| `fibonacci(n)` | int | list | List marshaling |
| `get_module_info()` | (none) | dict | Module introspection |

### 3.3 `generate_dicom.py`

**Before:** Fully functional DICOM generator, but all results were printed to stdout. The `build_dicom_file()` function returned a `Path` object.

**After:** Same core logic, plus five new Python.NET-friendly functions:

| Function | Returns | Purpose |
|----------|---------|---------|
| `get_available_patterns()` | `list[str]` | Enumerate supported patterns for UI |
| `generate_pixel_data(...)` | `bytes` | Raw grayscale pixels without disk I/O |
| `generate_color_pixel_data(...)` | `bytes` | Raw RGB pixels without disk I/O |
| `build_dicom_and_report(...)` | `dict` | Create DICOM file + return structured metadata |
| `get_dicom_metadata(path)` | `dict` | Read back DICOM attributes from an existing file |

Key design choice: `generate_pixel_data()` and `generate_color_pixel_data()` return raw `bytes` objects. In Python.NET, these map to `PyObject` but can be iterated or converted to `byte[]` in C#. This allows the desktop app to generate synthetic image data from Python and render it in a WPF `WriteableBitmap` without touching the filesystem.

## 4. C# Test Project Structure

```
404Repo/PythonIntegration/
├── PythonIntegration.sln
└── PythonIntegrationTests/
    ├── PythonIntegrationTests.csproj   # .NET 8 console app, references pythonnet 3.0.4
    ├── Program.cs                       # Entry point — runs all test suites
    ├── PythonSetup.cs                   # Runtime initialization and sys.path configuration
    ├── Assert.cs                        # Lightweight assertion helpers
    ├── TestResults.cs                   # Test case collection and reporting
    ├── ExampleModuleTests.cs            # 9 tests for example.py
    ├── ImageViewerTests.cs              # 9 tests for image_viewer.py
    └── GenerateDicomTests.cs            # 12 tests for generate_dicom.py
```

### 4.1 Runtime Initialization (`PythonSetup.cs`)

Before any Python code can run, the C# process must:

1. **Locate the Python scripts** — `PythonSetup.FindRepoRoot()` walks up the directory tree from the build output folder to find the `404Repo/` directory.
2. **Initialize the CPython runtime** — `PythonEngine.Initialize()` loads the Python shared library.
3. **Configure `sys.path`** — Both `PythonScripts/` and `Dummy python code/` are appended to Python's import path so that `Py.Import("image_viewer")` works.

The Python shared library can be configured via the `PYTHONNET_PYDLL` environment variable (e.g., `python311.dll` on Windows, `/usr/lib/libpython3.11.so` on Linux).

### 4.2 Test Execution Pattern

Each test file follows the same pattern:

```csharp
using (Py.GIL())                        // Acquire Python's Global Interpreter Lock
{
    dynamic module = Py.Import("module_name");  // Import the Python module
    dynamic result = module.function(args);      // Call a Python function
    string value = result["key"].ToString();     // Access return values
    Assert.AreEqual("expected", value);          // Validate
}
```

The `Py.GIL()` call is mandatory — Python.NET requires the GIL to be held for all Python operations, just like CPython's C API.

### 4.3 Test Coverage

| Module | Tests | What's Tested |
|--------|-------|---------------|
| `example.py` | 9 | String, int, float, dict, list marshaling; edge cases (empty string, zero fibonacci) |
| `image_viewer.py` | 9 | File metadata, byte loading, PNG dimension parsing, format validation, error handling |
| `generate_dicom.py` | 12 | Pattern enumeration, pixel data generation, DICOM creation (single/multi/color), metadata readback, error handling |

Total: **30 test cases**.

### 4.4 Assertion Helpers

Rather than pulling in a full test framework (xUnit, NUnit), the project uses a minimal `Assert` class with:
- `AreEqual(expected, actual)` — exact equality
- `AreApproximatelyEqual(expected, actual, tolerance)` — floating-point comparison
- `IsTrue(condition)` / `IsFalse(condition)` — boolean checks
- `IsNotNull(value)` — null guard

Failed assertions throw `AssertionException`, which `TestResults.RunTest()` catches and records without aborting the suite.

## 5. How to Run the Tests

### Prerequisites

1. **.NET 8 SDK** installed
2. **Python 3.8+** installed and on PATH
3. **Python packages:** `pip install numpy pydicom`
4. **NuGet restore:** `dotnet restore` (pulls `pythonnet 3.0.4`)

### Commands

```bash
# From the repository root
cd 404Repo/PythonIntegration

# Restore NuGet packages
dotnet restore

# Build
dotnet build

# Run tests
dotnet run --project PythonIntegrationTests

# If Python isn't auto-detected, set the DLL path:
# Windows:
set PYTHONNET_PYDLL=python311.dll
# Linux:
export PYTHONNET_PYDLL=/usr/lib/x86_64-linux-gnu/libpython3.11.so
```

### Expected Output

```
========================================
  BioMetrix Python.NET Integration Tests
========================================

Running example.py tests...
  [PASS] greet returns greeting string
  [PASS] add returns correct sum
  ...

Running image_viewer.py tests...
  [PASS] get_image_info returns metadata for existing file
  ...

Running generate_dicom.py tests...
  [PASS] get_available_patterns returns pattern list
  ...

========================================
  Test Summary
========================================

--- example.py ---
  Total: 9  Passed: 9  Failed: 0

--- image_viewer.py ---
  Total: 9  Passed: 9  Failed: 0

--- generate_dicom.py ---
  Total: 12  Passed: 12  Failed: 0

Overall: 30 tests, 30 passed, 0 failed
```

## 6. Type Marshaling Reference

| Python Type | C# Access Pattern | Notes |
|-------------|-------------------|-------|
| `str` | `string s = pyObj.ToString();` or cast `(string)pyObj` | Direct conversion |
| `int` | `int n = (int)pyObj;` | Works for values fitting Int32 |
| `float` | `double d = (double)pyObj;` | Python float → C# double |
| `bool` | `bool b = (bool)pyObj;` | Direct conversion |
| `dict` | `pyObj["key"]` | Returns dynamic; cast inner values |
| `list` | `pyObj[i]` or `pyObj.__len__()` | Index access; use `__len__()` for count |
| `bytes` | `pyObj.__len__()` for length | Iterate or convert to byte[] |
| `None` | Compare with `Py.None()` | Use `obj == null` or `obj.Equals(Py.None())` |

## 7. Architecture Diagram

```
┌────────────────────────────────────────────────────┐
│                C# (.NET 8)                         │
│                                                    │
│  Program.cs                                        │
│    │                                               │
│    ├── PythonSetup.Initialize()                    │
│    │     └── PythonEngine.Initialize()             │
│    │     └── sys.path.append(scriptDirs)           │
│    │                                               │
│    ├── ExampleModuleTests.Run()                    │
│    │     └── Py.Import("example")                  │
│    │         └── example.greet(), .add(), etc.     │
│    │                                               │
│    ├── ImageViewerTests.Run()                      │
│    │     └── Py.Import("image_viewer")             │
│    │         └── image_viewer.get_image_info()     │
│    │                                               │
│    └── GenerateDicomTests.Run()                    │
│          └── Py.Import("generate_dicom")           │
│              └── generate_dicom.build_dicom_and_   │
│                  report(), .get_dicom_metadata()   │
│                                                    │
│  ┌──────────────────────────────────────────────┐  │
│  │           Python.NET (pythonnet 3.0.4)       │  │
│  │  Manages GIL, type marshaling, module import │  │
│  └──────────────┬───────────────────────────────┘  │
└─────────────────┼──────────────────────────────────┘
                  │ In-process bridge
┌─────────────────┼──────────────────────────────────┐
│                 ▼                                   │
│            CPython 3.x Runtime                      │
│                                                     │
│  PythonScripts/                                     │
│    ├── example.py          (basic type demos)       │
│    ├── generate_dicom.py   (DICOM generation)       │
│    └── Dummy python code/                           │
│        ├── image_viewer.py (image inspection)       │
│        └── sample_picture.png                       │
│                                                     │
│  Dependencies: numpy, pydicom                       │
└─────────────────────────────────────────────────────┘
```

## 8. Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| `DllNotFoundException` on startup | Python shared library not found | Set `PYTHONNET_PYDLL` to the full path of your Python DLL/SO |
| `PythonException: ModuleNotFoundError` | `sys.path` doesn't include script directories | Verify `PythonSetup.PythonScriptsPath` points to the correct directory |
| `ImportError: numpy` or `pydicom` | Missing Python packages | Run `pip install numpy pydicom` |
| All generate_dicom tests fail | numpy/pydicom not installed | The test suite gracefully reports the import error |
| `BadPythonDllException` | Python version mismatch | Ensure `pythonnet` version is compatible with your Python version |
| Tests pass locally but fail in CI | Python not available in CI environment | Install Python in your CI pipeline and set `PYTHONNET_PYDLL` |
