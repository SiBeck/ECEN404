# CLAUDE.md — ECEN404 BioMetrix Project

## Project Overview

BioMetrix is a medical imaging platform with two .NET 8 components:

1. **DesktopApp** — WPF desktop client for DICOM image viewing and provider authentication
2. **Database (BioMetrixDatabase)** — ASP.NET Core Web API backend (currently scaffolded with placeholder endpoints)

Both live under `404Repo/`.

## Repository Layout

```
ECEN404/
├── CLAUDE.md                              # This file (repo-wide guide)
├── .gitignore
└── 404Repo/
    ├── Database/
    │   ├── BioMetrixDatabase.sln
    │   └── BioMetrixDatabase/
    │       ├── BioMetrixDatabase.csproj    # ASP.NET Core Web API (.NET 8)
    │       ├── Program.cs                  # API entry point
    │       ├── WeatherForecast.cs          # Placeholder model
    │       ├── Controllers/
    │       │   └── WeatherForecastController.cs
    │       ├── Properties/
    │       │   └── launchSettings.json     # Ports: HTTP 5168, HTTPS 7183
    │       ├── appsettings.json
    │       └── appsettings.Development.json
    └── DesktopApp/
        ├── CLAUDE.md                       # Desktop-app-specific guide
        ├── copilot-instructions.md
        ├── 403DesktopApp.sln
        └── 403DesktopApp/
            ├── 403DesktopApp.csproj        # WPF app (.NET 8, Windows-only)
            ├── App.xaml / App.xaml.cs       # Entry point: LoginWindow → MainWindow
            ├── MainWindow.xaml / .xaml.cs   # Tabbed UI: Home, Image Viewer
            ├── LoginWindow.xaml / .xaml.cs  # Login dialog
            ├── Commands/
            │   └── RelayCommand.cs         # Generic ICommand implementation
            ├── Models/
            │   └── MedicalProvider.cs      # Provider data model
            ├── Services/
            │   └── AuthenticationServices.cs  # AuthenticationService + PasswordHasher
            ├── ViewModels/
            │   ├── MainViewModel.cs        # DICOM viewer logic, zoom, pan, frames
            │   └── LoginViewModel.cs       # Login/auth logic
            └── Views/                      # Empty — reserved for future UserControls
```

## Tech Stack

| Component | Language | Framework | Target | SDK |
|-----------|----------|-----------|--------|-----|
| Desktop App | C# | WPF | `net8.0-windows` | .NET 8 |
| Backend API | C# | ASP.NET Core Web API | `net8.0` | .NET 8 |
| Python ML/Analysis | Python 3.10+ | NumPy, scikit-image, pydicom, ML libs | — | Python.NET bridge |

## Build & Run

**Prerequisites:** .NET 8 SDK, Python 3.10+ (with pip). The desktop app requires Windows.

### Desktop App

```bash
# Build
dotnet build 404Repo/DesktopApp/403DesktopApp.sln

# Run
dotnet run --project 404Repo/DesktopApp/403DesktopApp/403DesktopApp.csproj
```

Or in Visual Studio: open `403DesktopApp.sln`, **Build > Rebuild Solution**, then **F5**.

### Backend API

```bash
# Build
dotnet build 404Repo/Database/BioMetrixDatabase.sln

# Run (Swagger UI at http://localhost:5168/swagger)
dotnet run --project 404Repo/Database/BioMetrixDatabase/BioMetrixDatabase.csproj
```

API launch profiles:
- **HTTP:** `http://localhost:5168`
- **HTTPS:** `https://localhost:7183`

### Python Environment

```bash
# Create and activate a virtual environment (recommended)
python -m venv .venv
# Windows
.venv\Scripts\activate
# Linux/macOS
source .venv/bin/activate

# Install Python dependencies
pip install -r requirements.txt
```

The Python runtime must be discoverable by Python.NET. Set `PYTHONNET_PYDLL` if the runtime is not on `PATH`:
```bash
# Windows example
set PYTHONNET_PYDLL=C:\Python310\python310.dll
# Linux example
export PYTHONNET_PYDLL=/usr/lib/libpython3.10.so
```

## Key Dependencies

