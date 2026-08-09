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

    internal async ValueTask<IDisposable> BeginProjectFileWriteAsync(
        CancellationToken cancellationToken)
    {
        await _projectFileWriteGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task waitForWorktreeMutation;
                lock (_workspaceOperationSync)
                {
                    if (!_worktreeMutationActive)
                    {
                        _activeProjectFileWrites++;
                        return new WorkspaceOperationLease(
                            this,
                            WorkspaceOperationKind.ProjectFileWrite);
                    }

                    waitForWorktreeMutation = _worktreeMutationCompletion?.Task
                                              ?? Task.CompletedTask;
                }

                await waitForWorktreeMutation.WaitAsync(cancellationToken);
            }
        }
        catch
        {
            _projectFileWriteGate.Release();
            throw;
        }
    }

    internal IDisposable? TryBeginWorktreeMutation()
    {
        lock (_workspaceOperationSync)
        {
            if (_worktreeMutationActive
                || _activeOutputOperations > 0
                || _activeProjectFileWrites > 0)
            {
                return null;
            }

            _worktreeMutationActive = true;
            _worktreeMutationCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return new WorkspaceOperationLease(this, WorkspaceOperationKind.WorktreeMutation);
        }
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
        bool[] enabledStates = new bool[tabItems.Length];
        for (int i = 0; i < tabItems.Length; i++)
        {
            enabledStates[i] = tabItems[i].Context.Value.IsEnabled.Value;
            tabItems[i].Context.Value.IsEnabled.Value = false;
        }

        try
        {
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
        finally
        {
            for (int i = 0; i < tabItems.Length; i++)
            {
                if (tabItems[i].Context.Value is { } context)
                {
                    context.IsEnabled.Value = enabledStates[i];
                }
            }
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

    private sealed class WorkspaceOperationLease(
        EditorService owner,
        WorkspaceOperationKind kind) : IDisposable
    {
        private int _disposed;

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
