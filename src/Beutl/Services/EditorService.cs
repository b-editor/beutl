using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Serialization;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class EditorTabItem : IAsyncDisposable
{
    private string? _hash;

    public EditorTabItem(IEditorContext context)
    {
        Context = new ReactiveProperty<IEditorContext>(context);
        FilePath = Context.Select(ctxt => ctxt?.Object.Uri?.LocalPath)
            .ToReadOnlyReactivePropertySlim()!;
        FileName = FilePath.Select(Path.GetFileName)
            .Do(_ => _hash = null)
            .ToReadOnlyReactivePropertySlim()!;
        Extension = Context.Select(ctxt => ctxt?.Extension!)
            .ToReadOnlyReactivePropertySlim()!;
    }

    public IReactiveProperty<IEditorContext> Context { get; }

    public IReadOnlyReactiveProperty<string> FilePath { get; }

    public IReadOnlyReactiveProperty<string> FileName { get; }

    public IReadOnlyReactiveProperty<EditorExtension> Extension { get; }

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

    public async ValueTask DisposeAsync()
    {
        await Context.Value.DisposeAsync();
        Context.Value = null!;

        Context.Dispose();
        FilePath.Dispose();
        FileName.Dispose();
        Extension.Dispose();
        IsSelected.Dispose();
    }
}

