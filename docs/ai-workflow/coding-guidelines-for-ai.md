# Coding guidelines for AI agents (the rules a human has to judge)

Anything that `.editorconfig`, `Directory.Build.props`, or `xamlstyler.json` can decide mechanically does **not** belong here. We do not ask AI agents to do the linter's job — let `dotnet format` and XAML Styler handle it.

This page only lists rules that need human judgment.

## Architecture

- **MIT/GPL boundary**: `Beutl.FFmpegWorker` is a GPL process. Do not reference it directly from MIT projects. Full details in [gpl-mit-boundary.md](./gpl-mit-boundary.md).
- **Source generators**: `Beutl.Engine.SourceGenerators` is built on `IIncrementalGenerator`. Signature changes invalidate the cache key and ripple downstream — always run `/beutl-build` to confirm the build is still green after touching a generator.
- **Module boundaries**:
  - `Beutl.Engine` (rendering / scene / track) has no project dependencies
  - `Beutl.ProjectSystem` owns project persistence
  - `Beutl.Editor` / `Beutl.Editor.Components` / `Beutl.Controls` form the Avalonia UI layer
  - `Beutl.Extensibility` is the plugin abstraction

## UI / XAML

- **Split out behavior**: when an event handler in a UserControl gets complicated, factor it into an Avalonia `Behavior` class or a `partial` code-behind file. A code-behind file pushing 200 lines is a warning sign.
- **Compiled bindings are required**: every new/changed XAML file declares `x:CompileBindings="True"` and `x:DataType="..."` on the root (see `.claude/rules/xaml.md`).

## Tests

- **NUnit + Moq** under `tests/`. Tests are split across per-area projects (e.g. `tests/Beutl.UnitTests/`, `tests/Beutl.Engine.Tests/`, `tests/SourceGeneratorTest/`, `tests/Beutl.FFmpegIpc.Tests/`); add new tests to the one that matches the code you changed. Do not introduce another test framework.
- **New logic ships with a test.** A PR without a corresponding test is incomplete.
- **`isolation: worktree`** — the `beutl-test-runner` subagent lets you try fixes against a worktree copy of the repo without polluting your real branch.

## Commit convention

Follow Conventional Commits, as used in the existing history (`git log --oneline`):

- `fix: ...` — bug fix
- `feat: ...` — new feature
- `refactor: ...` — behavior-preserving refactor
- `docs: ...` — documentation

## Things not to do

- Do not ask the AI to fix style issues that `.editorconfig` / `xamlstyler.json` already control (that is the linter's job).
- Do not change existing CI workflows under `.github/workflows/*` without explicit approval.
- Do not blur `Beutl.FFmpegWorker`'s independence (GPL boundary).
- Do not run `git push --force origin main` (the hook denies it).
