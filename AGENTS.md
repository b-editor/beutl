# AGENTS.md — Beutl shared instructions (for all AI coding agents)

Common contract for any AI coding agent working in this repo (Claude Code, Codex, Cursor, etc.). Claude Code imports this file via `CLAUDE.md`.

## Language policy

- **Conversation & plans**: respond in the user's language (Japanese when the user writes in Japanese). Plans presented via plan mode / equivalents (e.g. Claude Code's ExitPlanMode) follow the conversation language.
- **Artifacts** — code, code comments, commit messages, PR titles/descriptions, Issue titles/descriptions, and project documentation under `docs/` — write in **English**, regardless of conversation language.

## Project overview

Beutl is a cross-platform video editing / compositing application built on Avalonia. It is written in .NET and C# / XAML, targeting both `net10.0` and `net10.0-windows` (dual-target).

- License: the main app is **MIT**; `Beutl.FFmpegWorker` alone is **GPL-3.0-or-later** (a separate process)
- UI: Avalonia (XAML + ViewModel)
- Tests: NUnit + Moq under `tests/` (per-area projects, e.g. `tests/Beutl.UnitTests/`, `tests/Beutl.PublicApiContractTests/`, `tests/Beutl.Graphics3DTests/`, `tests/SourceGeneratorTest/`, `tests/Beutl.FFmpegIpc.Tests/`). `tests/Beutl.PublicApiContractTests/` is the non-friend compile gate for public authoring APIs; `tests/Beutl.Graphics3DTests/` is a Vulkan-gated NUnit suite that self-skips when no Vulkan device is available — see `tests/CLAUDE.md`
- E2E / headless-UI tests: `tests/Beutl.E2ETests/` (library-level) and `tests/Beutl.HeadlessUITests/` (drives the real shell, the sole test referencing `src/Beutl`) on `Avalonia.Headless.NUnit`, with shared helpers in `tests/Beutl.Testing.Headless/`. They run on headless CI without xvfb or a GPU — see `tests/CLAUDE.md`
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
| `Beutl.FFmpegIpc` | **MIT** IPC layer (transport: Protocol / Transport / SharedMemory). Also hosts the IPC client providers under `Providers/`, which translate frame/sample messages into `Beutl.Media` / `Beutl.Extensibility` types; that adapter role is why this project deliberately takes `ProjectReference`s to `Beutl.Engine` + `Beutl.Extensibility` rather than staying dependency-free. |
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

## Do not defer work

**Deferring tasks is forbidden.** When the change surfaces something — an edge case, a known TODO, a refactor you scoped out, a test you could not add yet — **finish it in the same change.** Do not split in-scope work off into a "later" pile, and do not treat capturing a follow-up (a PR `## Follow-ups` list, a `// TODO` comment, a Draft on the project board) as a substitute for doing the work. Those capture mechanisms exist to record genuinely separate work, **not** to dodge effort that belongs in the current change.

There are only two legitimate reasons to stop short of completing what the change surfaced, and **both require telling the user — never silently file it away and move on:**

1. **Genuinely out of scope** — a different feature or area that does not belong in this change. Surface it to the user (e.g. via `AskUserQuestion`) and let them decide whether to widen the scope or track it separately.
2. **Blocked** — you cannot resolve it without something only the user can provide (missing access, an upstream fix, a product decision). State the blocker explicitly.

Do not leave `[Obsolete]` shims, `// TODO` markers, or "v2" stubs behind as deferral markers (see "Design priorities").

## Spec-Driven Development for large features

For large features (a new filter category, an IPC protocol change, a new editor pane), use the Spec-Kit flow: `/speckit-specify → /speckit-plan → /speckit-tasks → /speckit-implement`. Details: `docs/ai-workflow/spec-driven-development.md`.

## What is automated (things to know)