### Desktop App (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| fo-dicom | 5.2.4 | DICOM file parsing and image loading |
| fo-dicom.Codecs | 5.16.4 | DICOM codec support |
| fo-dicom.Desktop | 4.0.8 | Desktop DICOM rendering |
| System.Drawing.Common | 9.0.9 | Bitmap pixel operations |

### Backend API (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| Swashbuckle.AspNetCore | 6.6.2 | Swagger/OpenAPI docs |

### Python.NET Bridge (NuGet — to be added to Desktop App)

| Package | Version | Purpose |
|---------|---------|---------|
| pythonnet | 3.0.* | Embed CPython in .NET; call Python from C# |

### Python Dependencies (pip — `requirements.txt`)

| Package | Purpose |
|---------|---------|
| numpy | Array operations, image matrix manipulation |
| pydicom | Pure-Python DICOM parsing (complements fo-dicom on the Python side) |
| scikit-image | Image processing filters, segmentation, morphology |
| opencv-python | Computer vision primitives (optional, for advanced analysis) |
| matplotlib | Plotting/visualization if headless rendering is needed |
| *ML framework TBD* | Model inference (e.g. TensorFlow, PyTorch, ONNX Runtime) |

## Architecture

### Desktop App — MVVM Pattern

```
UI (XAML) ←→ ViewModel ←→ Services / Models
              ↑
         Data binding via INotifyPropertyChanged + RelayCommand
```

- **Views** (`MainWindow.xaml`, `LoginWindow.xaml`): XAML markup with minimal code-behind
- **ViewModels** (`MainViewModel`, `LoginViewModel`): All logic — implement `INotifyPropertyChanged`
- **Models** (`MedicalProvider`): Data classes
- **Services** (`AuthenticationService`, `PasswordHasher`): Business logic
- **Commands** (`RelayCommand`): Generic `ICommand` with execute/canExecute delegates

### Backend API — Standard ASP.NET Core

- Minimal hosting in `Program.cs`
- Controller-based routing (`[ApiController]`, `[Route("[controller]")]`)
- Swagger enabled in Development environment
- Currently contains only the default WeatherForecast template

## Python.NET Integration

### Overview

