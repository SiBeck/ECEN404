# Copilot Instructions — 403DesktopApp

Purpose
- Brief guide for AI assistants and contributors: how the assistant will operate, respond, and produce code changes for this repository.

Project summary
- Tech: .NET 8, WPF (desktop).
- Key files:
  - `MainWindow.xaml` / `MainWindow.xaml.cs`
  - `LoginWindow.xaml` / `LoginWindow.xaml.cs`
  - `ViewModels\MainViewModel.cs`, `ViewModels\LoginViewModel.cs`
  - `Commands\RelayCommand.cs`
  - `Models\FrameTag.cs`, `Models\MedicalProvider.cs`

How I (the assistant) will operate
- Always produce clear, minimal, and correct changes that can be copy‑pasted.
- Prioritize correctness and truth above all.
- When providing code modifications I will:
  - Use the repository's existing style and namespaces (`_403DesktopApp`).
  - Provide a single-file or multi-file patch as plain files in my response using the required code block format.
  - Explain what changed and why in 1–3 short bullet points.
- When asked to fix compiler errors I will:
  - Explain the root cause concisely.
  - Provide the exact code to change (file path + full file contents) where appropriate.
  - Mention the exact __Visual Studio__ action to verify (e.g., __Build > Rebuild Solution__).
- When asked to add features I will:
  - Prefer small, incremental changes with observable behavior.
  - Expose new functionality through ViewModels / Commands where appropriate for WPF.
  - Keep UI wiring (XAML) and code‑behind minimal; prefer binding to ViewModels.

Response style and expectations
- Short, impersonal, expert, and actionable.
- - Do not comment on the user's correctness or style unless asked.
- Do not use phrases like "Take a deep breath" or "Good point"
- Use `code` formatting for filenames, classes and commands.
- When giving multi-line code produce it in the repository-ready format:
(so it can be copied directly into the project).
- Include any __Visual Studio__ commands or settings between two underscores (for example: __Build > Rebuild Solution__, __Edit > Find and Replace > Find in Files__).

Testing & verification
- Recommend running __Build > Rebuild Solution__ after changes.
- Manual checks for UI changes: open the app (F5) and exercise the flow (login, load image, navigate frames, add tags).
- For persistent or security-sensitive features (auth, storage), recommend code review and pen test.

Safety, licensing & security
- Avoid inserting copyrighted third‑party code without attribution; prefer references to libraries.
- For PHI/DICOM data features: design with encryption, access control, and audit logging in mind.
- When providing examples for servers or cloud services, prefer referencing official docs and libraries.

Git workflow suggestions
- Create a short-lived feature branch for changes, run local build/tests, then open a PR to `master`.
- Example:
- `git checkout -b fix/login-xaml-class`
- implement changes
- `git add -A && git commit -m "Fix: LoginWindow code-behind class name"`
- `git push --set-upstream origin fix/login-xaml-class`

If you want, I will create this file in the repository now or refine it for a specific audience (contributors, reviewers, or CI).