- **Automatic PR review**: opening a PR triggers `.github/workflows/claude-code-review.yml`, which runs Claude Code and posts a structured review.
- **Daily scheduled review**: `.github/workflows/scheduled-code-review.yml` reviews recent diffs or a given scope and files Draft items into GitHub Projects v2.
- **`@claude` mentions**: writing `@claude` in an issue/PR/review comment triggers `.github/workflows/claude.yml`.
- **Autonomous board loop**: `/beutl-loop` by default **drains the Project #9 board** in one bounded run (or pass a tighter `N`) — each tick a worktree sub-agent implements one item behind two binary gates (a **test gate** — a production change must ship an NUnit test + a recorded characterization baseline that met its expected outcome (green for a behavior-preserving change, **red for a bug-fix regression test** against the unmodified buggy code), or a documented manual-verification — and a six-point **self-review gate**), hands back a **draft branch**; the orchestrator runs a **pre-PR review round** (independent machine-verify of the self-review axes + `@beutl-reviewer` / `@beutl-xaml-binder` / `@beutl-design-reviewer`, up to two rework iterations) and opens the PR; the PR's **bot** reviews are resolved autonomously (including replying-and-resolving clear bot false positives and recording the pattern to loop-memory; human reviews are left for a person), and **the loop posts its own code-owner approval (`@yuto-trd`) and auto-squash-merges low-to-moderate-risk PRs — a conditional coverage probe gates auto-merge for larger production diffs — while higher-risk ones are left for a human**. Large features route through the Spec-Kit flow (`specify → plan → tasks → implement`). It runs **in-session only** on opus with auto-accept (`acceptEdits`) — there is no headless `claude -p` launcher (it billed as metered API usage). A parallel batch (`BEUTL_LOOP_PARALLEL`, default 1, max 3) runs up to 3 items concurrently with footprint-overlap scheduling. By default it is **unbounded by item count** — the stagnation circuit-breaker (3 no-progress strikes; a single blocked item is skipped, not a stop) and an empty board are the stops, with an optional `BEUTL_LOOP_MAX_ITEMS` cap and optional wall-clock; it never merges a high-risk PR, force-pushes `main`, or bypasses the rulesets. Verify the loop contract with `loop-contract-check.sh` (G-13) and calibrate the risk thresholds with `loop-calibrate.sh` (G-14). Details: `docs/ai-workflow/loop-engineering.md`.
- **Local subagents**: `beutl-reviewer` / `beutl-test-runner` / `beutl-source-generator-impact` / `beutl-spec-explorer` / `beutl-xaml-binder` / `beutl-design-reviewer` / `beutl-gpu-crash-reproducer` / `beutl-board-task-runner` live in `.claude/agents/`.
- **Local hooks**: dangerous-command deny / dotnet auto-allow / GPL-MIT boundary deny / session-start context injection live in `.claude/hooks/`. Details: `docs/ai-workflow/subagents-and-hooks.md`.

## Self-improvement

After completing a substantial task — 3+ files changed, a new feature, a non-trivial refactor, or a finished PR review cycle — invoke `/beutl-ai-self-review`. It cross-checks `AGENTS.md`, `CLAUDE.md`, `.claude/agents/`, `.claude/skills/`, `.claude/rules/`, `.claude/hooks/`, and `docs/ai-workflow/` against the current repo state and proposes targeted updates one item at a time. Apply the ones the user confirms; do not auto-commit.

## Detailed guides

- [docs/ai-workflow/README.md](docs/ai-workflow/README.md) — overview and routing
- [docs/ai-workflow/coding-guidelines-for-ai.md](docs/ai-workflow/coding-guidelines-for-ai.md) — rules that require human judgment
- [docs/ai-workflow/subagents-and-hooks.md](docs/ai-workflow/subagents-and-hooks.md) — subagent / hook reference
- [docs/ai-workflow/spec-driven-development.md](docs/ai-workflow/spec-driven-development.md) — Spec-Kit
- [docs/ai-workflow/gpl-mit-boundary.md](docs/ai-workflow/gpl-mit-boundary.md) — IPC boundary
- [docs/ai-workflow/loop-engineering.md](docs/ai-workflow/loop-engineering.md) — the `/beutl-loop` autonomous board loop and its risk-gated auto-merge
- Path-scoped rules: `.claude/rules/{xaml,csharp,gpl-mit-boundary,ai-setup}.md`
