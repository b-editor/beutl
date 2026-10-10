# Contract: VersionControlCoordinator lifecycle & UI orchestration

**Scope**: `src/Beutl/Services/VersionControlCoordinator.cs` — the app-level owner of per-project services and the only component allowed to run the close→operate→reopen cycle.

## Ownership

- Constructed once in `MainViewModel` next to `ProjectService`.
- Subscribes `ProjectService.ProjectObservable`: on project open → resolve project root from `Project.Uri`, run repo discovery (`git rev-parse --show-toplevel`), construct `GitCliVersionControlService` + `RepositoryWatcher`; on close → retire both after any in-flight activation completes.
- Ordinary close captures the current activation revision and project root, waits for that activation to finish, then retires the final owned backend for the same activation lineage exactly once with the `Close` snapshot intent. A project change while waiting aborts that handoff, so an old project's close snapshot can never reach a newly opened project's backend. The snapshot intent is passed even while an owned backend is transitioning from untracked to tracked; backend retirement rechecks `Repository` after the current exclusive initialization finishes and no-ops only when it is still genuinely untracked.
- Maintains separate owned and visible service state. A temporary close keeps ownership for recovery but publishes `null` to editor consumers; reopen republishes the same service only when the project root still matches.
- Publishes `(service, IsTracked, IsGitAvailable)` snapshots through one revisioned FIFO on the UI thread. Stale discovery completions and older queued publications cannot overwrite a newer project state. Within each revision, availability and tracked flags are written before the service, so every service-publication subscriber observes the matching flags; individual reactive callbacks are not an atomic multi-property transaction.

## Commit trigger wiring (FR-012/013/014/015)

| Trigger | Hook point | Kind |
|---|---|---|
| Explicit Save / Save All | end of `MenuBarViewModel.OnSave` / `OnSaveAll`, still holding the project-file write reservation → `NotifySavedAsync(completedWrite)` | `Save` |
| Project close | start of the close flow, after final save, before `ProjectService.CloseProject()` | `Close` |
| Before restore / branch switch | inside the cycle, when status is dirty | `Safety` |
| After restore | inside the cycle | `Restore` |
| Restore recovery after a post-commit failure | inside the recovery path | `Recovery` |
| Manual commit | tool tab / command palette, after saving the open project inside the exclusive lease so the version records what the user sees | `Manual` |

Autosave ticks never reach the coordinator (FR-015). All triggers no-op silently on a clean tree.

## The close→operate→reopen cycle (FR-022)

```text
1. Read-only backend preflight while the project stays open
   └─ return immediately when pull is already up to date or cannot proceed
2. Release the backend gate, then show the operation confirmation
3. Acquire ProjectService's transition gate and the work-tree lease
4. Reacquire the backend gate, save the project, and revalidate status and operation need
5. Restore / branch switch with a dirty project: CommitAllAsync(safety message, Safety)
6. Prefetch the Git LFS objects of the target while the project is open and the step is cancellable
7. await ProjectService.CloseProject()
8. Plain Git inside the mutation-phase `ExecuteExclusiveAsync` transaction:
   - pull: `git stash push --include-untracked` of the project scope when it has local changes,
     `git merge --ff-only --no-overwrite-ignore <fetched commit>`, `git stash pop`
   - restore: `git restore --source=<commit> --staged --worktree` on the project scope, recorded
     as a Restore snapshot (`git switch -c <name>` at the current tip first for a new branch)
   - branch switch: `git switch <name>`
9. await ProjectService.OpenProject(bepPath)
```

The two backend phases never invert the normal-close lock order. Read-only preflight releases the backend gate before requesting the project transition; the mutation phase always holds the project transition before reacquiring the backend gate. Confirmation dialogs hold neither gate. A concurrent normal close can therefore retire the backend and complete without deadlocking against pull confirmation. Pull confirmation captures the project/service epoch before the preflight, so close, branch transition, and backend replacement cancel a stale prompt.

Pull reopens the project whatever Git reports and returns Git's own result. When the merge fails, the stash is popped back first. When the stash cannot be popped after the fast-forward, Git keeps the entry (`beutl: local changes before pull`) and leaves any conflicts in the worktree; the result says so, and the conflict guidance and the conflict-marker warning take over from there. A project the pulled commit cannot reopen is reported and left closed; the fast-forward stands, as it would with `git pull`. `RepositoryDirty` is reserved for the preflight's cleanliness precondition, such as an unrelated dirty path outside an enclosing-project pathspec.

A restore that fails before its commit has already put the project scope back to HEAD, so recovery only reopens the project. If a restore commit succeeds but reopening fails, recovery restores the captured pre-operation tree and records a `Recovery` commit on top. The attempted restore remains in history and the original project state becomes the visible tip again without rewriting history. A switch or a restore to a new branch that already moved HEAD returns to the original branch with `git switch`. For Restore to New Branch the failed restore branch is retained, and only after the original project has reopened successfully does the coordinator identify that branch in a localized warning and direct the user to verify other worktrees before deleting it manually; a failed recovery emits no cleanup guidance.

Earlier versions kept private refs under `refs/beutl/safety/` and `refs/beutl/recovery/`. Beutl no longer reads or writes them; they are harmless and left alone.