The desktop app will use [Python.NET (pythonnet)](https://github.com/pythonnet/pythonnet) to bridge C#/.NET and Python. This enables calling Python-based image analysis, ML inference, and scientific computing libraries directly from the WPF application without a separate process or REST API.

### Why Python.NET

- **In-process execution** — Python runs inside the .NET process via the CPython embedding API. No subprocess spawning, no IPC serialization overhead.
- **Direct object access** — C# code can import Python modules, call functions, and pass/receive NumPy arrays, DICOM pixel data, and ML model results as native Python objects.
- **Ecosystem leverage** — Gives access to the full Python scientific stack (NumPy, scikit-image, pydicom, TensorFlow/PyTorch) without rewriting algorithms in C#.
- **Single deployment** — The app ships as one WPF executable that initializes the Python runtime on startup.

### Architecture

```
┌─────────────────────────────────────────────────────────┐
│  WPF Desktop App (.NET 8)                               │
│                                                         │
│  XAML Views ←→ ViewModels ←→ Services                   │
│                                    │                    │
│                              PythonService               │
│                          (C# wrapper class)             │
│                                    │                    │
│                          ┌─────────┴─────────┐          │
│                          │   Python.NET       │          │
│                          │  (pythonnet 3.x)   │          │
│                          └─────────┬─────────┘          │
│                                    │                    │
│                  ┌─────────────────┼──────────────────┐ │
│                  │                 │                   │ │
│           analysis.py      ml_inference.py     preprocessing.py │
│          (scikit-image,    (TF/PyTorch/ONNX)   (NumPy, pydicom) │
│           segmentation)                                 │
└─────────────────────────────────────────────────────────┘
```

### Planned Directory Structure

```
404Repo/DesktopApp/403DesktopApp/
├── Services/
│   ├── AuthenticationServices.cs
│   └── PythonService.cs           # NEW — Python.NET runtime manager
├── PythonScripts/                 # NEW — Python modules called from C#
│   ├── __init__.py
│   ├── analysis.py                # Image analysis (filters, segmentation, measurements)
│   ├── preprocessing.py           # DICOM pixel preprocessing, normalization
│   └── ml_inference.py            # ML model loading and inference
└── ...
```

### Integration Pattern

**1. Runtime initialization** — `PythonService` initializes the Python engine once at app startup via `PythonEngine.Initialize()`. The GIL (Global Interpreter Lock) is managed per-call using `Py.GIL()`.

```csharp
// PythonService.cs — conceptual example
using Python.Runtime;

public class PythonService : IDisposable
{
    private static bool _initialized;

    public void Initialize(string pythonDll, string scriptsPath)
    {
        if (_initialized) return;
        Runtime.PythonDLL = pythonDll;           // path to python3x.dll/.so
        PythonEngine.Initialize();
        // Add PythonScripts/ to sys.path so modules are importable
        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.append(scriptsPath);
        }
        _initialized = true;
    }

    public void Dispose()
    {
        if (_initialized) PythonEngine.Shutdown();
    }
}
```

**2. Calling Python from C#** — Acquire the GIL, import the module, call the function, convert the result back to .NET types.

```csharp
public double[] AnalyzeImage(byte[] pixelData, int width, int height)
{
    using (Py.GIL())
    {
        dynamic np = Py.Import("numpy");
        dynamic analysis = Py.Import("analysis");

        // Convert pixel data to NumPy array
        PyObject npArray = np.frombuffer(pixelData, dtype: np.uint8)
                             .reshape(height, width);

        // Call Python analysis function
        dynamic result = analysis.compute_metrics(npArray);

        // Convert result back to C# types
        return result.As<double[]>();
    }
}
```

**3. Python module pattern** — Each `.py` file exposes pure functions that accept NumPy arrays and return serializable results.

```python
# analysis.py — conceptual example
import numpy as np
from skimage import filters, measure

def compute_metrics(image: np.ndarray) -> dict:
    """Compute basic image metrics on a 2D grayscale array."""
    return {
        "mean": float(np.mean(image)),
        "std": float(np.std(image)),
        "min": int(np.min(image)),
        "max": int(np.max(image)),
    }

def segment_regions(image: np.ndarray, threshold: float = None):
    """Threshold and label connected regions."""
    if threshold is None:
        threshold = filters.threshold_otsu(image)
    binary = image > threshold
    labels = measure.label(binary)
    return labels, measure.regionprops_table(labels, properties=["area", "centroid"])
```

### Key Conventions for Python.NET Code

| Rule | Details |
|------|---------|
| GIL management | Always wrap Python calls in `using (Py.GIL()) { ... }` — never hold the GIL longer than needed |
| Thread safety | Python.NET is single-threaded by default; run Python calls on a dedicated thread or use `await Task.Run(() => { using (Py.GIL()) { ... } })` to avoid blocking the UI thread |
| Error handling | Catch `PythonException` in C# to surface Python tracebacks; log but do not expose raw tracebacks to end users |
| Data marshalling | Pass pixel data as `byte[]` → NumPy via `np.frombuffer()`; return results as primitive types, lists, or dicts (avoid passing complex Python objects back to C#) |
| Module location | All `.py` files go in `PythonScripts/`; add this directory to `sys.path` at initialization |
| Python style | Follow PEP 8 in all `.py` files; use type hints; keep functions stateless and pure where possible |
| Dependency management | Pin Python package versions in `requirements.txt`; document the required Python version (3.10+) |
| Cleanup | Call `PythonEngine.Shutdown()` in the app's shutdown handler (`App.OnExit` or `PythonService.Dispose()`) |

### Setup Checklist (for contributors)

1. Install Python 3.10+ and ensure `python` / `python3` is on `PATH`
2. Create a virtual environment: `python -m venv .venv` and activate it
3. Install dependencies: `pip install -r requirements.txt`
4. Add the `pythonnet` NuGet package to `403DesktopApp.csproj`:
   ```xml
   <PackageReference Include="pythonnet" Version="3.0.*" />
   ```
5. Set `PYTHONNET_PYDLL` environment variable if needed (points to `python3xx.dll` / `libpython3.xx.so`)
6. Verify: build and run — `PythonService.Initialize()` should succeed without errors

## Code Conventions

| Rule | Example |
|------|---------|
| Namespace | `_403DesktopApp` (underscore prefix because name starts with digit) |
| Backend namespace | `BioMetrixDatabase` |
| Private fields | `_currentImagePath`, `_viewModel` (underscore prefix) |
| Properties | `CurrentPage`, `ZoomLevel` (PascalCase) |
| Methods | `Navigate()`, `OpenImage()` (PascalCase) |
| XAML element names | `DisplayImage`, `MarkupCanvas` (PascalCase) |
| Commands | `RelayCommand` for all user actions |
| Nullable types | Enabled project-wide |
| Implicit usings | Enabled project-wide |
| Unsafe code | Enabled in desktop app for bitmap pixel manipulation |

### Patterns to Follow

- Prefer ViewModel changes over code-behind when adding UI features
- Use XAML data binding — avoid direct UI manipulation from code-behind
- Use `RelayCommand` for new user actions
- Keep changes small and incremental
- PHI/DICOM data must be handled with encryption and access control in mind

## Authentication

Demo credentials for testing:

| Provider ID | Password | Name | Specialty |
|------------|----------|------|-----------|
| MD001 | demo123 | John Smith | Cardiology |
| MD002 | demo456 | Sarah Johnson | Pediatrics |
| NP001 | demo789 | Emily Davis | Family Medicine |

Implementation details:
- PBKDF2-SHA256 with 100,000 iterations and 16-byte salt
- Timing-safe comparison via `CryptographicOperations.FixedTimeEquals`
- Providers currently hardcoded in `AuthenticationService` (needs database for production)

## Application Flow

1. `App.OnStartup` sets `ShutdownMode.OnExplicitShutdown`
2. `LoginWindow` shown as modal dialog
3. On successful auth → `ShutdownMode.OnMainWindowClose`, `MainWindow` opens
4. On cancel/failure → `Application.Shutdown()`
5. `MainWindow` has tabbed UI: Home (dashboard), Image Viewer (DICOM)

### Image Viewer Features

- Open DICOM files (`.dcm`, `.dicom`) via file dialog
- Multi-frame DICOM support with frame navigation
- Zoom: Ctrl+MouseWheel (range 10%–1000%)
- Pan: click-and-drag on canvas
- Keyboard: Left/Right arrows for frames, Shift+Left/Right for first/last frame
- Pixel-level rendering via `WriteableBitmap` with unsafe pointer access

## Testing

**No automated test framework is configured.** Manual testing only:

1. Build both solutions
2. Run the desktop app and log in with demo credentials
3. Load a DICOM image via the Image Viewer tab
4. Test zoom (Ctrl+scroll), pan (click-drag), frame navigation (arrow keys)
5. For the API: use the `.http` file or Swagger UI at `http://localhost:5168/swagger`

## Known Issues

- `LoginWindow.xaml.cs` declares `partial class MainWindow` instead of `partial class LoginWindow` — class name mismatch
- `PasswordBox_PasswordChanged` event referenced in `LoginWindow.xaml` but handler not implemented in code-behind
- Authentication uses hardcoded demo data (production needs database integration)
- No unit tests, integration tests, or CI/CD pipeline
- `Views/` folder is empty (reserved for future UserControls)
- Backend API is still the default template — no real endpoints implemented
- Python.NET integration not yet implemented — `PythonService`, `PythonScripts/`, and `requirements.txt` still need to be created
- No `requirements.txt` or `.venv` setup yet for Python dependencies

## Git Workflow

- Feature branches off `master` with descriptive names
- Short-lived branches; merge via PR
- Commit messages: imperative mood, category-prefixed (`Fix:`, `Add:`, `Update:`)
- Build must pass before opening a PR
- No CI/CD pipeline configured yet

## Security Considerations

- DICOM files may contain PHI (Protected Health Information) — handle with encryption and access control
- Password hashing uses industry-standard PBKDF2 with OWASP-recommended iterations
- Timing-safe hash comparison prevents timing attacks
- No logging framework configured — avoid logging sensitive data
- Backend API has no authentication/authorization middleware yet
