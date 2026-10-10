using System.Diagnostics.CodeAnalysis;
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

public sealed partial class EditorService
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
    private readonly IProjectFileWriteAdmission _projectFileWriteAdmission;
    private int _projectItemActivationSuppressionCount;

    internal bool ActivateAddedProjectItems => Volatile.Read(ref _projectItemActivationSuppressionCount) == 0;

    internal IDisposable SuppressProjectItemActivation()
    {
        Dispatcher.UIThread.VerifyAccess();
        _projectItemActivationSuppressionCount++;
        return Disposable.Create(() =>
        {
            Dispatcher.UIThread.VerifyAccess();
            _projectItemActivationSuppressionCount--;
        });
    }

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

    internal void PublishProjectVersionControlService(
        IProjectVersionControlService? service)
    {
        _projectVersionControlService.Value = service;
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
            if (item.Context.Value is EditViewModel sceneEditor)
                await sceneEditor.FlushMediaFingerprintsAsync();
            if (item.Context.Value is ISavableEditorContext editor && !await editor.SaveAsync())
            {
                return false;
            }
        }

        return true;
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
            if (CreateTabItem(obj, isSelected: true) is { } tabItem2)
            {
                SelectedTabItem.Value = tabItem2;
            }
        }
    }

    // Background editing shares the normal context, history, save admission and tab lifecycle,
    // without changing either the selected tab or the recent-file navigation state.
    internal EditorTabItem? GetOrCreateBackgroundTabItem(CoreObject obj)
    {
        Dispatcher.UIThread.VerifyAccess();
        using IDisposable? open = TryBeginEditorFileOpen();
        if (open is null)
            return null;

        return TryGetTabItem(obj, out EditorTabItem? existing)
            ? existing
            : CreateTabItem(obj, isSelected: false);
    }

    private EditorTabItem? CreateTabItem(CoreObject obj, bool isSelected)
    {
        EditorExtension? ext = _extensionProvider.MatchEditorExtension(obj.Uri!.LocalPath);
        if (ext?.TryCreateContext(obj, new EditorContextServices(this, _extensionProvider), out IEditorContext? context) != true)
            return null;

        var item = new EditorTabItem(context!) { IsSelected = { Value = isSelected } };
        TabItems.Add(item);
        return item;
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
                await editor.FlushMediaFingerprintsAsync();
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
}
