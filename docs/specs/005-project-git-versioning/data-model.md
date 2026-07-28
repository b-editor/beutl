# Data Model: Git Version Control for Editing Projects

**Feature**: 005-project-git-versioning | **Date**: 2026-07-28

All types live in `Beutl.Editor.VersionControl` (project `src/Beutl.Editor/`, Avalonia-free) unless noted. Types are immutable records unless stated otherwise.

## GitAvailability

Result of probing the machine for git tooling.

| Field | Type | Notes |
|---|---|---|
| `State` | `GitAvailabilityState` | `Installed` / `NotInstalled` / `VersionTooOld` |
| `GitPath` | `string?` | Resolved executable path when installed |
| `Version` | `Version?` | Parsed from `git --version`; floor is 2.23 (needs `git switch`/`git restore`) |
| `LfsInstalled` | `bool` | `git lfs version` succeeded |

## RepositoryInfo

Identity of the repository serving one open project. `null` on the service ⇒ project not under version control (or git unavailable).

| Field | Type | Notes |
|---|---|---|
| `RepoRoot` | `string` | Absolute path of the repository work-tree root |
| `ProjectRoot` | `string` | Absolute path of the directory containing the `.bep` |
| `IsNestedInForeignRepo` | `bool` | `RepoRoot` ≠ `ProjectRoot` (enclosing repo the user opted into) |
| `Pathspec` | `string` | `"."` for a dedicated repo; project directory relative to `RepoRoot` when nested. Every status/add/log/restore call is scoped with it |

**Invariant**: `ProjectRoot` is always equal to or below `RepoRoot`. Operations never touch paths outside `Pathspec` (FR-003).

## SnapshotKind

`enum`: `Manual` | `Save` | `Close` | `Safety` | `Restore` | `Init`.

Persisted in the repository as the commit trailer `Beutl-Snapshot: save|close|safety|restore|init` (absent ⇒ `Manual`, including commits made by external tools). UI badges/localization derive from this — never from the subject text (FR-016).

## CommitInfo

One entry in the history list.

| Field | Type | Notes |
|---|---|---|
| `Sha` / `ShortSha` | `string` | |
| `Subject` | `string` | Raw subject; shown verbatim for `Manual`, localized display for auto kinds |
| `AuthorName` | `string` | |
| `AuthorDate` | `DateTimeOffset` | |
| `Kind` | `SnapshotKind` | Parsed from trailer |

## FileChange

| Field | Type | Notes |
|---|---|---|
| `Path` | `string` | Repo-relative |
| `Status` | `FileChangeStatus` | `Added` / `Modified` / `Deleted` / `Renamed` |
| `OldPath` | `string?` | For renames |

## WorkspaceStatus

Snapshot of the current repo state, produced by one `git status --porcelain=v2 -z` (+ branch/ahead-behind headers).

| Field | Type | Notes |
|---|---|---|
| `Branch` | `string?` | `null` only in the rejected detached case (defensive; UI shows a warning) |
| `Ahead` / `Behind` | `int` | vs upstream; 0 when no upstream |
| `Changes` | `IReadOnlyList<FileChange>` | Scoped to `Pathspec` |
| `HasConflicts` | `bool` | Unmerged paths present ⇒ service enters `Conflicted` (FR-033) |
| `IsClean` | `bool` | Derived: no changes |

**State transitions** (service level): `NotARepo → Ready` (InitializeAsync / opening a tracked project) ; `Ready → Conflicted` (unmerged paths detected) ; `Conflicted → Ready` (external resolution observed on refresh). There is no in-app transition into `Conflicted` — only external tools can create it.

## CommitResult / RemoteOpResult

- `CommitResult`: `NoChanges` | `Committed(string Sha)` | `SkippedNoIdentity` (auto-triggers only; one-time warning surfaced).
- `RemoteOpResult`: `Success` | `AuthFailed(string Guidance)` | `Diverged` | `Offline` | `Failed(string Stderr)` — each maps to a distinct actionable message (FR-031/FR-032, edge cases).

## BranchInfo / RemoteInfo / GitIdentity

- `BranchInfo`: `Name`, `IsCurrent`, `UpstreamName?`.
- `RemoteInfo`: `Name` (always `origin` in v1), `Url`.
- `GitIdentity`: `Name`, `Email`; `null` from `GetIdentityAsync` ⇒ unset (triggers the one-time prompt, stored repo-local — FR-004).

## VersionControlConfig (`src/Beutl.Configuration/`, mutable `ConfigurationBase`)

| Property | Type | Default | Maps to |
|---|---|---|---|
| `EnableForNewProjects` | `bool` | `true` | Creation-dialog checkbox default (clarification #1) |
| `AutoCommitOnSave` | `bool` | `true` | FR-012 |
| `AutoCommitOnClose` | `bool` | `true` | FR-013 |
| `GitExecutablePath` | `string?` | `null` | Discovery override (R-3) |
| `UseLfsWhenAvailable` | `bool` | `true` | FR-035 (clarification #4) |
| `LargeMediaWarningThresholdMb` | `int` | `50` | FR-035 warning without LFS |

## Repository content contracts (on-disk)

- **Generated `.gitignore`** (project root; also written inside the project dir in the nested case): `**/.beutl/`, `*.tmp`.
- **Generated `.gitattributes`**: `*.bep` / `*.scene` / `*.belm` / `.gitignore` / `.gitattributes` → `text eol=lf`; when LFS active: `resources/**` media patterns → `filter=lfs diff=lfs merge=lfs -text`.
- **Commit message**: subject per R-5; trailer `Beutl-Snapshot: <kind>` for auto commits.

## Relationships

```text
VersionControlCoordinator (src/Beutl/, app-level, 1 per open project)
 ├─ owns → IProjectVersionControlService (GitCliVersionControlService)
 │          ├─ RepositoryInfo (identity, pathspec scoping)
 │          ├─ GitCliRunner (process contract, R-2)
 │          ├─ RepositoryWatcher (debounced status refresh, R-8)
 │          └─ emits WorkspaceStatus via StatusChanged (background thread)
 ├─ subscribes → ProjectService.ProjectObservable (create/dispose per project)
 └─ orchestrates → close → git op → reopen cycles (restore / switch / pull)

VersionControlTabViewModel (src/Beutl.Editor.Components/)
 └─ resolves IProjectVersionControlService via IEditorContext.GetService
    (EditViewModel switchboard; all scene tabs of one project share the instance)
```

## Element file naming (prerequisite fix, `Beutl.Editor`)

`ElementFileNaming.GetUri(sceneUri, elementId)` → `{Id:N}.belm`; on collision append `-{index}` (matches `DeclarativeDocumentApplier`). Replaces `RandomFileNameGenerator` at the five GUI call sites (R-10.1). Existing files are never renamed.
