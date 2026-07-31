# Contract: VersionControlCoordinator lifecycle & UI orchestration

**Scope**: `src/Beutl/Services/VersionControlCoordinator.cs` — the app-level owner of per-project services and the only component allowed to run the close→operate→reopen cycle.

## Ownership

- Constructed once in `MainViewModel` next to `ProjectService`.
- Subscribes `ProjectService.ProjectObservable`: on project open → resolve project root from `Project.Uri`, run repo discovery (`git rev-parse --show-toplevel`), construct `GitCliVersionControlService` + `RepositoryWatcher`; on close → dispose both.
- Exposes the current service to `EditViewModel.GetService` (single instance shared by all scene tabs of the project).

## Commit trigger wiring (FR-012/013/014/015)

| Trigger | Hook point | Kind |
|---|---|---|
| Explicit Save / Save All | end of `MenuBarViewModel.OnSave` / `OnSaveAll` → `NotifySavedAsync()` | `Save` |
| Project close | start of the close flow, after final save, before `ProjectService.CloseProject()` | `Close` |
| Before restore / branch switch / pull | inside the cycle, when status is dirty | `Safety` |
| After restore | inside the cycle | `Restore` |
| Manual commit | tool tab / menu command | `Manual` |

Autosave ticks never reach the coordinator (FR-015). All triggers no-op silently on a clean tree.

## The close→operate→reopen cycle (FR-022)

```text
1. Confirm dialog (operation-specific; discloses close/reopen + undo-history loss)
2. If dirty: CommitAllAsync(safety message, Safety)
3. ProjectService.CloseProject()
4. Git operation (RestoreWorktreeFromAsync / SwitchBranchAsync / PullFastForwardAsync)
   └─ on failure: surface stderr verbatim; attempt reopen of the original state
      (original branch for switch; work tree is untouched on failed ff-only pull)
5. For restore: CommitAllAsync(restore message, Restore)
6. ProjectService.OpenProject(bepPath)
```

Push runs outside the cycle (no work-tree mutation): progress dialog + cancel only.

Guard: the cycle refuses to start while an export/render job is reading project files (checked via the output service's active-jobs state); snapshots (which don't move files) are allowed concurrently and only ever capture completed atomic writes.

## Enablement flows (FR-001/FR-002/FR-003)

- **Create dialog**: `CreateNewProjectViewModel` depends on `IProjectVersionControlInitializer`, which exposes availability and project initialization without coupling the dialog to the app coordinator. "Track history with Git" is visible when `GetAvailabilityAsync` = `Installed`, defaults from `VersionControlConfig.EnableForNewProjects` (default true), and calls `InitializeCurrentProjectAsync` after project creation when checked.
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
