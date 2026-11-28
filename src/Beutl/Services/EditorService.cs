using System.Diagnostics.CodeAnalysis;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed partial class EditorService
    : Beutl.Editor.Components.OutputTab.IOutputRunHost,
        Beutl.Editor.Services.IEditorFileUsage,
        IProjectFileWriteAdmissionHost
{
    public bool IsFileInUse(string path, bool isDirectory) => IsAnyFileInUse([(path, isDirectory)]);

    // Looks at every loaded project and every editor of this service, whether its project is active or in the
    // background. The objects are walked once and each folder is listed once, however many files are asked about.
    public bool IsAnyFileInUse(IReadOnlyCollection<(string Path, bool IsDirectory)> files)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (files.Count == 0) return false;

        FilePathComparison.ResolutionContext paths = FilePathComparison.CreateResolutionContext();
        var exactFiles = new HashSet<string>(StringComparer.Ordinal);
        var folders = new List<string>();
        foreach ((string path, bool isDirectory) in files)
        {
            string canonicalPath = paths.ResolveCanonicalPath(path);
            if (isDirectory)
                folders.Add(canonicalPath);
            else
                exactFiles.Add(canonicalPath);
        }

        IEnumerable<CoreObject> roots = _tabItems.Concat(InactiveTabItems)
            .Select(item => item.Context.Value?.Object)
            .OfType<CoreObject>();
        foreach (Project project in _sessions.Sessions.Select(session => session.Project))
            roots = roots.Concat(project.Items).Prepend(project);

        var filesFree = new HashSet<string>(StringComparer.Ordinal);
        // An open scene is also a project item.
        return roots.Distinct<CoreObject>(ReferenceEqualityComparer.Instance)
            .Any(root => CoreSerializer.Visit(root, visit => FileSourceFiles.EnumerateReferencedPaths(visit.Value).Any(Matches)));

        bool Matches(string file)
        {
            if (!filesFree.Add(file)) return false;

            string canonicalFile = paths.ResolveCanonicalPath(file);
            return exactFiles.Contains(canonicalFile)
                   || folders.Exists(folder => FilePathComparison.IsSameOrDescendantCanonicalPath(folder, canonicalFile));
        }
    }

    internal Beutl.Editor.Components.WebBrowserTab.IBrowserSettingsHost? BrowserSettingsHost { get; set; }
    internal Beutl.Editor.Components.FileBrowserTab.FileBrowserStorageProviderRegistry? StorageProviders { get; set; }
    // Makes the asset host of a project's workspace.
    internal Func<EditorWorkspace, ProjectAssetCoordinator>? ProjectAssetsFactory { get; set; }

    // The asset host of the active project.
    internal ProjectAssetCoordinator? ProjectAssets => ActiveWorkspace.Value.ProjectAssets;
    internal Beutl.Editor.Components.AiWorkspaceTab.IAiWorkspaceHost? AiWorkspaceHost { get; set; }
    internal Beutl.Editor.Components.AgentTab.IAgentWorkspaceHost? AgentWorkspaceHost { get; set; }
    internal Beutl.Api.BeutlApiApplication? ApiApplication { get; set; }
    internal Task<bool> ExtensionStartupRestrictedMode { get; set; } = Task.FromResult(false);
    private readonly CoreList<EditorTabItem> _tabItems;
    private readonly ILogger _logger = Log.CreateLogger<EditorService>();
    private readonly ExtensionProvider _extensionProvider;
    private readonly Action<Project, Uri> _serializeProject;
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

    internal IServiceProvider AppServices { get; }

    public EditorService(ExtensionProvider extensionProvider, IServiceProvider appServices)
        : this(
            extensionProvider,
            static (project, uri) => CoreSerializer.StoreToUri(project, uri),
            appServices)
    {
    }

    // sessions are the loaded projects whose editors this service holds, ProjectSessionRegistry.Current unless a
    // test gives its own.
    internal EditorService(
        ExtensionProvider extensionProvider,
        Action<Project, Uri> serializeProject,
        IServiceProvider appServices,
        ProjectSessionRegistry? sessions = null)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(serializeProject);
        ArgumentNullException.ThrowIfNull(appServices);
        AppServices = appServices;

        _extensionProvider = extensionProvider;
        _serializeProject = serializeProject;
        _tabItems = new() { ResetBehavior = ResetBehavior.Remove };
        _sessions = sessions ?? ProjectSessionRegistry.Current;
        _shellWorkspace = new EditorWorkspace(this, projectFile: null);
        _activeWorkspace = new ReactivePropertySlim<EditorWorkspace>(_shellWorkspace);
        // Follows the active session from the moment it changes, so the open tabs and the workspace that owns them
        // never disagree. The registry lives as long as the process, so it holds this service only weakly.
        FollowSessions(_sessions, this);
        LifecycleActivity = _lifecycleActivity.ToReadOnlyReactivePropertySlim();
        ProjectVersionControlService = _projectVersionControlService.ToReadOnlyReactivePropertySlim();
        HostProjectFileWriteAdmission.RegisterHost(this);
    }

    // Answers only for contexts in this service's own tabs, open or put aside with an inactive session, read at
    // the moment of the write, so a second EditorService in the process never gates a tab that belongs to this
    // one, and a context whose tab has closed is no longer admitted. A tab is admitted by the workspace of its
    // project, so a write in one project never waits for another.
    IProjectFileWriteAdmission? IProjectFileWriteAdmissionHost.TryGetAdmission(IEditorContext context)
    {
        foreach (EditorTabItem item in _tabItems)
        {
            if (ReferenceEquals(item.Context.Value, context))
                return ActiveWorkspace.Value.Admission;
        }

        foreach (EditorWorkspace workspace in Workspaces)
        {
            if (workspace.InactiveTabs is { } tabs && tabs.Items.Any(item => ReferenceEquals(item.Context.Value, context)))
                return workspace.Admission;
        }

        return null;
    }

    public ICoreList<EditorTabItem> TabItems => _tabItems;

    internal ExtensionProvider ExtensionProvider => _extensionProvider;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem { get; } = new ReactivePropertySlim<EditorTabItem?>();

    // Follows the selected tab and that tab's context, starting with the current answer on subscribe.
    internal IObservable<bool> IsSelectedEditor(IEditorContext editor)
        => SelectedTabItem
            .Select(tab => tab?.Context.Select(context => ReferenceEquals(context, editor)) ?? Observable.Return(false))
            .Switch()
            .DistinctUntilChanged();

    // What the version control tracking the active project shows of it.
    internal IReadOnlyReactiveProperty<IProjectVersionControlService?>
        ProjectVersionControlService
    { get; }

    // The version control tracking the active project.
    internal IProjectVersionControlCoordinator? ProjectVersionControlCoordinator
    {
        get => ActiveWorkspace.Value.VersionControl;
        set => ActiveWorkspace.Value.VersionControl = value;
    }

    // The fonts a scene editor forks: the fork of its project's session while the project is loaded, else the host's.
    internal Func<Project?, Beutl.Media.FontManager>? ProjectFonts { get; set; }

    internal IProjectVersionControlSession? ProjectVersionControlSession
        => ProjectVersionControlCoordinator as IProjectVersionControlSession;

    internal void PublishProjectVersionControlService(
        IProjectVersionControlService? service)
    {
        ActiveWorkspace.Value.PublishProjectVersionControlService(service);
    }

    internal static ValueTask<bool> SaveEditorInReservedWorkspaceAsync(ISavableEditorContext editor)
        => editor is EditViewModel sceneEditor ? sceneEditor.SaveInReservedWorkspaceAsync() : editor.SaveAsync();

    // Lifecycle callers reserve the workspace before saving the project and its editors.
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
        // The project need not be the active one: its editors are those of its own workspace.
        EditorWorkspace workspace = WorkspaceFor(project);
        EditorTabItem[] tabItems = workspace.TabItems;
        using IDisposable suspension = workspace.SuspendEditors();
        await Task.Run(
            () => _serializeProject(project, projectUri),
            cancellationToken);

        foreach (EditorTabItem item in tabItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Context.Value is EditViewModel sceneEditor)
                await sceneEditor.FlushMediaFingerprintsAsync();
            if (item.Context.Value is ISavableEditorContext editor && !await SaveEditorInReservedWorkspaceAsync(editor))
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

    internal async ValueTask<SceneRemovalResult> RemoveSceneFromProjectAsync(
        ProjectService projects, Project project, Scene scene, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var write = await WorkspaceFor(project).BeginProjectFileWriteAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // The project need not be the active one, but it must still be loaded.
        if (projects.CurrentTransition is not null || projects.Sessions.Find(project) is null)
            return SceneRemovalResult.SceneNotFound;

        bool dirty = WorkspaceFor(project).TabItems.FirstOrDefault(item => ReferenceEquals(item.Context.Value?.Object, scene)) is { } tab
                     && tab.Context.Value is EditViewModel editor
                     && (editor.IsDisposingOrDisposed || editor.HasUnsavedSceneChanges);
        return ProjectPersistence.RemoveScene(project, scene, dirty, persist: true);
    }

    public void ActivateTabItem(CoreObject obj) => ActivateTabItem(obj, inheritDockLayout: false);

    internal void ActivateNewScene(Scene scene) => ActivateTabItem(scene, inheritDockLayout: true);

    private void ActivateTabItem(CoreObject obj, bool inheritDockLayout)
    {
        ViewConfig viewConfig = AppServices.GetRequiredService<GlobalConfiguration>().ViewConfig;
        string path = obj.Uri!.LocalPath;
        viewConfig.UpdateRecentFile(path);

        if (TryGetTabItem(obj, out EditorTabItem? tabItem))
        {
            tabItem.IsSelected.Value = true;
            SelectedTabItem.Value = tabItem;
        }
        else
        {
            if (CreateTabItem(obj, isSelected: true, inheritDockLayout) is { } tabItem2)
            {
                SelectedTabItem.Value = tabItem2;
            }
        }
    }

    // Background editing shares the normal context, history, save admission and tab lifecycle,
    // without changing either the selected tab or the recent-file navigation state.
    internal EditorTabItem? GetOrCreateBackgroundTabItem(CoreObject obj, bool inheritDockLayout = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        using IDisposable? open = TryBeginEditorFileOpen();
        if (open is null)
            return null;

        return TryGetTabItem(obj, out EditorTabItem? existing)
            ? existing
            : CreateTabItem(obj, isSelected: false, inheritDockLayout);
    }

    private EditorTabItem? CreateTabItem(CoreObject obj, bool isSelected, bool inheritDockLayout)
    {
        EditorExtension? ext = _extensionProvider.MatchEditorExtension(obj.Uri!.LocalPath);
        if (ext is null
            || ExtensionGuard.Call(ext, AppServices.GetService<INotificationService>(),
                () => ext.TryCreateContext(obj, new EditorContextServices(this, _extensionProvider), out IEditorContext? created) ? created : null)
            is not { } context)
            return null;

        if (inheritDockLayout && context is EditViewModel editor
            && SelectedTabItem.Value?.Context.Value is EditViewModel { IsDisposingOrDisposed: false } currentEditor
            && BeutlDockFactory.IsPortraitScene(currentEditor.Scene) == BeutlDockFactory.IsPortraitScene(editor.Scene))
        {
            editor.DockHost.InheritLayout(currentEditor.DockHost);
        }

        var item = new EditorTabItem(context) { IsSelected = { Value = isSelected } };
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

    // Test seam: the app always saves before closing; only tests pass saveChanges: false.
    internal async ValueTask CloseTabItem(EditorTabItem item, bool saveChanges)
    {
        if (saveChanges && !await SaveSceneEditorsBeforeCloseAsync([item]))
            return;

        if (TabItems.Remove(item))
            await item.DisposeAsync();
    }
}
