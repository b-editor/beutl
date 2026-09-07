using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Serialization;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class EditorTabItem : IAsyncDisposable
{
    private readonly object _lifetimeGate = new();
    private static readonly AsyncLocal<IReadOnlySet<EditorTabItem>?> s_ownedDisposals = new();
    private Task? _disposeTask;
    private Task? _transitionTask;
    private bool _closing;
    private string? _hash;

    public EditorTabItem(IEditorContext context)
    {
        MutableContext = new ReactiveProperty<IEditorContext>(context);
        FilePath = Context.Select(ctxt => ctxt?.Object.Uri?.LocalPath)
            .ToReadOnlyReactivePropertySlim()!;
        FileName = FilePath.Select(Path.GetFileName)
            .Do(_ => _hash = null)
            .ToReadOnlyReactivePropertySlim()!;
        Extension = Context.Select(ctxt => ctxt?.Extension!)
            .ToReadOnlyReactivePropertySlim()!;
        Commands = Context.Select(ctxt => ctxt?.Commands)
            .ToReadOnlyReactivePropertySlim();
    }

    private IReactiveProperty<IEditorContext> MutableContext { get; }

    public IReadOnlyReactiveProperty<IEditorContext> Context => MutableContext;

    public IReadOnlyReactiveProperty<string> FilePath { get; }

    public IReadOnlyReactiveProperty<string> FileName { get; }

    public IReadOnlyReactiveProperty<EditorExtension> Extension { get; }

    public IReadOnlyReactiveProperty<IKnownEditorCommands?> Commands { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string GetFileNameHash()
    {
        if (_hash == null)
        {
            string name = FileName.Value;
            ReadOnlySpan<char> span = name.AsSpan();

            // UTF-8を得たいわけではないので
            byte[] hash = MD5.HashData(MemoryMarshal.Cast<char, byte>(span));

            _hash = Convert.ToHexString(hash);
        }

        return _hash;
    }

    /// <summary>
    /// Replaces the owned editor context after the previous context has fully torn down.
    /// The supplied context is consumed even when replacement is rejected.
    /// </summary>
    public ValueTask<bool> ReplaceContextAsync(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IEditorContext? oldContext = null;
        TaskCompletionSource<bool>? completion = null;
        bool rejected;
        lock (_lifetimeGate)
        {
            rejected = _closing || _transitionTask is not null;
            if (!rejected)
            {
                oldContext = MutableContext.Value;
                MutableContext.Value = null!;
                completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _transitionTask = completion.Task;
            }
        }

        if (rejected)
            return new ValueTask<bool>(DisposeRejectedAndReturnFalseAsync(context));

        _ = CompleteReplacementAsync(oldContext!, context, completion!);
        return new ValueTask<bool>(completion!.Task);
    }

    private async Task CompleteReplacementAsync(
        IEditorContext oldContext,
        IEditorContext replacement,
        TaskCompletionSource<bool> completion)
    {
        bool published = false;
        try
        {
            await DisposeOwnedContextAsync(oldContext).ConfigureAwait(true);
            lock (_lifetimeGate)
            {
                if (!_closing)
                {
                    MutableContext.Value = replacement;
                    published = true;
                }
                _transitionTask = null;
            }
            if (!published)
                await DisposeRejectedContextAsync(replacement).ConfigureAwait(true);
            completion.TrySetResult(published);
        }
        catch (Exception ex)
        {
            lock (_lifetimeGate)
                _transitionTask = null;
            await DisposeRejectedContextAsync(replacement).ConfigureAwait(true);
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeOwnedContextAsync(IEditorContext context)
    {
        IReadOnlySet<EditorTabItem>? previous = s_ownedDisposals.Value;
        var current = previous is null
            ? new HashSet<EditorTabItem>(ReferenceEqualityComparer.Instance)
            : new HashSet<EditorTabItem>(previous, ReferenceEqualityComparer.Instance);
        current.Add(this);
        s_ownedDisposals.Value = current;
        try { await context.DisposeAsync().ConfigureAwait(true); }
        finally { s_ownedDisposals.Value = previous; }
    }

    private static async Task DisposeRejectedContextAsync(IEditorContext context)
    {
        try
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The caller cannot retain ownership of a rejected context. Preserve the
            // original replacement/close outcome while ensuring it is not leaked.
        }
    }

    private static async Task<bool> DisposeRejectedAndReturnFalseAsync(IEditorContext context)
    {
        await DisposeRejectedContextAsync(context).ConfigureAwait(false);
        return false;
    }

    public ValueTask DisposeAsync()
    {
        Task? transition;
        TaskCompletionSource<object?>? completion = null;
        bool reentrant = s_ownedDisposals.Value?.Contains(this) == true;
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null)
            {
                return reentrant ? ValueTask.CompletedTask : new ValueTask(_disposeTask);
            }

            _closing = true;
            transition = _transitionTask;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(transition, completion);
        return reentrant ? ValueTask.CompletedTask : new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(
        Task? transition,
        TaskCompletionSource<object?> completion)
    {
        try
        {
            if (transition is not null)
            {
                try { await transition.ConfigureAwait(true); }
                catch { }
            }

            IEditorContext? context;
            lock (_lifetimeGate)
            {
                context = MutableContext.Value;
                MutableContext.Value = null!;
            }
            if (context is not null)
                await DisposeOwnedContextAsync(context).ConfigureAwait(true);

            MutableContext.Dispose();
            FilePath.Dispose();
            FileName.Dispose();
            Extension.Dispose();
            Commands.Dispose();
            IsSelected.Dispose();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return;
        }

        completion.TrySetResult(null);
    }
}

public sealed class EditorService : IOutputOperationLeaseProvider
{
    private readonly CoreList<EditorTabItem> _tabItems;
    private readonly ExtensionProvider _extensionProvider;
    private readonly Action<Project, Uri> _serializeProject;
    private readonly ReactivePropertySlim<IProjectVersionControlService?>
        _projectVersionControlService = new();
    private readonly object _workspaceOperationSync = new();
    private readonly object _editorSuspensionSync = new();
    private readonly Dictionary<IEditorContext, (int Count, bool WasEnabled, bool WasTrackedByTab)>
        _editorSuspensions
        = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _projectFileWriteGate = new(1, 1);
    private TaskCompletionSource? _worktreeMutationCompletion;
    private int _activeOutputOperations;
    private int _activeProjectFileWrites;
    private bool _worktreeMutationActive;

    public EditorService(ExtensionProvider extensionProvider)
        : this(
            extensionProvider,
            static (project, uri) => CoreSerializer.StoreToUri(project, uri))
    {
    }

    internal EditorService(
        ExtensionProvider extensionProvider,
        Action<Project, Uri> serializeProject)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(serializeProject);

        _extensionProvider = extensionProvider;
        _serializeProject = serializeProject;
        _tabItems = new() { ResetBehavior = ResetBehavior.Remove };
        ProjectVersionControlService = _projectVersionControlService
            .ToReadOnlyReactivePropertySlim();
    }

    public ICoreReadOnlyList<EditorTabItem> TabItems => _tabItems;

    internal void ClearTabItems() => _tabItems.Clear();

    internal void AddTabItem(EditorTabItem item) => _tabItems.Add(item);

    internal bool RemoveTabItem(EditorTabItem item) => _tabItems.Remove(item);

    internal ExtensionProvider ExtensionProvider => _extensionProvider;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem { get; } = new ReactivePropertySlim<EditorTabItem?>();

    internal IReadOnlyReactiveProperty<IProjectVersionControlService?>
        ProjectVersionControlService
    { get; }

    internal IProjectVersionControlCoordinator? ProjectVersionControlCoordinator { get; set; }

    internal IProjectVersionControlSession? ProjectVersionControlSession
        => ProjectVersionControlCoordinator as IProjectVersionControlSession;

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

    internal void PublishProjectVersionControlService(
        IProjectVersionControlService? service)
    {
        _projectVersionControlService.Value = service;
    }

    internal IDisposable? TryBeginOutputOperation()
    {
        lock (_workspaceOperationSync)
        {
            if (_worktreeMutationActive)
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

    internal IDisposable BeginObservedOutputOperation(IEditorContext? context = null)
    {
        IDisposable operation = TryBeginOutputOperation()
                                ?? throw new InvalidOperationException(
                                    "Output cannot start while the project workspace is being replaced.");

        if (context is null)
        {
            return operation;
        }

        try
        {
            return new OutputOperationLease(operation, SuspendEditor(context));
        }
        catch
        {
            operation.Dispose();
            throw;
        }
    }

    internal IDisposable SuspendEditor(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (_editorSuspensionSync)
        {
            bool isTrackedByTab = TabItems.Any(
                item => ReferenceEquals(item.Context.Value, context));
            if (_editorSuspensions.TryGetValue(context, out var state))
            {
                _editorSuspensions[context] = (
                    state.Count + 1,
                    state.WasEnabled,
                    state.WasTrackedByTab || isTrackedByTab);
            }
            else
            {
                bool wasEnabled = context.IsEnabled.Value;
                _editorSuspensions.Add(context, (1, wasEnabled, isTrackedByTab));
                try
                {
                    context.IsEnabled.Value = false;
                }
                catch (Exception disableFailure)
                {
                    _editorSuspensions.Remove(context);
                    try
                    {
                        context.IsEnabled.Value = wasEnabled;
                    }
                    catch (Exception restoreFailure)
                    {
                        throw new AggregateException(disableFailure, restoreFailure);
                    }

                    throw;
                }
            }
        }

        return new EditorContextSuspensionLease(this, context);
    }

    private void EndEditorSuspension(IEditorContext context)
    {
        lock (_editorSuspensionSync)
        {
            if (!_editorSuspensions.TryGetValue(context, out var state))
            {
                return;
            }

            if (state.Count > 1)
            {
                _editorSuspensions[context] = (
                    state.Count - 1,
                    state.WasEnabled,
                    state.WasTrackedByTab);
            }
            else
            {
                _editorSuspensions.Remove(context);
                if (!state.WasTrackedByTab
                    || TabItems.Any(item => ReferenceEquals(item.Context.Value, context)))
                {
                    context.IsEnabled.Value = state.WasEnabled;
                }
            }
        }
    }

    internal async ValueTask<IProjectFileWriteLease> BeginProjectFileWriteAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task waitForWorktreeMutation;
            lock (_workspaceOperationSync)
            {
                waitForWorktreeMutation = _worktreeMutationActive
                    ? _worktreeMutationCompletion?.Task ?? Task.CompletedTask
                    : Task.CompletedTask;
            }

            // The gate is taken only once the workspace already looks free. Waiting for a worktree
            // mutation while holding it would park auto-save and editor teardown behind this writer.
            await waitForWorktreeMutation.WaitAsync(cancellationToken);
            await _projectFileWriteGate.WaitAsync(cancellationToken);
            lock (_workspaceOperationSync)
            {
                if (!_worktreeMutationActive)
                {
                    _activeProjectFileWrites++;
                    return new WorkspaceOperationLease(
                        this,
                        WorkspaceOperationKind.ProjectFileWrite);
                }
            }

            _projectFileWriteGate.Release();
        }
    }

    internal IProjectFileWriteLease? TryBeginProjectFileWrite()
    {
        if (!_projectFileWriteGate.Wait(0))
        {
            return null;
        }

        lock (_workspaceOperationSync)
        {
            if (!_worktreeMutationActive)
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
                    "The lease was not issued by this service for a project-file write.",
                    nameof(completedWrite));
            }

            handoff = lease;
        }

        IDisposable? mutation = null;
        lock (_workspaceOperationSync)
        {
            if (handoff is not null && handoff.TryTakeOver())
            {
                _activeProjectFileWrites--;
                // Released under the lock so the count and the gate never disagree about whether the
                // workspace is reserved.
                _projectFileWriteGate.Release();
            }

            if (!_worktreeMutationActive
                && _activeOutputOperations == 0
                && _activeProjectFileWrites == 0)
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

    internal async Task<bool> SaveProjectFilesAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => SaveProjectFilesCoreAsync(project, cancellationToken));
        }

        return await SaveProjectFilesCoreAsync(project, cancellationToken);
    }

    private async Task<bool> SaveProjectFilesCoreAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        Uri projectUri = project.Uri
                         ?? throw new InvalidOperationException(
                             "The project must have a file URI before it can be saved.");
        EditorTabItem[] tabItems = TabItems.ToArray();
        using IDisposable suspension = SuspendEditors();
        await Task.Run(
            () => _serializeProject(project, projectUri),
            cancellationToken);

        foreach (EditorTabItem item in tabItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Commands.Value is { } commands && !await commands.OnSave())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Disables every open editor until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// A version-control transition holds this from before its pre-transition save until the
    /// project is closed. Releasing it earlier would let the user edit while the cycle awaits Git,
    /// and those edits would land after the safety snapshot and be discarded by the close.
    /// Suspensions share a per-context reference count with output execution, so they can complete
    /// in any order without re-enabling an editor still owned by another operation.
    /// </remarks>
    internal IDisposable SuspendEditors()
    {
        EditorTabItem[] tabItems = TabItems.ToArray();
        var suspensions = new List<IDisposable>(tabItems.Length);
        try
        {
            foreach (EditorTabItem item in tabItems)
            {
                if (item.Context.Value is { } context)
                {
                    suspensions.Add(SuspendEditor(context));
                }
            }

            return new EditorSuspension(suspensions.ToArray());
        }
        catch (Exception acquisitionFailure)
        {
            var failures = new List<Exception> { acquisitionFailure };
            foreach (IDisposable suspension in suspensions)
            {
                try
                {
                    suspension.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    failures.Add(cleanupFailure);
                }
            }

            throw new AggregateException(failures);
        }
    }

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

    public bool TryGetTabItem(CoreObject obj, [NotNullWhen(true)] out EditorTabItem? result)
    {
        result = TabItems.FirstOrDefault(i => i.Context.Value?.Object == obj);

        return result != null;
    }

    public void ActivateTabItem(CoreObject obj)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string path = Uri.UnescapeDataString(obj.Uri!.LocalPath);
        viewConfig.UpdateRecentFile(path);

        if (TryGetTabItem(obj, out EditorTabItem? tabItem))
        {
            tabItem.IsSelected.Value = true;
            SelectedTabItem.Value = tabItem;
        }
        else
        {
            EditorExtension? ext = _extensionProvider.MatchEditorExtension(path);

            if (ext?.TryCreateContext(obj, new EditorContextServices(this, _extensionProvider), out IEditorContext? context) == true)
            {
                var tabItem2 = new EditorTabItem(context) { IsSelected = { Value = true } };
                AddTabItem(tabItem2);
                SelectedTabItem.Value = tabItem2;
            }
        }
    }

    public async ValueTask CloseTabItem(CoreObject obj)
    {
        if (TryGetTabItem(obj, out EditorTabItem? item))
        {
            RemoveTabItem(item);
            await item.DisposeAsync();
        }
    }

    public async ValueTask CloseTabItem(EditorTabItem item)
    {
        RemoveTabItem(item);
        await item.DisposeAsync();
    }

    private sealed class EditorSuspension(IDisposable[] suspensions) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            List<Exception>? failures = null;
            foreach (IDisposable suspension in suspensions)
            {
                try
                {
                    suspension.Dispose();
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is not null)
            {
                throw new AggregateException(failures);
            }
        }
    }

    private sealed class WorkspaceOperationLease(
        EditorService owner,
        WorkspaceOperationKind kind) : IProjectFileWriteLease
    {
        private int _disposed;

        public EditorService Owner => owner;

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

    private sealed class EditorContextSuspensionLease(
        EditorService owner,
        IEditorContext context) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndEditorSuspension(context);
            }
        }
    }

    private sealed class OutputOperationLease(
        IDisposable operation,
        IDisposable suspension) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                suspension.Dispose();
            }
            finally
            {
                operation.Dispose();
            }
        }
    }

    private enum WorkspaceOperationKind
    {
        Output,
        ProjectFileWrite,
        WorktreeMutation,
    }
}
