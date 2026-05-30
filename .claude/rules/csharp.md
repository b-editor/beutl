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
  - The top-level `src/Beutl/` app project is not referenced by any test project, so logic that needs tests belongs in a referenced library (e.g. `Beutl.Editor`, `Beutl.Engine`) — not in `src/Beutl/` ViewModels/Views.
- Source generators live in `src/Beutl.Engine.SourceGenerators/`; if you change them, run `/beutl-build` and verify downstream projects still resolve generated symbols.
- Style nits (spacing, ordering, var vs explicit) are owned by `.editorconfig` and `dotnet format`. Don't argue with the linter.
