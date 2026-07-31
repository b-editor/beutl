# Contract: IProjectVersionControlService

**Scope**: the seam consumed by the tool tab, the coordinator, and (via `IEditorContext.GetService`) plugin authors. Lives in `Beutl.Editor.VersionControl` (Avalonia-free). One instance per open project, owned by `VersionControlCoordinator`.

```csharp
public interface IProjectVersionControlService : IDisposable
{
    RepositoryInfo? Repository { get; }                       // null => not under VC / git unavailable

    Task<GitAvailability> GetAvailabilityAsync(CancellationToken ct);
    Task<RepositoryInfo?> DiscoverRepositoryAsync(string projectRoot, CancellationToken ct);
    Task InitializeAsync(InitOptions options, CancellationToken ct);

    Task<WorkspaceStatus> GetStatusAsync(CancellationToken ct);
    Task<CommitResult> CommitAllAsync(string message, SnapshotKind kind, CancellationToken ct);

    Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(string sha, CancellationToken ct);
    Task<string> GetDiffAsync(string sha, string? path, CancellationToken ct);   // unified diff text

    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct);
    Task CreateBranchAsync(string name, string startPoint, CancellationToken ct);
    Task SwitchBranchAsync(string name, CancellationToken ct);                   // low-level; cycle orchestrated above
    Task RestoreWorktreeFromAsync(string sha, CancellationToken ct);             // restore + clean, pathspec-scoped

    Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(CancellationToken ct);
    Task SetRemoteAsync(string url, CancellationToken ct);
    Task<RemoteOpResult> PushAsync(IProgress<string>? progress, CancellationToken ct);
    Task<RemoteOpResult> PullFastForwardAsync(CancellationToken ct);

    Task<GitIdentity?> GetIdentityAsync(CancellationToken ct);
    Task SetLocalIdentityAsync(GitIdentity identity, CancellationToken ct);      // repo-local only

    event EventHandler<WorkspaceStatus>? StatusChanged;                          // BACKGROUND thread
}
```

## Behavioral guarantees

1. **Serialization**: all members serialize on one internal `SemaphoreSlim(1,1)`; concurrent calls queue. No member ever runs on the caller's UI thread.
2. **Pathspec scoping**: every git invocation that touches paths (`add`, `status`, `log`, `show`, `restore`, `clean`) appends `-- {Repository.Pathspec}`. In the nested-repo case no file outside the project directory is ever staged, restored, or cleaned. `Branch*`/`Push`/`Pull` act on the whole repository (disclosed by the UI).
3. **`CommitAllAsync`**: checks status first; returns `NoChanges` without creating a commit when clean (FR-014). Auto kinds with unset identity return `SkippedNoIdentity`; `Manual` with unset identity triggers the identity flow instead. Stages with `git add -A -- <pathspec>`; writes the `Beutl-Snapshot` trailer for non-`Manual` kinds.
4. **`RestoreWorktreeFromAsync`**: `git restore --source=<sha> --worktree -- <pathspec>` then `git clean -fd -- <pathspec>`. Precondition (enforced by the coordinator, asserted by the service): the project is closed. Ignored files (`.beutl/`, `*.tmp`) survive the clean. The coordinator records a safety snapshot only when the project pathspec is dirty, so restoring again from an already clean state creates no empty safety commit.
5. **`SwitchBranchAsync` / `PullFastForwardAsync`**: same closed-project precondition. Pull is `--ff-only`; divergence returns `Diverged` and changes nothing.
6. **Conflict lockout**: when `WorkspaceStatus.HasConflicts`, every mutating member throws `VersionControlConflictedException` carrying the guidance text; read members (`GetStatusAsync`, `GetHistoryAsync`, …) keep working (FR-033).
7. **`StatusChanged`**: raised on a background thread after every mutating call and after each debounced watcher-triggered refresh; consumers marshal to the UI thread themselves (matches the `AutoSaveService` eventing rule).
8. **Cancellation**: `ct` kills the underlying git process; the repository is left in a state git itself considers consistent (no partial index writes thanks to git's own locking; a killed network op is retryable).
9. **Errors**: git non-zero exits surface as `GitOperationException { ExitCode, Stderr }` with stderr preserved for the error dialog after credentials embedded in URLs are redacted; remote operations map to `RemoteOpResult` instead of throwing for the four expected outcomes.

## Exposure

- `EditViewModel.GetService(typeof(IProjectVersionControlService))` returns the coordinator's instance for the open project (all scene tabs share it).
- The tool tab resolves it via `IEditorContext.GetRequiredService<IProjectVersionControlService>()` — the uniform plugin-facing pattern.
