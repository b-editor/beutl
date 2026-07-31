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
| Explicit Save / Save All | end of `MenuBarViewModel.OnSave` / `OnSaveAll` → `NotifySavedAsync()` | `Save` |
| Project close | start of the close flow, after final save, before `ProjectService.CloseProject()` | `Close` |
| Before restore / branch switch | inside the cycle, when status is dirty | `Safety` |
| Dirty pull | durable private checkpoint before pull; promoted after fast-forward | `Safety` |
| After restore | inside the cycle | `Restore` |
| Restore recovery after a post-commit failure | inside the recovery path | `Recovery` |
| Manual commit | tool tab / menu command | `Manual` |

Autosave ticks never reach the coordinator (FR-015). All triggers no-op silently on a clean tree.

## The close→operate→reopen cycle (FR-022)

```text
1. Confirm dialog (operation-specific; discloses close/reopen + undo-history loss)
2. If dirty:
   - restore / branch switch: CommitAllAsync(safety message, Safety)
   - pull: create a durable refs/beutl/safety/* checkpoint without moving the branch
3. ProjectService.CloseProject()
4. Git operation inside one `ExecuteExclusiveAsync` transaction
   └─ on failure: recover the operation-specific original state, then surface the error
      (original branch/work tree for switch; compensating Recovery commit for restore;
       exact branch-tip CAS plus checkpoint restore for pull)
5. For restore: apply the selected tree and append a Restore commit atomically
   For dirty pull: apply the checkpoint tree and append a Safety commit atomically
6. ProjectService.OpenProject(bepPath)
```

Pull recovery captures the checked-out local branch ref and commit before closing. It only rolls that same ref back when its current commit still equals the operation's expected commit; a concurrent external branch movement is never overwritten. `RepositoryDirty` is reserved for a real whole-repository cleanliness precondition failure, such as an unrelated dirty path outside an enclosing-project pathspec. `OwnershipLost` and `RecoveryFailed` remain internal transition states; at the coordinator boundary either becomes exactly `RemoteOpResult.Failed(Strings.VersionControl_PullTransitionUncertain)`, without composing potentially misleading inner remote-result text. Checkpoint ref publication is re-observed after a lost `update-ref` response, so a ref that Git durably created is still returned to the coordinator instead of becoming an unreachable orphan. The private checkpoint ref is deleted only after successful reopen or a fully verified recovery, and is retained when recovery cannot prove completion.

After rollback/checkpoint restoration, the coordinator re-reads the attached branch tip immediately before reopening and requires the exact captured original tip. This check is the recovery cycle's ownership linearization point: a mismatch leaves the project closed and the checkpoint reachable. A later external Git write is a new operation outside Beutl's transaction and is observed through the repository watcher; Beutl never rewrites that external result.

If a restore commit succeeds but reopening fails, recovery restores the captured pre-operation tree and records a `Recovery` commit on top. The attempted restore remains in history and the original project state becomes the visible tip again without rewriting history.

Push runs outside the cycle (no work-tree mutation): progress dialog + cancel only.

Guard: standard render exports and project-package exports acquire a shared output lease before reading project files. Restore, branch-switch, and pull acquire the corresponding exclusive work-tree lease before confirmation and hold it through close, mutation, recovery, and reopen. Either side fails immediately when the other is active, so an export cannot start in the confirmation window; snapshots (which don't move files) remain independent and only capture completed atomic writes.

Application-window shutdown uses the same asynchronous close contract. The first window-closing event is canceled, one shared pipeline awaits the close snapshot, project close, and proxy drain under a single 15-second deadline, and then issues one final `Close()`. Repeated closing events join the same pipeline; timeout or failure is logged before the final close proceeds, and any cleanup that finishes after the deadline remains observed.

## Enablement flows (FR-001/FR-002/FR-003)

- **Create dialog**: `CreateNewProjectViewModel` depends on `IProjectVersionControlInitializer`, which exposes availability and project initialization without coupling the dialog to the app coordinator. `InitializeCurrentProjectAsync` accepts `Func<CancellationToken, Task<GitIdentity?>>` and forwards its exact operation token to the identity prompt, so cancellation is not lost at the UI callback boundary. The identity flyout registers that token, cancels its pending result, and closes itself on the UI thread rather than waiting for user dismissal. "Track history with Git" remains false and hidden until `GetAvailabilityAsync` reports `Installed`; only then is the configured default applied and shown. Creation snapshots that visible checked state before writing the project, so a detection completion during creation can never opt the user in silently. A checked visible option calls `InitializeCurrentProjectAsync` after creation.
- **Existing project**: "Enable Version Control…" command (Project menu + command palette, gated on `ProjectService.IsOpened`).
- **Nested repo detected**: consent dialog with "use enclosing repository" (pathspec scoping, project-local `.gitignore`) / "leave unmanaged". Never `git init` inside a foreign work tree.
- **Save As**: never copies `.git`; the copy is offered fresh enablement per the creation default (clarification #3).

## UI surface map

| Surface | Location | Content |
|---|---|---|
| Tool tab | `src/Beutl.Editor.Components/VersionControlTab/` + `VersionControlTabExtension` (`[PrimitiveImpl]`, registered in `LoadPrimitiveExtensionTask`) | branch + ahead/behind + dirty summary; commit box; paged history list (kind badges); changed files; unified diff view (monospace, +/- coloring, 1 MB cap) |
| Menus | `MenuBarViewModel.Files.cs` + `MainView.axaml` (+ macOS mirror) + command palette | Enable Version Control…, Commit…, Push, Pull |
| Settings | `VersionControlConfig` page | per data-model.md table |
| Degradation | tool tab + menu items collapse to one informational state | per-OS install guidance (FR-037) |

All new XAML declares `x:CompileBindings="True"` + `x:DataType` (constitution IV). All user-facing strings go through `Beutl.Language` resources; repository content stays English (R-5).
