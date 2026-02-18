# CLAUDE.md — 403DesktopApp (Desktop Client)

> For the full repository guide (both Desktop and API components), see [../../CLAUDE.md](../../CLAUDE.md).

## Project Overview

WPF desktop client for medical imaging with DICOM support and provider authentication.

- **Language:** C# on .NET 8
- **Target:** `net8.0-windows` (Windows-only)
- **UI Framework:** WPF
- **Architecture:** MVVM (Model-View-ViewModel)
- **Solution:** `403DesktopApp.sln` → single project `403DesktopApp/403DesktopApp.csproj`

## Directory Structure

```
DesktopApp/
├── CLAUDE.md                          # This file
├── copilot-instructions.md            # AI assistant behavior guide
├── 403DesktopApp.sln
└── 403DesktopApp/
    ├── 403DesktopApp.csproj           # Project file (WPF, .NET 8, unsafe enabled)
    ├── App.xaml / App.xaml.cs         # Entry point: LoginWindow → MainWindow
    ├── MainWindow.xaml / .xaml.cs     # Tabbed UI: Home, Image Viewer
    ├── LoginWindow.xaml / .xaml.cs    # Login dialog (modal)
    ├── AssemblyInfo.cs
    ├── Commands/
    │   └── RelayCommand.cs            # Generic ICommand with execute/canExecute
    ├── Models/
    │   └── MedicalProvider.cs         # Provider data model (Id, Name, Specialty, Hash)
    ├── Services/
    │   └── AuthenticationServices.cs  # AuthenticationService + PasswordHasher classes
    ├── ViewModels/
    │   ├── MainViewModel.cs           # DICOM viewer: load, zoom, pan, frame navigation
    │   └── LoginViewModel.cs          # Login validation and auth flow
    └── Views/                         # Empty — reserved for future UserControls
```

## Build & Run

**Prerequisites:** .NET 8 SDK (or Visual Studio 2022 with .NET 8 workload), Windows OS.

```bash
# Build
dotnet build 403DesktopApp.sln

# Run
dotnet run --project 403DesktopApp/403DesktopApp.csproj
```

Or in Visual Studio: **Build > Rebuild Solution**, then **F5**.

## Dependencies (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| fo-dicom | 5.2.4 | DICOM file parsing and image loading |
| fo-dicom.Codecs | 5.16.4 | DICOM codec support |
| fo-dicom.Desktop | 4.0.8 | Desktop DICOM rendering |
| System.Drawing.Common | 9.0.9 | Bitmap pixel operations |

## Application Flow

1. `App.OnStartup` sets `ShutdownMode.OnExplicitShutdown`
2. `LoginWindow` shown as modal dialog with `LoginViewModel`
3. On successful auth → `ShutdownMode.OnMainWindowClose`, `MainWindow` opens
4. On cancel/failure → `Application.Shutdown()`
5. `MainWindow` has tabbed interface: Home (dashboard), Image Viewer (DICOM)

## Code Conventions

- **Namespace:** `_403DesktopApp` (underscore prefix required — name starts with digit)
- **Private fields:** underscore prefix (`_currentImagePath`, `_viewModel`)
- **Properties/Methods:** PascalCase (`CurrentPage`, `OpenImage()`)
- **XAML element names:** PascalCase (`DisplayImage`, `MarkupCanvas`)
- **MVVM:** UI binds to ViewModel properties and commands; minimize code-behind
- **Commands:** Use `RelayCommand` (in `Commands/`) for all user actions
- **Nullable types:** Enabled project-wide
- **Implicit usings:** Enabled project-wide
- **Unsafe code:** Enabled for bitmap pixel manipulation in `MainViewModel.LoadCurrentFrame()`

## MVVM Architecture

```
XAML Views ←→ ViewModels ←→ Services / Models
                 ↑
    INotifyPropertyChanged + RelayCommand
```

- **Views** bind to ViewModel properties via `{Binding}` and commands via `{Binding Command}`
- **ViewModels** own all logic; code-behind limited to UI-only concerns (mouse/keyboard handlers)
- **Services** encapsulate business logic (authentication, password hashing)
- **RelayCommand** wraps `Action<object>` execute and `Func<object, bool>` canExecute

### Key ViewModel Commands (MainViewModel)

| Command | Action | Shortcut |
|---------|--------|----------|
| `OpenImageCommand` | Open DICOM file dialog | — |
| `ClearImageCommand` | Clear loaded image | — |
| `ZoomInCommand` / `ZoomOutCommand` | Zoom ×1.2 / ÷1.2 | Ctrl+Scroll |
| `FitToScreenCommand` | Reset zoom to 100% | — |
| `NextFrameCommand` / `PreviousFrameCommand` | Navigate frames | Right/Left arrow |
| `FirstFrameCommand` / `LastFrameCommand` | Jump to first/last frame | Shift+Left/Right |
| `NavigateCommand` | Switch tabs | — |

## Authentication

Demo credentials for testing:

| Provider ID | Password | Name | Specialty |
|------------|----------|------|-----------|
| MD001 | demo123 | John Smith | Cardiology |
| MD002 | demo456 | Sarah Johnson | Pediatrics |
| NP001 | demo789 | Emily Davis | Family Medicine |

**Implementation:** PBKDF2-SHA256, 100,000 iterations, 16-byte salt, timing-safe comparison via `CryptographicOperations.FixedTimeEquals`. Providers are hardcoded in `AuthenticationService` (needs database for production).

## Testing

No automated test framework. Manual testing flow:

1. Build the solution
2. Run the app
3. Log in with demo credentials above
4. Load a DICOM image (`.dcm` / `.dicom`) via Image Viewer tab
5. Test zoom (Ctrl+mouse wheel), pan (click-drag), frame navigation (arrow keys)

## Key Patterns to Follow

- Prefer ViewModel changes over code-behind when adding features
- Use XAML data binding — avoid direct UI manipulation from code-behind
- Use `RelayCommand` for new user-triggered actions
- Keep changes small and incremental
- PHI/DICOM data must be handled with encryption and access control in mind
- No logging framework configured — keep console output minimal

## Known Issues

- `LoginWindow.xaml.cs` declares `partial class MainWindow` instead of `partial class LoginWindow`
- `PasswordBox_PasswordChanged` event referenced in `LoginWindow.xaml` but handler not implemented in code-behind
- Authentication uses hardcoded demo data (needs database integration)
- No unit tests or CI/CD pipeline
- `Views/` folder is empty (reserved for future UserControls)
