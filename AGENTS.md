# AGENTS.md — 知识薄弱分析 (Knowledge Weakness Analysis)

Avalonia (.NET 10) desktop app: import corrected student papers, extract questions via GLM vision model, summarize weak knowledge points. Solution: `KnowledgeWeakness.slnx`.

## Commands

- Build: `dotnet build KnowledgeWeakness.slnx`
- Test: `dotnet test KnowledgeWeakness.slnx` (xUnit + FluentAssertions; keep 0 warnings / all green)
- Run: `dotnet run --project src/App/KnowledgeWeakness.App.csproj`
- No launchSettings.json, no Directory.Build.props, no lint config. Plain `dotnet` on PATH — do NOT add PATH/env scripts.

## Layout & Layering (strict)

- `src/Core` — domain models, repository + AI interfaces, weakness analysis. References **nothing**.
- `src/Infrastructure` — EF Core SQLite persistence, GLM AI providers, image preprocessing. References Core only.
- `src/App` — Avalonia UI (Views `.axaml` + ViewModels, CommunityToolkit.Mvvm). References Core + Infrastructure.
- `tests/Tests` — flat xUnit test files.
- DI: composition root in `src/App/App.axaml.cs`; infra services in `src/Infrastructure/InfrastructureServiceCollectionExtensions.cs` (`AddInfrastructure(connectionString)`).
- `ViewLocator.cs` maps `XxxViewModel` → `XxxView` by name.

## Gotchas

- **Never block on async from the UI thread** (no `.Result`/`.GetAwaiter().GetResult()`). A past deadlock (fixed in 49fd451) came from sync-over-async settings reads; that's why `IVisionModelFactory.CreateAsync` is async — keep it that way.
- **Wrap CPU-bound sync work in `await Task.Run(...)`** (PDF export, backup staging) so the Avalonia window doesn't freeze.
- **Do not remove the SQLitePCLRaw pin to 2.1.12** in `src/Infrastructure/KnowledgeWeakness.Infrastructure.csproj` — it overrides EF's transitively vulnerable 2.1.11 (CVE-2025-6965).
- **New EF entities need a `SchemaUpgrader` patch** (`src/Infrastructure/Persistence/SchemaUpgrader.cs`) — `EnsureCreated()` is a no-op on existing DBs.
- GLM `HttpClient` uses `Timeout.InfiniteTimeSpan`; per-call cancellation via linked CTS with 600 s timeout. Parse GLM JSON defensively (`TryGetProperty` / length checks — see `GlmResponseHelper`, `VisionJsonParser`).
- Pending backup restores are applied at startup **before** the DbContext factory is built (`App.axaml.cs`) — don't reorder.
- API keys are DPAPI-encrypted via `SettingsRepository.GetSecretAsync/SetSecretAsync` — never store plaintext in the DB or logs.
- DB lives in `%LocalApplicationData%` (`src/App/Services/AppPaths.cs`); `knowledge-bases/` is read from `AppContext.BaseDirectory` at runtime.

## Repo Hygiene

- Root folders `.dotnet/ .nuget/ .appdata/ .tmpobj/ .tmpbin/ AppData/ .zcode/ .claude/` are local toolchain sandboxes / IDE state — gitignored, never commit or edit.
- `*.db*`, `logs/`, `docs/*.{jpg,png,...}` (student paper images) and secret files are gitignored.

## Conventions

- Chinese for UI strings, user-facing messages, and commit messages; English for code comments explaining rationale.
- CommunityToolkit.Mvvm source generators: `[ObservableProperty]` on `_camelCase` fields, `[RelayCommand]`; `ObservableCollection<T>` for bound lists.
- File-scoped namespaces; C# 12+ features fine (primary constructors, collection expressions, raw strings).
- Tests use FluentAssertions (`subject.Should()...`); XML doc comments record regression contracts.
- Avalonia compiled bindings enabled by default in App (`x:DataType` required).
