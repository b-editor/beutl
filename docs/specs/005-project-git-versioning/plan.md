# Implementation Plan: Git Version Control for Editing Projects

**Branch**: `speckit/005-project-git-versioning` | **Date**: 2026-07-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/005-project-git-versioning/spec.md`

## Summary

Turn a Beutl project directory into a Git repository the app manages for the user: automatic snapshots on explicit save/close, manual commits, a history tool tab with restore, branches, and a single remote (push / ff-only pull) — implemented by invoking the user's installed `git` CLI (research R-1), with graceful degradation when git is absent. Four serialization prerequisites (Id-based element file names, appVersion churn, path-separator normalization, JSON newline pinning — R-10) land first so commits are minimal and cross-platform from day one. Every operation that changes files under the editor runs a safety-snapshot → close → operate → reopen cycle (R-6/R-7); nothing in the in-app surface can rewrite or lose history.

## Technical Context

**Language/Version**: C# (`LangVersion: preview`), .NET `net10.0` + `net10.0-windows`

**Primary Dependencies**: none new — the user's installed `git` (≥ 2.23) as a child process; optional `git-lfs`. No LibGit2Sharp (R-1). Avalonia for the tool tab UI.

**Storage**: the project directory itself becomes the repository work tree; generated `.gitignore`/`.gitattributes`; commit trailers (`Beutl-Snapshot:`) as version metadata (data-model.md)

**Testing**: NUnit + Moq in `tests/Beutl.UnitTests/Editor/VersionControl/` against **real git** in temp dirs (`Assert.Ignore` when absent; env-isolated — R-14); two shell E2E scenarios in `tests/Beutl.HeadlessUITests/`

**Target Platform**: Windows / macOS / Linux desktop (GUI-launch PATH discovery per R-3)

**Project Type**: desktop application feature — Avalonia-free core service (`Beutl.Editor`) + shell orchestration (`Beutl`) + tool tab (`Beutl.Editor.Components`) + settings (`Beutl.Configuration`)

**Performance Goals**: snapshot of a 500-element project ≤ 2 s off the UI thread; history view opens ≤ 1 s for 200 versions (SC-003); bounded `git status` calls under autosave bursts (R-8 stress test)

**Constraints**: never block the UI thread; never touch files outside the project pathspec in a shared repo (FR-003); no history-rewriting operation exposed (FR-028); repository content language-independent (R-5); single writer assumed

**Scale/Scope**: hundreds of small JSON files per project; histories in the hundreds of versions; media up to multi-GB via LFS

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| I. License Firewall | No `ProjectReference` to `Beutl.FFmpegWorker`; no GPL linkage | **PASS** — feature spawns the user's `git` as a separate process (mere process invocation, no linking); no LibGit2Sharp/libgit2 dependency at all (R-1) |
| II. Dual TFM | `net10.0` + `net10.0-windows` keep building | **PASS** — no new TFM; no platform-specific APIs beyond existing per-OS process patterns; no new NuGet packages |
| III. Test-First NUnit | New logic ships with tests | **PASS** — real-git unit suite + serialization regression additions (`NoMigrationRegressionTests`) + 2 headless E2E scenarios (R-14); coverage gate unchanged |
| IV. Avalonia + Compiled Bindings | New XAML declares `x:CompileBindings` + `x:DataType` | **PASS** — `VersionControlTab` views follow the rule (coordinator-lifecycle.md); core service is Avalonia-free by placement |
| V. Style Belongs to the Linter | No stylistic-only edits | **PASS** — `dotnet format` owns style |
| VI. Source Generators | No generator changes | **PASS** — feature does not touch `Beutl.Engine.SourceGenerators` |

**Post-Phase-1 re-check**: PASS — the design adds no project, no package, and no cross-boundary reference; the only public-surface changes are additive (`IProjectVersionControlService` seam, `VersionControlConfig`) plus the `feat!:` appVersion serialization change, which carries a `BREAKING CHANGE:` footer and fixture updates (R-10.2).

## Project Structure

### Documentation (this feature)

```text
docs/specs/005-project-git-versioning/
├── spec.md              # Feature specification (+ Clarifications 2026-07-28)
├── plan.md              # This file
├── research.md          # Phase 0 — decisions R-1 … R-14
├── data-model.md        # Phase 1 — service/config/repo-content model
├── quickstart.md        # Phase 1 — user walkthrough + manual verification matrix
├── contracts/
│   ├── version-control-service.md   # IProjectVersionControlService seam
│   ├── git-cli-invocation.md        # GitCliRunner process contract
│   └── coordinator-lifecycle.md     # trigger wiring + close/reopen cycle + UI map
└── tasks.md             # Phase 2 (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
src/Beutl.Core/
├── Project.cs                                  # touched: appVersion churn fix (R-10.2, feat!)
└── JsonHelper.cs                               # touched: NewLine = "\n" pinning (R-10.4)

src/Beutl.ProjectSystem/ProjectSystem/
└── Scene.cs                                    # touched: Include/Exclude separator normalization (R-10.3)

src/Beutl.Editor/
├── VersionControl/                             # NEW — Avalonia-free core
│   ├── IProjectVersionControlService.cs
│   ├── GitCliVersionControlService.cs
│   ├── GitCliRunner.cs
│   ├── GitInstallationLocator.cs
│   ├── RepositoryWatcher.cs
│   └── (records: RepositoryInfo, CommitInfo, WorkspaceStatus, … per data-model.md)
└── Services/
    ├── ElementFileNaming.cs                    # NEW — {Id:N}.belm convention (R-10.1)
    ├── ElementStructureService.cs              # touched: use ElementFileNaming
    ├── DuplicateHelper.cs                      # touched: use ElementFileNaming
    └── ElementClipboardService.cs              # touched: use ElementFileNaming

