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

## Design priorities — adopt better designs eagerly

Beutl's policy: if there is a clearly better design, we want to adopt it, and backward compatibility is a cost to weigh against that improvement — not a default to preserve. When the cleaner design wins on the merits, take it and migrate the call sites in the same change. The full statement lives in [`AGENTS.md` → "Design priorities"](../../AGENTS.md). The headline rules:

- **Orthogonality first.** Split overlapping responsibilities; unify duplicated abstractions. Do not paper over a muddled design with an extra overload or a "legacy" parameter.
- **Library-user flexibility first.** On public surface (`Beutl.Engine`, `Beutl.Extensibility`, `Beutl.NodeGraph`, `Beutl.FFmpegIpc`, etc.) bias toward interfaces / virtual hooks / composable primitives so plugin authors can substitute pieces we did not anticipate.
- **No silent compat shims.** Do not introduce `[Obsolete]` members, `V2` types, or duplicate overloads to avoid touching call sites — update the call sites in the same diff. The single exception is a published extensibility contract used by out-of-tree plugins **with explicit user approval and a documented removal target**.
- **Mark breaking changes loudly.** Use `feat!:` / `refactor!:` Conventional Commits with a `BREAKING CHANGE:` footer describing the migration and the affected projects.
- **When the trade-off is non-obvious**, surface it to the user. Do not silently pick "keep the old API" just because it is the smaller diff.

The `beutl-design-reviewer` subagent audits public-surface diffs against these rules — it auto-delegates when a change touches public types in the modules above.

## UI / XAML

- **Split out behavior**: when an event handler in a UserControl gets complicated, factor it into an Avalonia `Behavior` class or a `partial` code-behind file. A code-behind file pushing 200 lines is a warning sign.
- **Compiled bindings are required**: every new/changed XAML file declares `x:CompileBindings="True"` and `x:DataType="..."` on the root (see `.claude/rules/xaml.md`).

## Tests

- **NUnit + Moq** under `tests/`. Tests are split across per-area projects (e.g. `tests/Beutl.UnitTests/`, `tests/Beutl.Graphics3DTests/`, `tests/SourceGeneratorTest/`, `tests/Beutl.FFmpegIpc.Tests/`); add new tests to the one that matches the code you changed. Do not introduce another test framework. (`tests/Beutl.Graphics3DTests/` is a Vulkan-gated NUnit suite that self-skips when no Vulkan device is available; GPU-free Graphics3D logic tests live under `tests/Beutl.UnitTests/Engine/Graphics3D/`.)
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
- Do not hand-edit anything under `.specify/scripts/` or `.specify/templates/`. Those files are vendored from upstream [github/spec-kit](https://github.com/github/spec-kit); changes are overwritten by `specify init --force` and must instead go upstream. **Sole exception**: the `SPECS_DIR` override that points Spec-Kit at `docs/specs/` instead of `specs/` (see the `# Beutl local:` markers in `.specify/scripts/bash/common.sh` and `create-new-feature.sh`). Re-apply this patch after every upstream resync, and pursue an upstream-friendly env-var override so the patch can eventually go away.

## Environment requirements

- The Spec-Kit bash scripts under `.specify/scripts/bash/*.sh` use Bash 4+ features (e.g. `${var^^}` uppercase expansion). macOS ships with Bash 3.2 at `/bin/bash`; install a newer Bash via Homebrew (`brew install bash`) so the `#!/usr/bin/env bash` shebang resolves to Bash 4+. The hooks under `.claude/hooks/` are Bash 3.2 compatible.
