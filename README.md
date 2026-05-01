# Knowledge Weakness Analysis

Avalonia desktop app for importing corrected student papers, extracting question data with a vision model, and summarizing weak knowledge points from wrong or partially-scored questions.

## Projects

- `src/Core`: domain models, abstractions, and weakness analysis logic.
- `src/Infrastructure`: SQLite persistence, repositories, image preprocessing, and GLM vision integration.
- `src/App`: Avalonia desktop UI.
- `tests/Tests`: xUnit tests.

## Build And Test

```powershell
dotnet test KnowledgeWeakness.slnx
dotnet build KnowledgeWeakness.slnx --no-restore
```
