using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Serialization;
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
        Commands = Context.Select(ctxt => ctxt?.Commands)
            .ToReadOnlyReactivePropertySlim();
    }

    public IReactiveProperty<IEditorContext> Context { get; }

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

    public async ValueTask DisposeAsync()
    {
        await Context.Value.DisposeAsync();
        Context.Value = null!;

        Context.Dispose();
        FilePath.Dispose();
        FileName.Dispose();
        Extension.Dispose();
        Commands.Dispose();
        IsSelected.Dispose();
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

    public ICoreList<EditorTabItem> TabItems => _tabItems;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem { get; } = new ReactivePropertySlim<EditorTabItem?>();

    internal IReadOnlyReactiveProperty<IProjectVersionControlService?>
        ProjectVersionControlService
    { get; }

    internal IProjectVersionControlCoordinator? ProjectVersionControlCoordinator { get; set; }

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
    /// Suspensions nest: an inner one records the already-disabled state and restores it.
    /// </remarks>
    internal IDisposable SuspendEditors()
    {
        return new EditorSuspension(TabItems.ToArray());
    }

    internal async Task SwitchEditorExtensionAsync(EditorExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        // Callers offer the extension only for the selected tab's file type, so the swap must stay
        // bound to the tab that was selected when it was offered.
        EditorTabItem? targetTab = SelectedTabItem.Value;
        if (targetTab is null)
        {
            return;
        }

        // The lease must end before the swap: the outgoing context's teardown takes its own.
        using (await BeginProjectFileWriteAsync(CancellationToken.None))
        {
            if (targetTab.Commands.Value is { } commands)
            {
                await commands.OnSave();
            }
        }

        // Waiting for the lease can span a version-control transition that disposes every tab.
        if (!ReferenceEquals(SelectedTabItem.Value, targetTab)
            || targetTab.Context.Value is not { } currentContext)
        {
            return;
        }

        if (!extension.TryCreateContext(
                currentContext.Object,
                new EditorContextServices(this, _extensionProvider),
                out IEditorContext? context))
        {
            NotificationService.ShowInformation(
                title: MessageStrings.ContextNotCreated,
                message: string.Format(
                    format: MessageStrings.FailedToOpenFileWithExtension,
                    arg0: extension.DisplayName,
                    arg1: targetTab.FileName.Value));
            return;
        }

        // Installed before the outgoing context is torn down, so a failed teardown cannot leave the
        // tab bound to a half-disposed editor.
        targetTab.Context.Value = context;
        // DisposeAsync, not Dispose: IEditorContext.Dispose has an empty default implementation and
        // EditViewModel overrides only DisposeAsync, so Dispose would leak the outgoing editor.
        await currentContext.DisposeAsync();
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
                TabItems.Add(tabItem2);
                SelectedTabItem.Value = tabItem2;
            }
        }
    }

    public async ValueTask CloseTabItem(CoreObject obj)
    {
        if (TryGetTabItem(obj, out EditorTabItem? item))
        {
            TabItems.Remove(item);
            await item.DisposeAsync();
        }
    }

    public async ValueTask CloseTabItem(EditorTabItem item)
    {
        TabItems.Remove(item);
        await item.DisposeAsync();
    }

    private sealed class EditorSuspension : IDisposable
    {
        private readonly EditorTabItem[] _tabItems;
        private readonly bool[] _enabledStates;
        private int _disposed;

        public EditorSuspension(EditorTabItem[] tabItems)
        {
            _tabItems = tabItems;
            _enabledStates = new bool[tabItems.Length];
            for (int i = 0; i < tabItems.Length; i++)
            {
                if (tabItems[i].Context.Value is not { } context)
                {
                    continue;
                }

                _enabledStates[i] = context.IsEnabled.Value;
                context.IsEnabled.Value = false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            for (int i = 0; i < _tabItems.Length; i++)
            {
                // A transition closes the project before releasing its suspension, which nulls the
                // context of every tab it captured; those tabs no longer exist to re-enable.
                if (_tabItems[i].Context.Value is { } context)
                {
                    context.IsEnabled.Value = _enabledStates[i];
                }
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

    private enum WorkspaceOperationKind
    {
        Output,
        ProjectFileWrite,
        WorktreeMutation,
    }
}
