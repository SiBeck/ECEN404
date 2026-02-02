# CLAUDE.md — 403DesktopApp

> See also: [Root CLAUDE.md](../../CLAUDE.md) for full repository overview including the Database API project.

## Project Overview

Medical imaging desktop application with DICOM support and provider authentication.

- **Language:** C# on .NET 8
- **UI Framework:** WPF (Windows-only)
- **Architecture:** MVVM (Model-View-ViewModel)
- **Solution:** `403DesktopApp.sln` containing one project `403DesktopApp/403DesktopApp.csproj`

## Directory Structure

```
403DesktopApp/
├── 403DesktopApp/              # Main project
│   ├── App.xaml(.cs)           # Entry point; shows LoginWindow then MainWindow
│   ├── MainWindow.xaml(.cs)    # Tabbed UI: Home, Image Viewer
│   ├── LoginWindow.xaml(.cs)   # Login dialog
│   ├── Commands/
│   │   └── RelayCommand.cs     # ICommand implementation
│   ├── Models/
│   │   └── MedicalProvider.cs  # Provider data model
│   ├── Services/
│   │   └── AuthenticationServices.cs  # Auth + PBKDF2 password hashing
│   ├── ViewModels/
│   │   ├── MainViewModel.cs    # Image viewer logic, navigation
│   │   └── LoginViewModel.cs   # Login logic
│   └── Views/                  # (empty, reserved for future UserControls)
├── 403DesktopApp.sln
└── copilot-instructions.md
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

## Key Dependencies (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| fo-dicom | 5.2.4 | DICOM image loading/parsing |
| fo-dicom.Codecs | 5.16.4 | DICOM codec support |
| fo-dicom.Desktop | 4.0.8 | Desktop DICOM rendering |
| System.Drawing.Common | 9.0.9 | Bitmap operations |

## Code Conventions

- **Namespace:** `_403DesktopApp` (underscore prefix because name starts with a digit)
- **Private fields:** underscore prefix (`_currentImagePath`)
- **Classes/methods:** PascalCase
- **XAML elements:** PascalCase names (`DisplayImage`, `MarkupCanvas`)
- **MVVM pattern:** UI binds to ViewModel properties and commands; minimize code-behind
- **Commands:** Use `RelayCommand` (in `Commands/`) for all user actions
- **Unsafe code** is enabled for bitmap pixel manipulation in the image viewer

## Authentication

Demo credentials for testing:

| Provider ID | Password |
|------------|----------|
| MD001 | demo123 |
| MD002 | demo456 |
| NP001 | demo789 |

Passwords are hashed with PBKDF2-SHA256 (100k iterations, 16-byte salt) and compared with timing-safe equality.

## Testing

No automated test project exists. Manual testing flow:

1. Build the solution
2. Run the app
3. Log in with demo credentials above
4. Load a DICOM image via the Image Viewer tab
5. Test zoom (mouse wheel), pan (click-drag), frame navigation (arrow keys, Shift+arrow for first/last)

## Key Patterns to Follow

- **Prefer ViewModel changes** over code-behind when adding features
- **Use data binding** — avoid direct UI manipulation from code-behind
- **Keep changes minimal** — small, incremental, copy-paste-ready patches
- **Security:** PHI/DICOM data must be handled with encryption and access control in mind
- **No logging framework** is configured yet; keep console output minimal

## Known Issues

- `LoginWindow.xaml.cs` defines `partial class MainWindow` instead of `LoginWindow`
- `PasswordBox_PasswordChanged` event referenced in XAML but not implemented in code-behind
- Authentication uses hardcoded demo data (needs database for production)
- No unit tests or CI pipeline
- Views folder is empty

## Git Workflow

- Feature branches off `master`, short-lived, with descriptive names
- Commit messages: imperative mood, prefixed with category (`Fix:`, `Add:`, `Update:`)
- Build must pass before opening a PR
