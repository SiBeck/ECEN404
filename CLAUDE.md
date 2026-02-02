# CLAUDE.md — ECEN404 Medical Imaging System

## Repository Overview

This repository contains two .NET 8 C# projects for a medical imaging system (BioMetrix):

1. **DesktopApp** (`404Repo/DesktopApp/`) — WPF desktop application for DICOM image viewing with provider authentication
2. **Database** (`404Repo/Database/`) — ASP.NET Core Web API backend (currently scaffold/placeholder)

## Repository Structure

```
ECEN404/
├── CLAUDE.md                          # This file (root documentation)
└── 404Repo/
    ├── DesktopApp/                    # WPF Desktop Application
    │   ├── 403DesktopApp.sln
    │   ├── CLAUDE.md                  # DesktopApp-specific documentation
    │   ├── copilot-instructions.md
    │   └── 403DesktopApp/
    │       ├── 403DesktopApp.csproj   # .NET 8, WPF, Windows-only
    │       ├── App.xaml(.cs)          # Entry point: LoginWindow → MainWindow
    │       ├── MainWindow.xaml(.cs)   # Tabbed UI (Home, Image Viewer)
    │       ├── LoginWindow.xaml(.cs)  # Login dialog
    │       ├── Commands/
    │       │   └── RelayCommand.cs    # ICommand implementation for MVVM
    │       ├── Models/
    │       │   └── MedicalProvider.cs # Provider data model
    │       ├── Services/
    │       │   └── AuthenticationServices.cs  # PBKDF2-SHA256 auth
    │       ├── ViewModels/
    │       │   ├── LoginViewModel.cs  # Login form logic
    │       │   └── MainViewModel.cs   # Image viewer + navigation
    │       └── Views/                 # Empty (reserved for UserControls)
    │
    └── Database/                      # ASP.NET Core Web API
        ├── BioMetrixDatabase.sln
        └── BioMetrixDatabase/
            ├── BioMetrixDatabase.csproj  # .NET 8, cross-platform
            ├── Program.cs                # API config (Swagger, controllers)
            ├── appsettings.json
            ├── BioMetrixDatabase.http    # REST Client test file
            ├── WeatherForecast.cs        # Placeholder model
            ├── Controllers/
            │   └── WeatherForecastController.cs  # Placeholder endpoint
            └── Properties/
                └── launchSettings.json   # HTTP:5168, HTTPS:7183

```

## Build & Run

**Prerequisites:** .NET 8 SDK, Windows OS (for DesktopApp), Visual Studio 2022 recommended.

```bash
# DesktopApp — build and run
dotnet build 404Repo/DesktopApp/403DesktopApp.sln
dotnet run --project 404Repo/DesktopApp/403DesktopApp/403DesktopApp.csproj

# Database API — build and run
dotnet build 404Repo/Database/BioMetrixDatabase.sln
dotnet run --project 404Repo/Database/BioMetrixDatabase/BioMetrixDatabase.csproj
# API available at http://localhost:5168 with Swagger UI
```

## Technology Stack

| Component | Target | Framework | Key Libraries |
|-----------|--------|-----------|---------------|
| DesktopApp | `net8.0-windows` | WPF | fo-dicom 5.2.4, System.Drawing.Common 9.0.9 |
| Database | `net8.0` | ASP.NET Core | Swashbuckle (Swagger) |

Both projects enable `Nullable` reference types and `ImplicitUsings`.

## Architecture & Patterns

### DesktopApp — MVVM

- **Models:** Plain data classes (`MedicalProvider`)
- **ViewModels:** Implement `INotifyPropertyChanged`; expose commands via `RelayCommand`
- **Views:** XAML with data binding; code-behind only for low-level UI events (mouse/keyboard)
- **Services:** `AuthenticationService` with PBKDF2-SHA256 hashing (100k iterations, timing-safe comparison)
- Unsafe code enabled (`AllowUnsafeBlocks`) for bitmap pixel manipulation

### Database — ASP.NET Core Web API

- Standard controller-based API with Swagger/OpenAPI
- Currently contains only the default WeatherForecast placeholder
- Uses built-in ASP.NET Core dependency injection

## Code Conventions

- **Namespace:** `_403DesktopApp` (underscore prefix — name starts with digit)
- **Private fields:** `_camelCase` with underscore prefix
- **Classes/methods:** PascalCase
- **XAML elements:** PascalCase (`DisplayImage`, `MarkupCanvas`)
- **Commands:** Always use `RelayCommand` for user actions
- **Commit messages:** Imperative mood, category prefix (`Fix:`, `Add:`, `Update:`)

## Authentication (Demo)

| Provider ID | Password |
|-------------|----------|
| MD001 | demo123 |
| MD002 | demo456 |
| NP001 | demo789 |

Hardcoded in `AuthenticationService` — needs database integration for production.

## Testing

No automated tests or CI/CD pipeline exists. Manual testing:

1. Build solution
2. Run app, log in with demo credentials
3. Load DICOM image via Image Viewer tab
4. Test zoom (mouse wheel), pan (click-drag), frame navigation (arrow keys)

## Known Issues

1. `LoginWindow.xaml.cs` declares `partial class MainWindow` instead of `LoginWindow`
2. `PasswordBox_PasswordChanged` event referenced in XAML but handler not implemented
3. Authentication uses hardcoded demo data only
4. No unit tests, integration tests, or CI pipeline
5. Views folder is empty (reserved for future UserControls)
6. Database project is scaffold-only (WeatherForecast placeholder)
7. No logging framework configured

## Guidelines for AI Assistants

- **Read before modifying** — always read files before suggesting changes
- **Prefer ViewModel changes** over code-behind additions
- **Use data binding** — avoid direct UI manipulation from code-behind
- **Keep changes minimal** — small, incremental, focused patches
- **Security-conscious** — PHI/DICOM data requires encryption and access control
- **Do not add** unnecessary abstractions, documentation files, or refactoring beyond what's requested
- **Windows-only** — DesktopApp targets Windows; do not introduce cross-platform assumptions
- **Match existing style** — follow the underscore-prefix private fields, PascalCase methods, MVVM separation
