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

## Build & Run

**Prerequisites:** .NET 8 SDK. The desktop app requires Windows.

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
