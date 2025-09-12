# Repository Guidelines

## Project Structure & Module Organization
- WPF (.NET 9) MVVM app. Core code at repo root: `Models/`, `ViewModels/`, `Views/`, `Services/`, `Infrastructure/`, `Utilities/`, `Controls/`, `Converters/`, `Themes/`.
- Tests live in `Testing/` (separate MSTest project).
- Assets and docs: `ffxi-manager.ico`, `Docs/`, `Documentation/`.
- Solution: `FFXIManager.sln`; app project: `FFXIManager.csproj`.

## Build, Test, and Development Commands
- Restore: `dotnet restore`
- Build: `dotnet build FFXIManager.sln -c Debug` (or `Release`)
- Run (WPF): `dotnet run --project FFXIManager.csproj -c Debug`
- Test: `dotnet test Testing/FFXIManager.Tests.csproj -c Debug`
- Publish: `dotnet publish FFXIManager.csproj -c Release -o .\publish`

## Coding Style & Naming Conventions
- Follow `.editorconfig`: 4-space indent, UTF-8 BOM, CRLF, nullable enabled, file-scoped namespaces, analyzers on.
- Var usage: explicit types except when type is apparent.
- Naming: PascalCase for public members; ViewModels end with `ViewModel`; Views are XAML windows/pages named to match (e.g., `MainWindow.xaml`).
- Keep MVVM boundaries: UI in `Views`, logic in `ViewModels`, app/services in `Services`/`Infrastructure`.

## Testing Guidelines
- Framework: MSTest (`MSTest.TestFramework`, `Microsoft.NET.Test.Sdk`).
- Location: under `Testing/` mirroring src folders (e.g., `Testing/Services/*Tests.cs`).
- Naming: `{UnitUnderTest}_{Condition}_{Expected}()` inside `[TestClass]` with `[TestMethod]`.
- Run locally with `dotnet test`; aim for coverage on services, utilities, and infrastructure.

## Commit & Pull Request Guidelines
- Use Conventional Commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
- PRs: include a clear description, link issues (`Closes #123`), and screenshots/GIFs for UI changes.
- Target `master` unless coordinating long‑running work; keep PRs small and focused; ensure tests pass.
- Versioning and releases are handled by CI; do not manually bump versions in PRs.

## Security & Configuration Tips
- Strong-name signing is enabled (`ffximanager.snk`); do not change or commit alternate keys.
- Do not commit secrets or machine-specific paths. Prefer app/manifests and configuration in code.

## Agent-Specific Notes
- Respect this file’s guidance across the repo. Align with existing folder layout and `.editorconfig` rules. Avoid broad refactors; prefer minimal, targeted changes with tests.

