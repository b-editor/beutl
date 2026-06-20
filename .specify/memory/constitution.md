# Beutl Constitution

This constitution captures the immutable principles that any new feature work in Beutl must respect. It is loaded by every `/speckit-*` command in the Spec-Driven Development flow.

## Core Principles

### I. License Firewall (NON-NEGOTIABLE)

Beutl ships as two binaries to keep its license posture clean:

- The main application and all extensions ship under **MIT**.
- `Beutl.FFmpegWorker` is a separate executable that links GPL-3.0-or-later FFmpeg.
- MIT projects MUST NOT take a `ProjectReference` to `Beutl.FFmpegWorker`.
- All communication between MIT code and the worker happens through `Beutl.FFmpegIpc` (pipes + length-prefixed JSON + shared memory).
- Code sharing across the boundary uses `<Compile Include="..." Link="..." />`, never a `ProjectReference`.

Pull requests that blur the boundary are rejected. The PreToolUse hook in `.claude/hooks/check-gpl-mit-boundary.sh` enforces this mechanically; do not work around it.

### II. Dual-Target Framework

Beutl targets `net10.0` and `net10.0-windows`. Both targets must keep building. Adding a new TFM requires a written justification in the spec and a corresponding update to `Directory.Build.props`.

### III. Test-First with NUnit

- Test framework is NUnit + Moq. Tests are organized under `tests/` in per-area projects (e.g. `tests/Beutl.UnitTests/`, `tests/SourceGeneratorTest/`, `tests/Beutl.FFmpegIpc.Tests/`). `tests/Beutl.Graphics3DTests/` is an executable visual harness, not an NUnit project.
- New logic in `src/` is incomplete without an accompanying test.
- Benchmarks (`tests/Beutl.Benchmarks`, `tests/Beutl.FFmpegBenchmarks`) use BenchmarkDotNet and are excluded from regular `dotnet test` runs.
- CI quality gate: `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings` must pass, with the coverage threshold configured in [`.github/workflows/dotnet.yml`](../../.github/workflows/dotnet.yml) honored.

### IV. Avalonia + Compiled Bindings

- UI is built with Avalonia using ViewModels.
- Every UserControl declares `x:CompileBindings="True"` and `x:DataType="..."`.
- `ReflectionBinding` is for legacy code only.
- XAML style is enforced by `xamlstyler.json` and `.editorconfig`. Humans do not hand-format XAML.

### V. Style Belongs to the Linter

- C# style is set by `.editorconfig` and `Directory.Build.props` (file-scoped namespaces, `_camelCase`, `s_camelCase`, `Nullable: enable`, `LangVersion: preview`).
- XAML style is set by `xamlstyler.json`.
- `dotnet format` is the source of truth. AI agents must not propose stylistic-only edits.

### VI. Source Generators Are Load-Bearing

- `src/Beutl.Engine.SourceGenerators/` produces code that `Beutl.Engine` and downstream projects consume at compile time.
- Generators are `IIncrementalGenerator`-based; signature changes invalidate the incremental cache and ripple downstream.
- Generator changes require `/beutl-build` to pass before review, and `tests/SourceGeneratorTest/` should follow.

## Quality Gates

Every PR must pass these before merging:

1. `dotnet format Beutl.slnx --verify-no-changes` (enforced by `.github/workflows/format-check.yml`)
2. `dotnet build Beutl.slnx` (enforced by `.github/workflows/dotnet.yml`)
3. `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings` (enforced by `.github/workflows/dotnet.yml`)
4. Coverage threshold (set in [`.github/workflows/dotnet.yml`](../../.github/workflows/dotnet.yml)) not regressed
5. CI Claude Code review (`.github/workflows/claude-code-review.yml`) addressed or explicitly waived
6. No new TODO comments left orphaned (`.github/workflows/todo-comments.yml` surfaces them on PRs)

## Development Workflow

- **Conventional Commits**: `fix:`, `feat:`, `refactor:`, `docs:` etc.
- **Branch model**: feature branches off `main`, rebased before merging.
- **Large features**: drive the spec from `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`. Spec lives under `docs/specs/<feature>/`.
- **Small fixes/refactors**: skip Spec-Kit, ship a focused PR.
- **PR reviews**: `claude-code-review.yml` posts an AI review automatically; the daily `scheduled-code-review.yml` also files Draft items into GitHub Projects v2.

## Governance

This constitution overrides any conflicting guidance from skills, agents, or hooks. Amendments require:

1. A PR that updates this file and the related docs (`docs/ai-workflow/`, `AGENTS.md`).
2. Maintainer review.
3. Bumping the version below and noting the amendment date.

**Version**: 0.1.0 | **Ratified**: 2026-05-18 | **Last Amended**: 2026-05-18
