# Contract: IProjectVersionControlService

**Scope**: the read/query seam consumed by the tool tab and exposed to plugin authors through `IEditorContext.GetService`. Lives in `Beutl.Editor.VersionControl` (Avalonia-free). `VersionControlCoordinator` owns one internal backend per open project and is the public surface for user-level version-control mutations; the separate narrow `IRepositoryLockRecoveryService` capability remains responsible only for consented stale-lock removal.

```csharp
public interface IProjectVersionControlService
{
    RepositoryInfo? Repository { get; }

    Task<GitAvailability> GetAvailabilityAsync(CancellationToken ct);
    Task<WorkspaceStatus> GetStatusAsync(CancellationToken ct);
    Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(string sha, CancellationToken ct);
    Task<string> GetDiffAsync(string sha, string? path, CancellationToken ct);
    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct);
    Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(CancellationToken ct);
    Task<GitIdentity?> GetIdentityAsync(CancellationToken ct);

    event EventHandler<WorkspaceStatus>? StatusChanged;
}
```

The public initialization seam uses the same cancellation contract:

```csharp
public interface IProjectVersionControlInitializer
{
    Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<bool> InitializeCurrentProjectAsync(
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync,
        CancellationToken cancellationToken);
}
```

The coordinator passes the exact `InitializeCurrentProjectAsync` operation token to `requestIdentityAsync`. The previous parameterless callback is not retained as an overload or compatibility shim.

Mutation is split into two internal surfaces:

- `IProjectVersionControlBackend` owns discovery, initialization, snapshots, remote and identity updates, retirement, and `ExecuteExclusiveAsync`.
- `IProjectVersionControlTransaction` is available only inside `ExecuteExclusiveAsync`. It owns branch/tree transitions, pull checkpoints, guarded branch-tip rollback, and checkpoint deletion. Callers cannot retain it or interleave another mutation halfway through a lifecycle cycle.

The public `IProjectVersionControlCoordinator` exposes user-level mutations such as commit, restore, branch operations, identity/remote changes, push, and pull. It also exposes pending-pull recovery enumeration/action plus a change signal using the intentionally opaque `ProjectRecoveryInfo` (`Id`, display file name, creation time); backend refs, commits, and mutation primitives remain internal. Recovery returns `ProjectRecoveryResult`: `RestoredOriginal` and `ReappliedCheckpoint(recoveryBranchName)` are successful dispositions, while `Declined`, `NotFoundOrChanged`, `Unavailable`, `FailedPreserved(recoveryReference)`, and `FailedUncertain` keep the pending action visible. `FailedPreserved` is returned only after the named Git reference is re-observed at the expected checkpoint commit; a generic close, recovery, reopen, completion, or reference-verification failure returns `FailedUncertain` and makes no durability claim. It couples those mutations to dialogs, output leases, project close/reopen, and recovery instead of exposing backend primitives to plugins. The narrower `IProjectVersionControlSession` supplies menu state and save integration without forcing non-editor consumers to implement the full mutation surface; general project close remains on `ProjectService`.

## Behavioral guarantees

