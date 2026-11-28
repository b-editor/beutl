using Beutl.Editor.VersionControl;

namespace Beutl.Services;

// The reservation of the project's files: outputs, project-file writes and editor file opens may overlap one
// another, and a worktree mutation runs only while none of them does. Each workspace has its own, so a save, an
// export or a version-control operation in one project neither waits for nor blocks an unrelated one. Projects whose
// files can overlap (one folder inside the other's, or inside the other's Git worktree) share the reservation: a
// mutation of one waits for the other's work, and the other's work waits for the mutation.
internal sealed partial class EditorWorkspace
{
    // Shared by every workspace of the editor service, so a workspace can check the ones it overlaps atomically.
    private object _workspaceOperationSync => Owner.WorkspaceOperationSync;
    private readonly SemaphoreSlim _projectFileWriteGate = new(1, 1);
    private TaskCompletionSource? _worktreeMutationCompletion;
    private int _activeOutputOperations;
    private int _activeProjectFileWrites;
    private int _activeEditorFileOpens;
    private bool _worktreeMutationActive;

    // Whether this project's own files are being replaced. A mutation of an overlapping project still holds back the
    // reservations taken here.
    internal bool IsWorktreeMutationActive
    {
        get
        {
            lock (_workspaceOperationSync)
            {
                return _worktreeMutationActive;
            }
        }
    }

    // The folder of the project, canonical, or null for the editor service's own workspace.
    private readonly string? _projectDirectory;
    private string? _repositoryRoot;

    // The Git worktree the project's version control works in, canonical; set by the coordinator tracking it.
    internal void SetRepositoryRoot(string? repositoryRoot)
    {
        string? canonical = null;
        if (repositoryRoot is not null)
        {
            try
            {
                canonical = Path.TrimEndingDirectorySeparator(FilePathComparison.ResolveCanonicalPath(repositoryRoot));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            }
        }

        lock (_workspaceOperationSync)
        {
            _repositoryRoot = canonical;
        }
    }

    // Whether a mutation of one of the two workspaces can replace files the other uses. Called holding the lock.
    private bool Overlaps(EditorWorkspace other)
    {
        if (ReferenceEquals(other, this) || _projectDirectory is null || other._projectDirectory is null)
            return false;

        return Contains(_repositoryRoot ?? _projectDirectory, other._projectDirectory)
               || Contains(other._repositoryRoot ?? other._projectDirectory, _projectDirectory);

        static bool Contains(string scope, string directory)
            => FilePathComparison.IsSameOrDescendantCanonicalPath(scope, directory);
    }

    // The worktree mutation that holds back work here: this workspace's own, or one of an overlapping workspace.
    // Called holding the lock.
    private EditorWorkspace? FindBlockingMutation(EditorWorkspace[] workspaces)
    {
        if (_worktreeMutationActive)
            return this;

        foreach (EditorWorkspace other in workspaces)
        {
            if (other._worktreeMutationActive && Overlaps(other))
                return other;
        }

        return null;
    }

    internal IDisposable? TryBeginOutputOperation()
    {
        EditorWorkspace[] workspaces = Owner.Workspaces;
        lock (_workspaceOperationSync)
        {
            if (FindBlockingMutation(workspaces) is not null)
            {
                return null;
            }

            _activeOutputOperations++;
            return new WorkspaceOperationLease(this, WorkspaceOperationKind.Output);
        }
    }

    IDisposable? IOutputOperationLeaseProvider.TryBeginOutputOperation()
    {
        return TryBeginOutputOperation();
    }

    // Keeps a worktree mutation from starting while a file is opened in an editor, or created and
    // opened, and fails while one runs. Opens may overlap each other, outputs and project-file writes.
    internal IDisposable? TryBeginEditorFileOpen()
    {
        EditorWorkspace[] workspaces = Owner.Workspaces;
        lock (_workspaceOperationSync)
        {
            if (FindBlockingMutation(workspaces) is not null)
            {
                return null;
            }

            _activeEditorFileOpens++;
            return new WorkspaceOperationLease(this, WorkspaceOperationKind.EditorFileOpen);
        }
    }

    internal IDisposable BeginObservedOutputOperation()
        => TryBeginOutputOperation()
           ?? throw new InvalidOperationException(
               "Output cannot start while the project workspace is being replaced.");

    internal async ValueTask<IProjectFileWriteLease> BeginProjectFileWriteAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task waitForWorktreeMutation;
            EditorWorkspace[] workspaces = Owner.Workspaces;
            lock (_workspaceOperationSync)
            {
                waitForWorktreeMutation = FindBlockingMutation(workspaces) is { } blocking
                    ? blocking._worktreeMutationCompletion?.Task ?? Task.CompletedTask
                    : Task.CompletedTask;
            }