public sealed class EditorService
    : IOutputOperationLeaseProvider,
        Beutl.Editor.Services.IEditorFileUsage,
        IProjectFileWriteAdmissionHost
{
    public bool IsFileInUse(string path, bool isDirectory)
    {
        Dispatcher.UIThread.VerifyAccess();
        IEnumerable<CoreObject> objects = _tabItems
            .Select(item => item.Context.Value?.Object)
            .OfType<CoreObject>();
        if (BeutlApplication.Current.Project is { } project)
            objects = objects.Concat(project.Items).Prepend(project);
        foreach (CoreObject obj in objects
                     .SelectMany(Beutl.ProjectSystem.SerializedGraphTraversal.Enumerate)
                     .OfType<CoreObject>()
                     .Distinct<CoreObject>(ReferenceEqualityComparer.Instance))
        {
            if (Matches(obj.Uri)) return true;
        }
        return false;

        bool Matches(Uri? uri) => uri is { IsFile: true } && (isDirectory
            ? FilePathComparison.IsSameOrDescendant(path, uri.LocalPath)
            : FilePathComparison.AreSameCanonicalPath(path, uri.LocalPath));
    }

    internal Beutl.Editor.Components.WebBrowserTab.IBrowserSettingsHost? BrowserSettingsHost { get; set; }
    internal Beutl.Editor.Components.FileBrowserTab.FileBrowserStorageProviderRegistry? StorageProviders { get; set; }
    private readonly CoreList<EditorTabItem> _tabItems;
    private readonly ILogger _logger = Log.CreateLogger<EditorService>();
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
    private readonly IProjectFileWriteAdmission _projectFileWriteAdmission;
    private readonly ReactivePropertySlim<ProjectLifecycleActivity> _lifecycleActivity = new();
    private readonly object _lifecycleActivitySync = new();
    private readonly List<LifecycleActivityLease> _lifecycleActivities = [];
    private TaskCompletionSource? _worktreeMutationCompletion;
    private int _activeOutputOperations;
    private int _activeProjectFileWrites;
    private int _activeEditorFileOpens;
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
        LifecycleActivity = _lifecycleActivity.ToReadOnlyReactivePropertySlim();
        _projectFileWriteAdmission = new ProjectFileWriteAdmission(this);
        HostProjectFileWriteAdmission.RegisterHost(this);
    }

    // Answers only for contexts in this service's own tab list, read at the moment of the write, so a
    // second EditorService in the process never gates a tab that belongs to this one, and a context
    // whose tab has closed is no longer admitted.
    IProjectFileWriteAdmission? IProjectFileWriteAdmissionHost.TryGetAdmission(IEditorContext context)
    {
        foreach (EditorTabItem item in _tabItems)
        {
            if (ReferenceEquals(item.Context.Value, context))
                return _projectFileWriteAdmission;
        }

        return null;
    }

    public ICoreList<EditorTabItem> TabItems => _tabItems;

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

    /// <summary>
    /// The slow project work shown in place of the editor area, or
    /// <see cref="ProjectLifecycleActivity.None"/> while the editor area is available.
    /// </summary>
    internal IReadOnlyReactiveProperty<ProjectLifecycleActivity> LifecycleActivity { get; }

    /// <summary>
    /// Shows <paramref name="activity"/> in place of the editor area until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// Activities can overlap, as when creating a project first closes the open one, so the most recent
    /// activity still running is shown. The handle can be disposed from any thread.
    /// </remarks>
    internal IDisposable BeginLifecycleActivity(ProjectLifecycleActivity activity)
    {
        if (activity == ProjectLifecycleActivity.None)
        {
            throw new ArgumentOutOfRangeException(nameof(activity));
        }

        var lease = new LifecycleActivityLease(this, activity);
        lock (_lifecycleActivitySync)
        {
            _lifecycleActivities.Add(lease);
        }

        PublishLifecycleActivity();
        return lease;
    }

    private void EndLifecycleActivity(LifecycleActivityLease lease)
    {
        lock (_lifecycleActivitySync)
        {
            _lifecycleActivities.Remove(lease);
        }

        PublishLifecycleActivity();
    }

    private void PublishLifecycleActivity()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _lifecycleActivity.Value = GetCurrentLifecycleActivity();
        }
        else
        {
            // The posted update reads the list when it runs, so updates arriving out of order still settle
            // on the current activity.
            Dispatcher.UIThread.Post(() => _lifecycleActivity.Value = GetCurrentLifecycleActivity());
        }
    }

    private ProjectLifecycleActivity GetCurrentLifecycleActivity()
    {
        lock (_lifecycleActivitySync)
        {
            return _lifecycleActivities.Count > 0
                ? _lifecycleActivities[^1].Activity
                : ProjectLifecycleActivity.None;
        }
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

    // Keeps a worktree mutation from starting while a file is opened in an editor, or created and
    // opened, and fails while one runs. Opens may overlap each other, outputs and project-file writes.
    internal IDisposable? TryBeginEditorFileOpen()
    {
        lock (_workspaceOperationSync)
        {
            if (_worktreeMutationActive)
            {
                return null;
            }

            _activeEditorFileOpens++;
            return new WorkspaceOperationLease(this, WorkspaceOperationKind.EditorFileOpen);
        }
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
                && _activeProjectFileWrites == 0
                && _activeEditorFileOpens == 0)
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
            if (item.Context.Value is ISavableEditorContext editor && !await editor.SaveAsync())
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

    public bool TryGetTabItem(CoreObject obj, [NotNullWhen(true)] out EditorTabItem? result)
    {
        result = TabItems.FirstOrDefault(i => i.Context.Value?.Object == obj);

        return result != null;
    }

    public void ActivateTabItem(CoreObject obj)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string path = obj.Uri!.LocalPath;
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
                TabItems.Add(tabItem2);
                SelectedTabItem.Value = tabItem2;
            }
        }
    }

    public async ValueTask CloseTabItem(CoreObject obj)
    {
        if (TryGetTabItem(obj, out EditorTabItem? item))
        {
            await CloseTabItem(item);
        }
    }

    public ValueTask CloseTabItem(EditorTabItem item) => CloseTabItem(item, saveChanges: true);

    internal async ValueTask CloseTabItem(EditorTabItem item, bool saveChanges)
    {
        if (saveChanges && !await SaveSceneEditorsBeforeCloseAsync([item]))
            return;

        if (TabItems.Remove(item))
            await item.DisposeAsync();
    }

    internal async Task<bool> SaveSceneEditorsBeforeCloseAsync(IEnumerable<EditorTabItem> items)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => SaveSceneEditorsBeforeCloseAsync(items));

        // Git transitions already saved or deliberately discarded these models. Writing them during
        // teardown would overwrite the checked-out files. Disposal itself remains a discard operation.
        if (IsWorktreeMutationActive)
            return true;

        EditViewModel[] editors = items.Select(item => item.Context.Value)
            .OfType<EditViewModel>()
            .Where(editor => !editor.IsDisposingOrDisposed && editor.Scene.Uri is not null)
            .ToArray();
        if (editors.Length == 0)
            return true;

        using IDisposable suspension = SuspendEditors();
        try
        {
            using IProjectFileWriteLease fileWrite = await BeginProjectFileWriteAsync(CancellationToken.None);
            foreach (EditViewModel editor in editors)
            {
                // A transition can have retired a captured editor while this writer waited.
                if (editor.IsDisposingOrDisposed)
                    continue;

                editor.HistoryManager.FlushPendingMutations();
                editor.HistoryManager.Commit();
                if (!await editor.SaveAsync())
                    throw new IOException(MessageStrings.FileSaveException);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save an editor before closing it.");
            NotificationService.ShowError(string.Empty, MessageStrings.FileSaveException);
            return false;
        }
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

    private sealed class LifecycleActivityLease(
        EditorService owner,
        ProjectLifecycleActivity activity) : IDisposable
    {
        private int _disposed;

        public ProjectLifecycleActivity Activity => activity;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndLifecycleActivity(this);
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
        EditorFileOpen,
    }
}
