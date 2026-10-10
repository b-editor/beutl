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
- `IProjectVersionControlTransaction` is available only inside `ExecuteExclusiveAsync`. It owns the pull, restore, and branch operations that run while the project is closed. Callers cannot retain it or interleave another mutation halfway through a lifecycle cycle.

The public `IProjectVersionControlCoordinator` exposes user-level mutations such as commit, restore, branch operations, identity/remote changes, push, and pull; backend refs, commits, and mutation primitives remain internal. It couples those mutations to dialogs, output leases, project close/reopen, and recovery instead of exposing backend primitives to plugins. The narrower `IProjectVersionControlSession` supplies menu state and save integration without forcing non-editor consumers to implement the full mutation surface; general project close remains on `ProjectService`.

## Behavioral guarantees

1. **Serialization, lock order, and lifetime**: backend work serializes on one internal gate. Coordinator operations may use a short read-only preflight phase, release the backend gate for confirmation, then acquire the project transition before reacquiring the backend gate for the complete close/mutate/recover/reopen phase. Normal close uses the same project-transition→backend order, so no path waits for the project transition while holding the backend gate. Retirement changes the backend from active to retiring, waits for the exclusive owner, optionally records the final snapshot, then enters a terminal retired state; no queued mutation can start afterward.
2. **Pathspec scoping**: ordinary project-content commands (`add`, `status`, `log`, `show`, and scoped index restore) append `-- {Repository.Pathspec}`. In the nested-repository case no file outside the project directory is staged or restored by project operations. With Git LFS installed, the cancellable prefetch inspects the transition target even when the current project has no LFS paths. Restore uses `Repository.Pathspec/**` as its fetch include; because Git LFS parses that option as comma-separated gitignore globs, a path that cannot be represented literally is resolved with `git ls-tree` and LFS scans that exact project subtree object without a path filter. Branch and pull prefetches clear both LFS path filters and fetch the whole repository transition target. Every prefetch disables `lfs.fetchrecentalways` for that invocation, so configured recent branches cannot widen its explicit target. If fetch fails or no remote exists, the bounded target listing must contain canonical SHA-256 OIDs and every cached object must stream-hash to its OID before the project may close; missing, corrupt, malformed, or truncated evidence fails closed. `Branch*`/`Push`/`Pull` act on the whole repository (disclosed by the UI). The pull preflight requires unrelated repository state outside an enclosing project to be clean and returns `RepositoryDirty` only when that precondition fails. The pull stash, restore, and its rollback use the snapshot pathspecs and preserve unrelated outside staging.
3. **`CommitAllAsync`**: checks status first; returns `NoChanges` without creating a commit when clean (FR-014). Automatic kinds with unset identity return `SkippedNoIdentity`; the coordinator resolves identity before a manual commit. The built-in Git backend stages the project scope with `git add -A` and records it with `git commit` (with `--only` on the project pathspecs when something else is staged); Git runs the hooks and updates the branch and index under its own locks. Every kind writes the `Beutl-Snapshot` trailer. The built-in backend returns `Committed(CommitRevision.Known)` with HEAD as read after the commit; `Unavailable` remains a defensive public result state for alternative coordinator implementations.
4. **Restore transaction**: the coordinator records a Safety snapshot only when the project pathspec is dirty, closes the project, and calls `RestoreProjectTreeAsync`, which runs `git restore --source=<commit> --staged --worktree` on the project scope (files the commit lacks are deleted) and records a Restore snapshot. A failure before that commit puts the scope back to HEAD. If reopening fails after the commit, the coordinator restores the captured original tree as a Recovery snapshot; it does not erase the attempted restore or rewrite history.
5. **Branch and pull transactions**: the project must be closed first. Branch switch is `git switch`. Pull accepts only a fast-forward: `PullFastForwardAsync` stashes the project's local changes, runs `git merge --ff-only` to the commit the preflight fetched, and pops the stash. A failed merge pops the stash back; a pop that cannot apply leaves Git's stash entry and conflicts in place and returns `Failed` with a message naming the stash.
6. **Plain Git**: these commands run as plain Git porcelain under Git's own locks and hooks. Beutl does not defend against a concurrent Git process or the user's Git setup; such a failure surfaces as Git's error.
7. **Uncancellable phase**: once the project is closed the Git commands run without accepting cancellation, so the project always reopens on a state Git itself finished.
8. **Conflict lockout**: when `WorkspaceStatus.HasConflicts`, every mutation is refused with conflict guidance while read members (`GetStatusAsync`, `GetHistoryAsync`, etc.) keep working (FR-033).
9. **`StatusChanged`**: publication is best-effort after durable mutations and debounced watcher refreshes. Each subscriber is isolated so one callback cannot fail the operation or suppress later subscribers; consumers marshal to the UI thread themselves.
10. **Cancellation**: cancellable operations kill the underlying git process; the repository is left in a state git itself considers consistent. Once the project is closed, pull, restore, and branch commands run without cancellation and the project reopens on the files Git left. Identity callbacks receive the exact operation token so cancellation also reaches an in-progress initialization prompt; the prompt cancels its pending result and closes its flyout on the UI thread.
11. **Errors**: git non-zero exits surface as `GitOperationException { ExitCode, Stderr }` with stderr preserved for the error dialog after credentials embedded in URLs are redacted; remote operations map expected outcomes, including an unrelated-dirty-repository refusal, to `RemoteOpResult` instead of throwing.

## Exposure

- `EditViewModel.GetService(typeof(IProjectVersionControlService))` returns the coordinator's visible read/query service for the open project; temporary close publishes `null` without surrendering backend ownership.
- The tool tab observes `IReadOnlyReactiveProperty<IProjectVersionControlService?>` for queries and resolves `IProjectVersionControlCoordinator` for mutations.
- Plugin callers cannot cast the public service to the internal backend or transaction interfaces.
