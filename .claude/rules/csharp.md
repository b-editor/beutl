---
paths:
  - "**/*.cs"
---

# C# rules (Beutl)

Language and project shape are set by `Directory.Build.props`:

- `LangVersion: preview`, `Nullable: enable`, `ImplicitUsings: enable`.
- Target frameworks: `net10.0` and `net10.0-windows` (do not introduce additional TFMs without explicit reason).
- File-scoped namespaces are the norm; do not introduce block-scoped namespaces in new files.

Naming (matches `.editorconfig`):

- `static` fields → `s_camelCase`.
- `private` instance fields → `_camelCase`.
- `const` fields → `PascalCase`.
- Public members → `PascalCase`.

Other:

- Expression-bodied members are preferred when the body is a single statement.
- New logic without a corresponding test under `tests/` (NUnit + Moq, in the matching test project) is incomplete.
  - The top-level `src/Beutl/` app project is referenced by exactly one test project: the shell E2E suite `tests/Beutl.HeadlessUITests/`, which drives the real application shell headlessly (Avalonia headless). For ordinary unit/logic tests, keep the logic in a referenced library (e.g. `Beutl.Editor`, `Beutl.Engine`) and test it there — do not reach into `src/Beutl/` ViewModels/Views from a unit test. Add an E2E test only when the behaviour genuinely lives in the shell orchestration (`ProjectService`/`EditorService`/`MainViewModel`/`EditViewModel`).
- Source generators live in `src/Beutl.Engine.SourceGenerators/`; if you change them, run `/beutl-build` and verify downstream projects still resolve generated symbols.
- Style nits (spacing, ordering, var vs explicit) are owned by `.editorconfig` and `dotnet format`. Don't argue with the linter.