Push runs outside the cycle (no work-tree mutation): progress dialog + cancel only.

Guard: restore and branch-switch refuse to begin while the existing output service reports an active export. They acquire the exclusive work-tree lease before confirmation and hold it through close, mutation, recovery, and reopen. Pull confirmation first releases the backend gate, then acquires the exclusive work-tree lease with the project transition before its revalidation/mutation phase. Explicit saves acquire a project-file write reservation before writing and hand that same reservation to the save snapshot, so the workspace is never unreserved between the write and the commit. Automatic save and close snapshots hold the exclusive lease through staging and commit, so Git never stages a partially written project file.

## Enablement flows (FR-001/FR-002/FR-003)

- **Create dialog**: `CreateNewProjectViewModel` requires `IProjectVersionControlInitializer` and the identity callback; there is no degraded constructor that silently omits version control. The initializer exposes availability and project initialization without coupling the dialog to the app coordinator. `InitializeCurrentProjectAsync` accepts `Func<CancellationToken, Task<GitIdentity?>>` and forwards its exact operation token to the identity prompt, so cancellation is not lost at the UI callback boundary. The identity flyout registers that token, cancels its pending result, and closes itself on the UI thread rather than waiting for user dismissal. "Track history with Git" remains false and hidden until `GetAvailabilityAsync` reports `Installed`; only then is the configured default applied and shown. Creation snapshots that visible checked state before writing the project, so a detection completion during creation can never opt the user in silently. A checked visible option calls `BeginNewProject` before creation and passes the setup's `InitializeAsync` to `ProjectService.CreateProject` as the step that runs after the project files are written and before the project opens, so the first version is recorded before any editor exists and no editor is suspended for it. The backend that initialized the repository is handed to the activation of that same creation transition, which publishes it as tracked without repeating discovery or hygiene. Once the first version is recorded, the backend is handed over even if the reserved-path step after it fails. A declined or failed initialization discards that backend, and the project opens with a fresh one. When initialization stopped after it created or attached the repository, that activation looks at the repository again, without asking: it resumes tracking when discovery still finds that same repository with a checked-out commit, as reopening the project would, and otherwise the project opens untracked. A backend prepared for a project that did not open is retired when the setup is disposed, or with the coordinator. `InitializeCurrentProjectAsync` remains the path for enabling an already open project, and keeps its editors suspended, behind the progress view described below, through the initial commit.
- **Existing project**: "Enable Version Control…" button in the version control tab, which raises the existing shell `EnableVersionControl` context command (also reachable from the command palette, gated on `ProjectService.IsOpened`).
- **Command lifecycle**: `MenuBarViewModel` delegates version-control availability, tracking state, and save notification to the coordinator. Project close remains the responsibility of `ProjectService`; no package-operation or context-command public contract is changed by this feature.
- **Nested repo detected**: activation asks once, unless the repository already records an opt-in, with a consent dialog offering "use enclosing repository" (pathspec scoping, project-local `.gitignore`) / "leave unmanaged". Never `git init` inside a foreign work tree.
- **Save As**: never copies `.git`; the copy is offered fresh enablement per the creation default (clarification #3).

## UI surface map

| Surface | Location | Content |
|---|---|---|
| Tool tab | `src/Beutl.Editor.Components/VersionControlTab/` + `VersionControlTabExtension` (`[PrimitiveImpl]`, registered in `LoadPrimitiveExtensionTask`) | branch + ahead/behind + dirty summary; commit box; paged history list (kind badges); changed files; unified diff view (monospace, +/- coloring, 1 MB cap) |
| Commands | `MenuBarViewModel.Files.cs` + `MainViewExtension` context commands + command palette (no menu-bar entries: the tool tab is the only menu-level surface) | Enable Version Control…, Commit… |
| Settings | `VersionControlConfig` page | per data-model.md table |
| Degradation | tool tab + the version control commands collapse to one informational state | per-OS install guidance (FR-037) |

While a project is created with tracking, version control is enabled for the open project, or a version-controlled project closes, `EditorService.LifecycleActivity` replaces the editor area with a progress view (`ProjectLifecycleOverlay` in `MainView`), so the user never sees an editor that looks usable while it waits for Git. Creation shows it from `BeginNewProject` until the setup is disposed after the editor has opened. Enabling version control shows it exactly while `InitializeCurrentProjectAsync` keeps the editors suspended, from before the save until the initial commit ends. It is shown and the editors are suspended in one dispatcher job, and the editors are enabled again before it is hidden, so a suspended editor is never drawn without it, whichever thread the initialization runs on. It is hidden while the identity prompt is open, since the editors are released then so the user can keep editing, and shown again for the retry. Closing shows it as soon as the close barrier at the start of `ClosingPreparing` stops new operations, when an operation is still running, an activation is pending, or the owned backend is tracked. It is decided under the same lock that activates the barrier, so no operation can start unnoticed before the wait it causes, and it stays until the close completes or is aborted. Overlapping activities show the most recent one.

All new XAML declares `x:CompileBindings="True"` + `x:DataType` (compiled-binding requirement). All user-facing strings go through `Beutl.Language` resources; repository content stays English (R-5).
