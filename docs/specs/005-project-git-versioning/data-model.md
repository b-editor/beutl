# Data Model: Git Version Control for Editing Projects

**Feature**: 005-project-git-versioning | **Date**: 2026-07-28

All types live in `Beutl.Editor.VersionControl` (project `src/Beutl.Editor/`, Avalonia-free) unless noted. Types are immutable records unless stated otherwise.

## GitAvailability

Result of probing the machine for git tooling.

| Field | Type | Notes |
|---|---|---|
| `State` | `GitAvailabilityState` | `Installed` / `NotInstalled` / `VersionTooOld` |
| `GitPath` | `string?` | Resolved executable path when installed |
| `Version` | `Version?` | Parsed from `git --version`; floor is 2.23 (needs `git switch`, worktree, and current plumbing behavior) |
| `LfsInstalled` | `bool` | `git lfs version` succeeded |

## RepositoryInfo

Identity of the repository serving one open project. `null` on the service ⇒ project not under version control (or git unavailable).

| Field | Type | Notes |
|---|---|---|
| `RepoRoot` | `string` | Absolute path of the repository work-tree root |
| `ProjectRoot` | `string` | Absolute path of the directory containing the `.bep` |
| `IsNestedInForeignRepo` | `bool` | `RepoRoot` ≠ `ProjectRoot` (enclosing repo the user opted into) |
| `Pathspec` | `string` | `"."` for a dedicated repo; project directory relative to `RepoRoot` when nested. Project status/history/snapshot/tree operations use it |

**Invariant**: `ProjectRoot` is always equal to or below `RepoRoot`. Project-content operations never stage or restore paths outside `Pathspec`; disclosed repository-level branch, push, pull, cleanliness checks, and expected-old ref updates apply to the enclosing repository (FR-003).

## SnapshotKind

`enum`: `Manual` | `Save` | `Close` | `Safety` | `Restore` | `Recovery` | `Init`.

Persisted in the repository as the commit trailer `Beutl-Snapshot: save|close|safety|restore|recovery|init` (absent ⇒ `Manual`, including commits made by external tools). `Recovery` records a compensating commit after an attempted restore committed successfully but its project reopen failed. UI badges/localization derive from this — never from the subject text (FR-016).

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

**Repository-state transitions**: `NotARepo → Ready` (initialization / opening a tracked project); `Ready → Conflicted` (unmerged paths detected); `Conflicted → Ready` (external resolution observed on refresh). There is no in-app transition into `Conflicted` — only external tools can create it.

**Backend lifetime transitions**: `Active → Retiring → Retired`. Starting retirement immediately rejects new mutations, waits for the current exclusive transaction, optionally records the final close snapshot, then disposes the watcher and other resources. `Retired` is terminal. A temporary close during a coordinator cycle hides the public service without retiring the owned backend.

## CommitResult / RemoteOpResult

- `CommitResult`: `NoChanges` | `Committed(CommitRevision Revision)` | `SkippedNoIdentity` (auto-triggers only; one-time warning surfaced). `CommitRevision` is `Known(string Sha)` or `Unavailable`; the latter means `git commit` succeeded but the best-effort post-commit revision lookup failed, so callers must not retry the commit.
- `RemoteOpResult`: `Success` | `AuthFailed(string Guidance)` | `Diverged` | `Offline` | `RepositoryDirty` | `Failed(string Stderr)` — each maps to a distinct actionable message (FR-031/FR-032, edge cases). `RepositoryDirty` is reserved for a failed whole-repository cleanliness precondition; it never represents ownership loss or an unverified recovery.

## CheckedOutBranchTip / ProjectCheckpoint

- `CheckedOutBranchTip(RefName, Commit)` identifies one attached local branch and its exact commit. Detached HEAD is not a valid input to a close/reopen mutation cycle.
- `ProjectCheckpoint(RefName, Commit, BaseTip)` identifies a commit reachable through `refs/beutl/safety/*`. It captures the project pathspec with a temporary index while leaving the checked-out branch, working tree, and user's index unchanged.
- A checkpoint is valid only while its ref resolves to the recorded commit, that commit's first parent equals `BaseTip.Commit`, and the same local branch remains checked out. Branch rollback uses the recorded ref plus expected-old commit as one compare-and-swap.

## PullTransitionState

Internal result state returned with a fast-forward pull:

- `Unchanged`: no durable branch/tree transition remains; normal recovery/reopen is safe.
- `Applied`: the exact target tree/index was prepared and the expected-old branch CAS reached the target.
- `OwnershipLost`: an external ref, worktree, or index update invalidated Beutl's captured ownership; Beutl does not overwrite it.
- `RecoveryFailed`: mutation started and the backend could not verify either the target or restored original state.

`OwnershipLost` and `RecoveryFailed` leave the project closed and retain any private checkpoint. They remain distinct internally because the coordinator must not attempt a second rollback against uncertain ownership; only at the public coordinator boundary are both rendered as the exact localized uncertain-transition `Failed` result, without inner result text.

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

All six values are editable from the existing Editor Settings page; blank executable input restores automatic Git discovery, and the media threshold is clamped to at least 1 MB.

## Repository content contracts (on-disk)

- **Generated `.gitignore`** (project root; also written inside the project dir in the nested case): `**/.beutl/`, `*.tmp`.
- **Generated `.gitattributes`**: `*.bep` / `*.scene` / `*.belm` / `.gitignore` / `.gitattributes` → `text eol=lf`; when LFS active: `resources/**` media patterns → `filter=lfs diff=lfs merge=lfs -text`.
- **Commit message**: subject per R-5; trailer `Beutl-Snapshot: <kind>` for auto commits.

## Relationships

```text
VersionControlCoordinator (src/Beutl/, app-level, 1 per open project)
 ├─ owns → IProjectVersionControlBackend (GitCliVersionControlService)
 │          ├─ RepositoryInfo (identity, pathspec scoping)
 │          ├─ GitCliRunner (process contract, R-2)
 │          ├─ RepositoryWatcher (debounced status refresh, R-8)
 │          └─ emits WorkspaceStatus via StatusChanged (background thread)
 ├─ exposes → IProjectVersionControlService (read/query only)
 ├─ mutates → IProjectVersionControlTransaction (exclusive, non-retainable)
 ├─ subscribes → ProjectService.ProjectObservable (create/dispose per project)
 └─ orchestrates → close → git op → reopen cycles (restore / switch / pull)

VersionControlTabViewModel (src/Beutl.Editor.Components/)
 ├─ observes IProjectVersionControlService for status/history/diff queries
 └─ resolves IProjectVersionControlCoordinator for mutations
    (EditViewModel switchboard; all scene tabs of one project share the instances)
```

## Element file naming (prerequisite fix, `Beutl.Editor`)

`ElementFileNaming.GetUri(sceneUri, elementId)` → `{Id:N}.belm`; on collision append `-{index}` (matches `DeclarativeDocumentApplier`). Replaces `RandomFileNameGenerator` at the six GUI call sites (R-10.1). Existing files are never renamed.
