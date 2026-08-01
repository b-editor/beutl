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
1. Read-only backend preflight while the project stays open
   └─ return immediately when pull is already up to date or cannot proceed
2. Release the backend gate, then show the operation confirmation
3. Acquire ProjectService's transition gate and the work-tree lease
4. Reacquire the backend gate and revalidate status, branch tip, upstream, and operation need
5. If dirty:
   - restore / branch switch: CommitAllAsync(safety message, Safety)
   - pull: create a durable refs/beutl/safety/* checkpoint without moving the branch
6. ProjectService.CloseProject()
7. Git operation inside the mutation-phase `ExecuteExclusiveAsync` transaction
   └─ on failure: recover the operation-specific original state, then surface the error
      (original branch/work tree for switch; compensating Recovery commit for restore;
       exact branch-tip CAS plus checkpoint restore for pull)
8. For restore: apply the selected tree and append a Restore commit atomically
   For dirty pull: apply the checkpoint tree and append a Safety commit atomically
9. ProjectService.OpenProject(bepPath)
```

The two backend phases never invert the normal-close lock order. Read-only preflight releases the backend gate before requesting the project transition; the mutation phase always holds the project transition before reacquiring the backend gate. Confirmation dialogs hold neither gate. A concurrent normal close can therefore retire the backend and complete without deadlocking against pull or pending-recovery confirmation.

Pull recovery captures the checked-out local branch ref and commit before closing. Immediately before the first guarded tree/ref transition, a second durable descriptor ref under `refs/beutl/recovery/<project-path-hash>/<id>` records the checkpoint ref, exact branch/base/target commits, project file, and creation time. It only rolls that same ref back when its current commit still equals the operation's expected commit; a concurrent external branch movement is never overwritten. `RepositoryDirty` is reserved for a real whole-repository cleanliness precondition failure, such as an unrelated dirty path outside an enclosing-project pathspec. `OwnershipLost` and `RecoveryFailed` remain internal transition states; at the coordinator boundary either becomes exactly `RemoteOpResult.Failed(Strings.VersionControl_PullTransitionUncertain)`, without composing potentially misleading inner remote-result text. Checkpoint and descriptor ref publication are re-observed after a lost `update-ref` response, so a ref that Git durably created is still returned to the coordinator instead of becoming an unreachable orphan. The descriptor and private checkpoint are deleted together by one compare-and-swap ref transaction only after successful reopen or a fully verified recovery, and both are retained when completion cannot be proved.

Repository activation enumerates pending descriptors even when the Version Control tab has never been opened. A continuously present descriptor ID is offered at most once during the active service session; IDs that complete or disappear are removed from the deduplication set, while an explicit Recent Projects open may offer the same durable descriptor again. The offer is canceled when its project/service generation is replaced or normally closed, so stale prompts never overlap a later activation. Enumeration and confirmation hold no backend gate. Direct pull and manual-recovery confirmation capture the project/service epoch before preflight or lookup and use that same cancellation token through confirmation, so close, branch transition, and backend replacement cancel a stale prompt without holding a lifecycle, project, or backend gate. If accepted, the normal recovery cycle reacquires the project transition first, then the backend gate, re-enumerates the exact ID and descriptor object, verifies the open project path again, closes, rolls an already-applied target back to its exact base when necessary, restores the saved checkpoint, reopens, and atomically completes both refs only after successful project publication. Explicit opens use a per-attempt preparation: descriptor discovery and confirmation run before `ProjectService` acquires its transition, then the immutable ticket is applied inside that transition only after acquiring the work-tree lease, rediscovering the same repository, and matching the exact descriptor object and project path. An already-applied ticket also captures the exact live opening-marker object; apply skips recovery only while that same marker instance still names the same repository and recovery. Missing or replaced markers abort rather than falling back to another recovery, required-ID misses never clear an unrelated marker, and ordinary stale-marker cleanup uses reference-identity compare-and-remove. A superseded attempt, changed descriptor, unavailable preparation, busy work tree, or accepted recovery failure aborts before the current project is closed. A physically escaping project alias vetoes open only when it belongs to a matching pending recovery or an internal version-control reopen; unrelated explicit project symlink opens preserve their prior behavior. `ProjectRecoveryResult` reports the exact disposition, and only its two success cases remove the non-overlay recovery banner. Declined, unavailable, changed, verified-preserved failure, or uncertain failure results keep the action visible, including while conflict guidance is visible.

After rollback/checkpoint restoration, the coordinator re-reads the attached branch tip immediately before reopening and requires the exact captured original tip. This check is the recovery cycle's ownership linearization point: a mismatch leaves the project closed and the checkpoint reachable. A later external Git write is a new operation outside Beutl's transaction and is observed through the repository watcher; Beutl never rewrites that external result.

If a restore commit succeeds but reopening fails, recovery restores the captured pre-operation tree and records a `Recovery` commit on top. The attempted restore remains in history and the original project state becomes the visible tip again without rewriting history.

Push runs outside the cycle (no work-tree mutation): progress dialog + cancel only.

Guard: standard render exports and project-package exports acquire a shared output lease before reading project files. Restore and branch-switch acquire the corresponding exclusive work-tree lease before confirmation and hold it through close, mutation, recovery, and reopen. Pull and pending-recovery confirmation first release the backend gate, then acquire the exclusive work-tree lease with the project transition before their revalidation/mutation phase. Either side fails immediately when the other is active; snapshots (which don't move files) remain independent and only capture completed atomic writes.

Application-window shutdown uses the same asynchronous close contract. The first window-closing event is canceled, one shared pipeline awaits the close snapshot, project close, and proxy drain under a single 15-second deadline, and then issues one final `Close()`. Repeated closing events join the same pipeline; timeout or failure is logged before the final close proceeds, and any cleanup that finishes after the deadline remains observed.

## Enablement flows (FR-001/FR-002/FR-003)

- **Create dialog**: `CreateNewProjectViewModel` requires `IProjectVersionControlInitializer` and the identity callback; there is no degraded constructor that silently omits version control. The initializer exposes availability and project initialization without coupling the dialog to the app coordinator. `InitializeCurrentProjectAsync` accepts `Func<CancellationToken, Task<GitIdentity?>>` and forwards its exact operation token to the identity prompt, so cancellation is not lost at the UI callback boundary. The identity flyout registers that token, cancels its pending result, and closes itself on the UI thread rather than waiting for user dismissal. "Track history with Git" remains false and hidden until `GetAvailabilityAsync` reports `Installed`; only then is the configured default applied and shown. Creation snapshots that visible checked state before writing the project, so a detection completion during creation can never opt the user in silently. A checked visible option calls `InitializeCurrentProjectAsync` after creation.
- **Existing project**: "Enable Version Control…" command (Project menu + command palette, gated on `ProjectService.IsOpened`).
- **Menu lifecycle**: `MenuBarViewModel` depends on `IProjectVersionControlSession` only for read-only availability/tracking state and save notification. Project close remains the responsibility of the existing `ProjectService`; external hosts can substitute the version-control session without duplicating the general project-lifecycle surface.
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
