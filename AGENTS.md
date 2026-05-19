# AGENTS.md — Beutl shared instructions (for all AI coding agents)

Common contract for any AI coding agent working in this repo (Claude Code, Codex, Cursor, etc.). Claude Code imports this file via `CLAUDE.md`.

## Language policy

- **Conversation & plans**: respond in the user's language (Japanese when the user writes in Japanese). Plans presented via plan mode / equivalents (e.g. Claude Code's ExitPlanMode) follow the conversation language.
- **Artifacts** — code, code comments, commit messages, PR titles/descriptions, Issue titles/descriptions, and project documentation under `docs/` — write in **English**, regardless of conversation language.

## Project overview

Beutl is a cross-platform video editing / compositing application built on Avalonia. It is written in .NET and C# / XAML, targeting both `net10.0` and `net10.0-windows` (dual-target).

- License: the main app is **MIT**; `Beutl.FFmpegWorker` alone is **GPL-3.0-or-later** (a separate process)
- UI: Avalonia (XAML + ViewModel)
- Tests: NUnit + Moq under `tests/` (per-area projects, e.g. `tests/Beutl.UnitTests/`, `tests/Beutl.Engine.Tests/`, `tests/SourceGeneratorTest/`, `tests/Beutl.FFmpegIpc.Tests/`)
- Build: Nuke (`nukebuild/`) or `dotnet` directly

## Build / test / format

```bash
dotnet build Beutl.slnx                                            # build
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings  # test
dotnet format Beutl.slnx                                           # format
./build.sh <Target>                                                # Nuke (same as CI)
```

Claude Code skills are provided as `/beutl-build`, `/beutl-test`, `/beutl-format`, `/beutl-coverage`. They also fire on natural-language requests like "run the tests", "does this still build?", and they will **confirm scope** with AskUserQuestion (whole solution vs single project, verify vs apply, etc.) before executing. Pass arguments (e.g. `/beutl-test <FQN-substring>`) to skip the confirmation.

## Module boundary map

| Project | Role |
|---|---|
| `Beutl.Engine` | Core rendering / scene / track (no project dependencies) |
| `Beutl.Engine.SourceGenerators` | Roslyn source generators |
| `Beutl.ProjectSystem` | Project / document persistence |
| `Beutl.Editor`, `Beutl.Editor.Components`, `Beutl.Controls` | Avalonia UI layer |
| `Beutl.Extensibility` | Plugin abstractions |
| `Beutl.NodeGraph` | Node editor |
| `Beutl.FFmpegIpc` | **MIT** IPC layer |
| `Beutl.FFmpegWorker` | **GPL** separate process; reach it only via IPC |
| `Beutl.Api` | Server API client |

## Mandatory rules

1. **Do not cross the GPL/MIT boundary.** MIT projects must not take a `ProjectReference` to `Beutl.FFmpegWorker`. The `.claude/hooks/check-gpl-mit-boundary.sh` PreToolUse hook enforces this mechanically. Details: `docs/ai-workflow/gpl-mit-boundary.md`.
2. **XAML must use compiled bindings** — every new UserControl declares `x:CompileBindings="True"` together with `x:DataType="..."`. Details: `.claude/rules/xaml.md`.
3. **New logic ships with a NUnit test** — add tests under `tests/` in the matching test project (e.g. `tests/Beutl.UnitTests/` for general unit tests, `tests/SourceGeneratorTest/` for generator changes).
4. **Do not ask the AI to do the linter's job** — `.editorconfig` / `xamlstyler.json` / `dotnet format` own style.
5. **Do not change existing CI workflows (`.github/workflows/*`) without explicit approval.**
6. **Force-pushing to `main` / `master` is forbidden** — the hook denies any `git push` with `--force`, `-f`, or `--force-with-lease` that targets `main`/`master`, including refspec forms like `HEAD:main`. Push to a feature branch instead.

## Commit convention

Conventional Commits, following the existing history:

- `fix: ...` — bug fix
- `feat: ...` — new feature
- `refactor: ...` — behavior-preserving refactor
- `docs: ...` — documentation

## Spec-Driven Development for large features

For large features (a new filter category, an IPC protocol change, a new editor pane), use the Spec-Kit flow: `/speckit-specify → /speckit-plan → /speckit-tasks → /speckit-implement`. Details: `docs/ai-workflow/spec-driven-development.md`.

## What is automated (things to know)

- **Automatic PR review**: opening a PR triggers `.github/workflows/claude-code-review.yml`, which runs Claude Code and posts a structured review.
- **Daily scheduled review**: `.github/workflows/scheduled-code-review.yml` reviews recent diffs or a given scope and files Draft items into GitHub Projects v2.
- **`@claude` mentions**: writing `@claude` in an issue/PR/review comment triggers `.github/workflows/claude.yml`.
- **Local subagents**: `beutl-reviewer` / `beutl-test-runner` / `beutl-source-generator-impact` / `beutl-spec-explorer` / `beutl-xaml-binder` live in `.claude/agents/`.
- **Local hooks**: dangerous-command deny / dotnet auto-allow / GPL-MIT boundary deny / session-start context injection live in `.claude/hooks/`. Details: `docs/ai-workflow/subagents-and-hooks.md`.

## Self-improvement

After completing a substantial task — 3+ files changed, a new feature, a non-trivial refactor, or a finished PR review cycle — invoke `/beutl-ai-self-review`. It cross-checks `AGENTS.md`, `CLAUDE.md`, `.claude/agents/`, `.claude/skills/`, `.claude/rules/`, `.claude/hooks/`, and `docs/ai-workflow/` against the current repo state and proposes targeted updates one item at a time. Apply the ones the user confirms; do not auto-commit.

## Detailed guides

- [docs/ai-workflow/README.md](docs/ai-workflow/README.md) — overview and routing
- [docs/ai-workflow/coding-guidelines-for-ai.md](docs/ai-workflow/coding-guidelines-for-ai.md) — rules that require human judgment
- [docs/ai-workflow/subagents-and-hooks.md](docs/ai-workflow/subagents-and-hooks.md) — subagent / hook reference
- [docs/ai-workflow/spec-driven-development.md](docs/ai-workflow/spec-driven-development.md) — Spec-Kit
- [docs/ai-workflow/gpl-mit-boundary.md](docs/ai-workflow/gpl-mit-boundary.md) — IPC boundary
- Path-scoped rules: `.claude/rules/{xaml,csharp,gpl-mit-boundary,ai-setup}.md`