            // The gate is taken only once the workspace already looks free. Waiting for a worktree
            // mutation while holding it would park auto-save and editor teardown behind this writer.
            await waitForWorktreeMutation.WaitAsync(cancellationToken);
            await _projectFileWriteGate.WaitAsync(cancellationToken);
            if (TryAdmitProjectFileWrite() is { } lease)
            {
                return lease;
            }
        }
    }

    internal IProjectFileWriteLease? TryBeginProjectFileWrite()
    {
        if (!_projectFileWriteGate.Wait(0))
        {
            return null;
        }

        return TryAdmitProjectFileWrite();
    }

    // Clipboard edits can wait behind auto-save, but must not enter a replacement worktree.
    internal async ValueTask<IProjectFileWriteLease?> TryBeginProjectFileWriteAsync(CancellationToken cancellationToken)
    {
        EditorWorkspace[] workspaces = Owner.Workspaces;
        lock (_workspaceOperationSync)
        {
            if (FindBlockingMutation(workspaces) is not null) return null;
        }

        await _projectFileWriteGate.WaitAsync(cancellationToken);
        return TryAdmitProjectFileWrite(cancellationToken);
    }

    // Called holding the project-file write gate: admits the write unless a worktree mutation started
    // (or the request was canceled) meanwhile, and otherwise releases the gate.
    private WorkspaceOperationLease? TryAdmitProjectFileWrite(CancellationToken cancellationToken = default)
    {
        EditorWorkspace[] workspaces = Owner.Workspaces;
        lock (_workspaceOperationSync)
        {
            if (FindBlockingMutation(workspaces) is null && !cancellationToken.IsCancellationRequested)
            {
                _activeProjectFileWrites++;
                return new WorkspaceOperationLease(
                    this,
                    WorkspaceOperationKind.ProjectFileWrite);
            }
        }

        _projectFileWriteGate.Release();
        return null;
    }

    /// <summary>
    /// Reserves the workspace for a worktree mutation, optionally taking over a finished
    /// project-file write so the workspace is never left unreserved between the two.
    /// </summary>
    /// <param name="completedWrite">
    /// A project-file write to fold into this mutation. It is released whether or not the mutation
    /// starts, so the caller must have finished writing. The caller still owns it and must dispose
    /// it, which is a no-op once it has been taken over.
    /// </param>
    internal IDisposable? TryBeginWorktreeMutation(IProjectFileWriteLease? completedWrite = null)
    {
        WorkspaceOperationLease? handoff = null;
        if (completedWrite is not null)
        {
            if (completedWrite is not WorkspaceOperationLease
                {
                    Kind: WorkspaceOperationKind.ProjectFileWrite
                } lease
                || !ReferenceEquals(lease.Owner, this))
            {
                throw new ArgumentException(
                    "The lease was not issued by this workspace for a project-file write.",
                    nameof(completedWrite));
            }

            handoff = lease;
        }

        IDisposable? mutation = null;
        EditorWorkspace[] workspaces = Owner.Workspaces;
        lock (_workspaceOperationSync)
        {
            if (handoff is not null && handoff.TryTakeOver())
            {
                _activeProjectFileWrites--;
                // Released under the lock so the count and the gate never disagree about whether the
                // workspace is reserved.
                _projectFileWriteGate.Release();
            }

            if (FindBlockingMutation(workspaces) is null
                && !HasReservations
                && !workspaces.Any(other => Overlaps(other) && other.HasReservations))
            {
                _worktreeMutationActive = true;
                _worktreeMutationCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                mutation = new WorkspaceOperationLease(
                    this,
                    WorkspaceOperationKind.WorktreeMutation);
            }
        }

        return mutation;
    }

    // The workspace whose files a project-file write reserves, or null for a lease of another kind.
    internal static EditorWorkspace? OwnerOf(IProjectFileWriteLease lease)
        => lease is WorkspaceOperationLease { Kind: WorkspaceOperationKind.ProjectFileWrite } workspaceLease
            ? workspaceLease.Owner
            : null;

    // Outputs, project-file writes or editor file opens are running. Called holding the lock.
    private bool HasReservations
        => _activeOutputOperations != 0 || _activeProjectFileWrites != 0 || _activeEditorFileOpens != 0;

    private void EndWorkspaceOperation(WorkspaceOperationKind kind)
    {
        TaskCompletionSource? completedWorktreeMutation = null;
        bool releaseProjectFileWrite = false;
        lock (_workspaceOperationSync)
        {
            switch (kind)
            {
                case WorkspaceOperationKind.Output when _activeOutputOperations > 0:
                    _activeOutputOperations--;
                    break;
                case WorkspaceOperationKind.ProjectFileWrite when _activeProjectFileWrites > 0:
                    _activeProjectFileWrites--;
                    releaseProjectFileWrite = true;
                    break;
                case WorkspaceOperationKind.EditorFileOpen when _activeEditorFileOpens > 0:
                    _activeEditorFileOpens--;
                    break;
                case WorkspaceOperationKind.WorktreeMutation:
                    _worktreeMutationActive = false;
                    completedWorktreeMutation = _worktreeMutationCompletion;
                    _worktreeMutationCompletion = null;
                    break;
            }
        }

        completedWorktreeMutation?.TrySetResult();
        if (releaseProjectFileWrite)
        {
            _projectFileWriteGate.Release();
        }
    }

    private sealed class WorkspaceOperationLease(
        EditorWorkspace owner,
        WorkspaceOperationKind kind) : IProjectFileWriteLease
    {
        private int _disposed;

        public EditorWorkspace Owner => owner;

        public WorkspaceOperationKind Kind => kind;

        // Retires the lease without ending the operation it reserves, so the caller can transfer
        // that reservation to another lease instead of releasing and racing to reacquire it.
        public bool TryTakeOver()
        {
            return Interlocked.Exchange(ref _disposed, 1) == 0;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndWorkspaceOperation(kind);
            }
        }
    }

    private enum WorkspaceOperationKind
    {
        Output,
        ProjectFileWrite,
        WorktreeMutation,
        EditorFileOpen,
    }
}
