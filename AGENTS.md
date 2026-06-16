# AGENTS.md — Beutl shared instructions (for all AI coding agents)

Common contract for any AI coding agent working in this repo (Claude Code, Codex, Cursor, etc.). Claude Code imports this file via `CLAUDE.md`.

## Language policy

- **Conversation & plans**: respond in the user's language (Japanese when the user writes in Japanese). Plans presented via plan mode / equivalents (e.g. Claude Code's ExitPlanMode) follow the conversation language.
- **Artifacts** — code, code comments, commit messages, PR titles/descriptions, Issue titles/descriptions, and project documentation under `docs/` — write in **English**, regardless of conversation language.

## Project overview

Beutl is a cross-platform video editing / compositing application built on Avalonia. It is written in .NET and C# / XAML, targeting both `net10.0` and `net10.0-windows` (dual-target).

- License: the main app is **MIT**; `Beutl.FFmpegWorker` alone is **GPL-3.0-or-later** (a separate process)
- UI: Avalonia (XAML + ViewModel)
- Tests: NUnit + Moq under `tests/` (per-area projects, e.g. `tests/Beutl.UnitTests/`, `tests/Beutl.Graphics3DTests/`, `tests/SourceGeneratorTest/`, `tests/Beutl.FFmpegIpc.Tests/`)
- Build: Nuke (`nukebuild/`) or `dotnet` directly

## Build / test / format

```bash
dotnet build Beutl.slnx                                            # build
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings  # test
dotnet format Beutl.slnx                                           # format
./build.sh <Target>                                                # Nuke (same as CI)
```

Claude Code skills are provided as `/beutl-build`, `/beutl-test`, `/beutl-format`, `/beutl-coverage`. They also fire on natural-language requests like "run the tests", "does this still build?", and they will **confirm scope** with AskUserQuestion (whole solution vs single project, verify vs apply, etc.) before executing. Pass arguments (e.g. `/beutl-test <FQN-substring>`) to skip the confirmation. Before opening a PR, `/beutl-pre-pr` runs the same checks locally that the CI review and the `beutl-reviewer` subagent will run.

## Module boundary map

| Project | Role |
|---|---|
| `Beutl.Engine` | Core rendering / scene / track (no project dependencies) |
| `Beutl.Engine.SourceGenerators` | Roslyn source generators |
| `Beutl.ProjectSystem` | Project / document persistence |
| `Beutl.Editor` | Non-UI editor logic — undo/redo, packaging, editing-pipeline services (no Avalonia) |
| `Beutl.Editor.Components`, `Beutl.Controls` | Avalonia UI layer (views / controls / ViewModels) |
| `Beutl.Extensibility` | Plugin abstractions |
| `Beutl.NodeGraph` | Node editor |
| `Beutl.FFmpegIpc` | **MIT** IPC layer |
| `Beutl.FFmpegWorker` | **GPL** separate process; reach it only via IPC |
| `Beutl.Api` | Server API client |

## Mandatory rules

1. **Do not cross the GPL/MIT boundary.** MIT projects must not take a compile-closure `ProjectReference` to `Beutl.FFmpegWorker`; the sole exception is `src/Beutl/Beutl.csproj`'s build-order-only reference (`ReferenceOutputAssembly="false"` + output-copy target). The `.claude/hooks/check-gpl-mit-boundary.sh` PreToolUse hook enforces this mechanically. Details: `docs/ai-workflow/gpl-mit-boundary.md`.
2. **XAML must use compiled bindings** — every new UserControl declares `x:CompileBindings="True"` together with `x:DataType="..."`. Details: `.claude/rules/xaml.md`.
3. **New logic ships with a NUnit test** — add tests under `tests/` in the matching test project (e.g. `tests/Beutl.UnitTests/` for general unit tests, `tests/SourceGeneratorTest/` for generator changes).
4. **Do not ask the AI to do the linter's job** — `.editorconfig` / `xamlstyler.json` / `dotnet format` own style.
5. **Do not change existing CI workflows (`.github/workflows/*`) without explicit approval.**
6. **Force-pushing to `main` / `master` is forbidden** — the hook denies the literal `git push (--force | -f | --force-with-lease) origin (main | master)` forms. Bypass routes (refspec forms like `HEAD:main`, variable expansion, etc.) are explicitly out of scope for the hook and are guarded by GitHub branch protection. Push to a feature branch instead.