1. **Serialization, lock order, and lifetime**: backend work serializes on one internal gate. Coordinator operations may use a short read-only preflight phase, release the backend gate for confirmation, then acquire the project transition before reacquiring the backend gate for the complete close/mutate/recover/reopen phase. Normal close uses the same project-transition→backend order, so no path waits for the project transition while holding the backend gate. Retirement changes the backend from active to retiring, waits for the exclusive owner, optionally records the final snapshot, then enters a terminal retired state; no queued mutation can start afterward.
2. **Pathspec scoping**: ordinary project-content commands (`add`, `status`, `log`, `show`, and scoped index restore) append `-- {Repository.Pathspec}`. In the nested-repository case no file outside the project directory is staged or restored by project operations. `Branch*`/`Push`/`Pull` and branch-tip compare-and-swap act on the whole repository (disclosed by the UI). Checkpointed pull requires unrelated repository state to be clean and returns `RepositoryDirty` only when that cleanliness precondition fails. `OwnershipLost` and `RecoveryFailed` are transition states, not dirty-repository diagnoses. Standalone checkpoint/restore transitions remain pathspec-scoped and preserve unrelated outside staging.
3. **`CommitAllAsync`**: checks status first; returns `NoChanges` without creating a commit when clean (FR-014). Automatic kinds with unset identity return `SkippedNoIdentity`; the coordinator resolves identity before a manual commit. Staging uses `git add -A -- <pathspec>` and non-`Manual` kinds write the `Beutl-Snapshot` trailer. Once `git commit` succeeds, post-commit revision lookup and status publication are best-effort and cannot reverse the durable result; a failed revision lookup returns `Committed(CommitRevision.Unavailable)` so callers do not retry the commit. `CommitRevision.Known` always contains a non-empty SHA; nullable or empty-string sentinels are forbidden.
4. **Project checkpoints and restart recovery**: `CreateProjectCheckpointAsync` uses a temporary index to write the complete project pathspec to a commit referenced by `refs/beutl/safety/<project-path-hash>/<id>`; it does not move the checked-out branch or alter the user's index/worktree. Before a dirty pull's first guarded transition, the backend writes a strict JSON blob and publishes `refs/beutl/recovery/<project-path-hash>/<id>` to that blob. The descriptor records its version/ID, exact checkpoint ref+commit, attached branch ref, base+target commit, project-relative `.bep` path, and timestamp. Enumeration treats every descriptor as untrusted: object IDs must be full validated OIDs; ref suffixes must be one exact `Guid` component; checkpoint path hashes must match the current project; branch refs reject every Git-forbidden character (including space); and project paths must be lexically rooted beneath the project root. Physical symlink containment is deliberately checked at persistence and again after a prepared Git tree but before its ref update/reopen, so a target-created escape remains recoverable by descriptor yet is never opened. Invalid descriptors are logged and skipped without becoming Git revision arguments. Pull revalidates the checkpoint ref, its first parent, the recorded base branch tip, and the project-state fingerprint, builds a merged tree and Safety commit without moving the branch, then applies that exact tree through the guarded transition. Callers never observe an intermediate mutation primitive. Completion uses one `update-ref --stdin` transaction with expected-old values for both descriptor and checkpoint; any CAS change retains both refs and reports a changed recovery instead of partially deleting evidence. If Git commits both deletions but the process response is lost, re-observing both refs as absent accepts the durable completion instead of reporting a nonexistent preserved reference.
5. **Restore transaction**: the coordinator records a Safety snapshot only when the project pathspec is dirty, closes the project, applies the selected tree, and appends a Restore commit. If a later step fails after that commit, the backend applies the captured original tree and appends a Recovery commit; it does not erase the attempted restore or rewrite history.
6. **Branch and pull transactions**: the project must be closed before a tree transition. Pull accepts only a fast-forward. A transition holds the worktree-private `HEAD.lock`, validates the attached ref and scoped worktree/index fingerprints, updates the captured worktree/private index through a protected branch-mode checkout, and compare-and-swaps the same branch from the exact expected commit to the target as its final durable step. The checkout and CAS run from a temporary detached/no-checkout worktree because Git's checked-out-branch update path otherwise contends with the original `HEAD.lock`; environment overrides point the checkout at the original worktree/index while only the temporary HEAD moves. Git therefore rejects late tracked, untracked, and ignored collisions without detaching the user's project HEAD. Recovery observes and, when necessary, expected-old-aligns that temporary HEAD before reversing an exact target state; unknown worktree content is not overwritten, while a proven Beutl-owned prepared index is restored. The temporary context is cleaned up best-effort. An external ref movement yields `OwnershipLost` and is never overwritten. If the backend cannot prove either the target or restored original state, it yields `RecoveryFailed`; the coordinator keeps the project closed, retains its checkpoint, and maps either internal state to exactly `Failed(VersionControl_PullTransitionUncertain)` without composing the backend's inner result text.
7. **Local destructive phases**: after their cancellable preflight, guarded tree transitions, checkpoint restore/delete, and branch-tip rollback run to a verified boundary without accepting cancellation. This keeps the private ref reachable and prevents cancellation from exposing a half-applied local transaction.
8. **Conflict lockout**: when `WorkspaceStatus.HasConflicts`, every mutation is refused with conflict guidance while read members (`GetStatusAsync`, `GetHistoryAsync`, etc.) keep working (FR-033).
9. **`StatusChanged`**: publication is best-effort after durable mutations and debounced watcher refreshes. Each subscriber is isolated so one callback cannot fail the operation or suppress later subscribers; consumers marshal to the UI thread themselves.
10. **Cancellation**: cancellable operations kill the underlying git process; the repository is left in a state git itself considers consistent. A killed network operation is followed by coordinator recovery from the captured branch tip and optional durable checkpoint. Identity callbacks receive the exact operation token so cancellation also reaches an in-progress initialization prompt; the prompt cancels its pending result and closes its flyout on the UI thread.
11. **Errors**: git non-zero exits surface as `GitOperationException { ExitCode, Stderr }` with stderr preserved for the error dialog after credentials embedded in URLs are redacted; remote operations map expected outcomes, including an unrelated-dirty-repository refusal, to `RemoteOpResult` instead of throwing.

## Exposure

- `EditViewModel.GetService(typeof(IProjectVersionControlService))` returns the coordinator's visible read/query service for the open project; temporary close publishes `null` without surrendering backend ownership.
- The tool tab observes `IReadOnlyReactiveProperty<IProjectVersionControlService?>` for queries and resolves `IProjectVersionControlCoordinator` for mutations.
- Plugin callers cannot cast the public service to the internal backend or transaction interfaces.