src/Beutl.Configuration/
├── VersionControlConfig.cs                     # NEW — ConfigurationBase subclass
└── GlobalConfiguration.cs                      # touched: wire the new config

src/Beutl.Editor.Components/
└── VersionControlTab/                          # NEW — tool tab (views + viewmodels)

src/Beutl/
├── Services/VersionControlCoordinator.cs       # NEW — lifecycle + close/reopen cycles
├── Services/PrimitiveImpls/VersionControlTabExtension.cs   # NEW
├── Services/StartupTasks/LoadPrimitiveExtensionTask.cs     # touched: register extension
├── ViewModels/EditViewModel.cs                 # touched: GetService branch
├── ViewModels/EditContext/ElementAdderImpl.cs  # touched: use ElementFileNaming
├── ViewModels/MenuBarViewModel.Files.cs        # touched: save hooks + new commands
├── ViewModels/Dialogs/CreateNewProjectViewModel.cs         # touched: tracking checkbox
└── Views/MainView.axaml (+ InitializeMenuBar.cs, MacWindow) # touched: menu entries

src/Beutl.Language/
└── Strings.resx (+ locales)                    # touched: new strings

tests/Beutl.UnitTests/Editor/VersionControl/    # NEW — real-git suite (R-14)
tests/Beutl.UnitTests/ProjectSystem/NoMigrationRegressionTests.cs   # touched (R-10.2/10.4)
tests/Beutl.HeadlessUITests/                    # touched: 2 E2E scenarios
```

**Structure Decision**: no new csproj — the core service goes into `src/Beutl.Editor/VersionControl/` (the placement rule for Avalonia-free, unit-testable editor services; precedent `ProjectPackageService`), UI into the existing tool-tab host `Beutl.Editor.Components`, shell wiring into `src/Beutl`. This mirrors how FileBrowserTab/TerminalTab are split today and keeps the plugin-facing seam (`IProjectVersionControlService` via `IEditorContext.GetService`) in a library project.

## Phase 0: Research

Complete — [research.md](./research.md), decisions R-1 … R-14. Headline choices: user's `git` CLI over LibGit2Sharp (R-1), snapshot-on-explicit-save-only (R-4), restore-as-new-commit (R-6), close→operate→reopen cycle (R-7), watcher/status anti-feedback design (R-8), four serialization prerequisites (R-10), pathspec scoping for enclosing repos (R-11).

## Phase 1: Design & Contracts

Complete — [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md). The service seam, process contract, and coordinator orchestration (trigger table, cycle steps, UI map) are pinned; repository content contracts (`.gitignore`, `.gitattributes`, message trailers) are in data-model.md.

## Phase 1 testing

- **Serialization prerequisites**: `NoMigrationRegressionTests` additions (appVersion preserved on plain resave; newline byte-stability on all OSes), separator normalization round-trip (Windows-written exclude entries load on POSIX), `ElementFileNaming` collision suffixes, per-call-site tests that new elements get `{Id:N}.belm`.
- **Runner**: arg passing, NUL parsing, typed non-zero-exit errors with stderr, env injection (`GIT_TERMINAL_PROMPT`, `GIT_OPTIONAL_LOCKS`), cancellation kills the process.
- **Service**: init artifacts (+ initial commit), status parsing incl. unmerged→`Conflicted` lockout, clean-tree commit skip, trailer round-trip through history, log paging, nested-repo pathspec scoping (fixture with a repo root above the project; asserts foreign files never staged/cleaned), restore reproduces the exact tree of the target commit incl. deleting later-added elements while `.beutl/` survives, branch create/switch, ff-pull success + divergence via a local bare remote, repo-local identity get/set.
- **Watcher**: debounce and `.git`/`.beutl`/`*.tmp` exclusion (TimeProvider-based); 1000-edit burst asserts bounded status calls (R-8).
- **Shell E2E** (2 scenarios): save → snapshot appears; restore close/reopen cycle completes and clears undo.
- **Manual matrix**: quickstart.md table (network/credential/LFS/notarization paths that cannot be automated honestly).

## Risks & mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| appVersion serialization change ripples into migration semantics / fixtures | Medium | High | Land first as an isolated `feat!:` task with explicit "when does appVersion advance" rules + regression fixtures (R-10.2) |
| autosave → watcher → `git status` feedback loop | Medium | Medium | `GIT_OPTIONAL_LOCKS=0` + `.git`/`.beutl` exclusion + 500 ms debounce, verified by the 1000-edit burst test (R-8) |
| `git clean -fd` in restore deletes user files on a wrong pathspec | Low | High | The only deleting operation in the design; pathspec-scoped, `.gitignore`-protected, and covered by the nastiest nested-repo fixtures (R-6, contract §4) |
| Newline pinning causes a one-time full diff for existing Windows projects | Certain (once) | Low | Pair with `.gitattributes eol=lf` so it happens once per project, not per machine; release-notes callout (R-10.4) |
| Close/reopen cycle meets in-memory state not flushed by the close path | Low | Medium | Reuses the proven `ProjectPackageService.ImportAsync` lifecycle; E2E restore scenario verifies; cycle refuses to run during export (coordinator contract) |
| macOS CLT git stub triggers an OS install dialog | Medium | Low | `xcode-select -p` check before trusting `/usr/bin/git` (R-3) |
| GUI-launch PATH misses the user's git | Medium | Low | Ordered probe list + `GitExecutablePath` override (R-3) |

## Complexity Tracking

No constitution violations to justify — no new projects, no new packages, no boundary crossings.