## Design priorities (adopt better designs eagerly)

Beutl's policy: **if there is a clearly better design, we want to adopt it.** Backward compatibility is a cost to weigh against that improvement, not a default to preserve. When the cleaner design wins on the merits, take it and migrate the call sites in the same change. Specifically:

- **Orthogonality first.** If two abstractions overlap or a single type has multiple unrelated responsibilities, split / unify / rename — even if it means renaming public types, moving members between projects, or changing constructor signatures. Do not paper over a muddled design with an extra overload or a "legacy" parameter.
- **Library-user flexibility first.** When designing public surface in `Beutl.Engine`, `Beutl.Extensibility`, `Beutl.NodeGraph`, `Beutl.FFmpegIpc`, etc., prefer extensibility points (interfaces, virtual hooks, composable primitives) over a single closed implementation that happens to fit current callers. Ask "could a plugin author do something we did not anticipate?" and bias toward yes.
- **Do not introduce `[Obsolete]` shims, duplicate "v2" types, or compatibility wrappers** to avoid touching call sites. Update the call sites in the same change. The only exception is a published extensibility contract used by out-of-tree plugins where the user has explicitly asked for a deprecation window — in that case, document the removal target in the PR description.
- **Breaking changes need a `feat!:` / `refactor!:` Conventional Commit and a `BREAKING CHANGE:` footer** describing the migration. Mention the affected projects so downstream consumers see it in the changelog.
- **When the choice is non-obvious** (e.g. the cleaner design has a real cost — large diff, ripple into many plugins, in-flight feature branches), surface the trade-off to the user and let them decide. Do not silently pick "keep the old API" just because it is the smaller diff, and equally do not silently force a sweeping rewrite when the gain is marginal.

`beutl-design-reviewer` (see `.claude/agents/`) audits non-trivial public-API changes against these priorities; auto-delegate when a change touches public surface or extensibility points.

## Commit convention

Conventional Commits, following the existing history:

- `fix: ...` — bug fix
- `feat: ...` — new feature
- `refactor: ...` — behavior-preserving refactor
- `docs: ...` — documentation

## Follow-up tasks

When work surfaces a follow-up — a deferred edge case, a known TODO, a refactor you scoped out, a test you could not add yet — **do not let it evaporate.** Capture it in one of two places:

1. **If a PR is open (or you are about to open one), append it to the PR description.** Add a dedicated `## Follow-ups` list so reviewers see it; fall back to `## Fixed issues / References` only when noting a linked issue/PR (that section is for closed/referenced issues, not for "fixed" follow-ups). This is the default for anything tightly coupled to the PR under review.
2. **Otherwise, add it as a Draft item to GitHub Projects v2 — [b-editor/projects/9](https://github.com/orgs/b-editor/projects/9).** This is the default for cross-cutting or longer-horizon work that outlives the current PR. Give the draft a clear title and a one-line body describing the context; reference the originating PR/commit when one exists.

Pick whichever fits; when unsure, prefer the PR description for PR-local items and the project board for everything else. Do not silently drop a follow-up just because it is out of scope for the current change.

## Spec-Driven Development for large features

For large features (a new filter category, an IPC protocol change, a new editor pane), use the Spec-Kit flow: `/speckit-specify → /speckit-plan → /speckit-tasks → /speckit-implement`. Details: `docs/ai-workflow/spec-driven-development.md`.

## What is automated (things to know)

- **Automatic PR review**: opening a PR triggers `.github/workflows/claude-code-review.yml`, which runs Claude Code and posts a structured review.
- **Daily scheduled review**: `.github/workflows/scheduled-code-review.yml` reviews recent diffs or a given scope and files Draft items into GitHub Projects v2.
- **`@claude` mentions**: writing `@claude` in an issue/PR/review comment triggers `.github/workflows/claude.yml`.
- **Local subagents**: `beutl-reviewer` / `beutl-test-runner` / `beutl-source-generator-impact` / `beutl-spec-explorer` / `beutl-xaml-binder` / `beutl-design-reviewer` / `beutl-gpu-crash-reproducer` live in `.claude/agents/`.
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
