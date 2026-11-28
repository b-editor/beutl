using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

// The editor state of one project, as an editor service holds it: the reservation of the project's files, the slow
// work shown over its editors, its running outputs, its tabs while the project is in the background, and the
// version control tracking it. An editor service keeps one workspace per project file, so a session's editor state
// is keyed by the editor service and the project's file, and it outlives a version-control operation that closes and
// reopens the project. Work on one project neither waits for, suspends nor covers the editors of another.
internal sealed partial class EditorWorkspace : IOutputOperationLeaseProvider
{
    private readonly ILogger _logger = Log.CreateLogger<EditorWorkspace>();
    private readonly ReactivePropertySlim<IProjectVersionControlService?> _projectVersionControlService = new();

    internal EditorWorkspace(EditorService owner, string? projectFile)
    {
        Owner = owner;
        ProjectFile = projectFile;
        _projectDirectory = projectFile is not null && Path.GetDirectoryName(projectFile) is { Length: > 0 } directory
            ? Path.TrimEndingDirectorySeparator(FilePathComparison.ResolveCanonicalPath(directory))
            : null;
        LifecycleActivity = _lifecycleActivity.ToReadOnlyReactivePropertySlim();
        ProjectVersionControlService = _projectVersionControlService.ToReadOnlyReactivePropertySlim();
        Admission = new ProjectFileWriteAdmission(this);
    }

    internal EditorService Owner { get; }

    // The project file the workspace belongs to; null for the editor service's own workspace, which holds the work
    // that concerns no loaded project, such as creating one.
    internal string? ProjectFile { get; }

    internal IProjectFileWriteAdmission Admission { get; }

    private readonly ReactivePropertySlim<Project?> _project = new();
    private ProjectAssetCoordinator? _projectAssets;

    // The loaded model of the project file, or null while none is loaded. A version-control operation that closes
    // and reopens the project changes it to the reopened model.
    internal IReadOnlyReactiveProperty<Project?> Project => _project;

    internal void RefreshProject(ProjectSessionRegistry sessions)
        => _project.Value = ProjectFile is { } file ? sessions.Find(file)?.Project : null;

    private ProjectColorPaletteHost? _colorPalettes;

    // The project's color palettes, which the color pickers show while the project is the active one.
    internal ProjectColorPaletteHost? ColorPalettes
        => ProjectFile is null ? null : _colorPalettes ??= new ProjectColorPaletteHost(this);

    // The project's assets, for the file browsers of its editors.
    internal ProjectAssetCoordinator? ProjectAssets
        => ProjectFile is null ? null : _projectAssets ??= Owner.ProjectAssetsFactory?.Invoke(this);

    // The tabs of the project while another one is active, or null while it is active or has none put aside.
    internal EditorService.InactiveTabSet? InactiveTabs { get; set; }

    // The version control tracking the project for this editor service. One coordinator claims it when the project
    // first becomes active and keeps it until the project closes.
    internal IProjectVersionControlCoordinator? VersionControl { get; set; }

    internal IProjectVersionControlSession? VersionControlSession => VersionControl as IProjectVersionControlSession;

    // What the version control tracking the project shows of it.
    internal IReadOnlyReactiveProperty<IProjectVersionControlService?> ProjectVersionControlService { get; }

    internal void PublishProjectVersionControlService(IProjectVersionControlService? service)
    {
        _projectVersionControlService.Value = service;
        Owner.RefreshProjectVersionControlService(this);
    }

    internal bool IsAt(string file)
        => ProjectFile is { } path && FilePathComparison.AreSameCanonicalPath(path, file);

    // The tabs of the project: those put aside, or the open ones while the project is the active one.
    internal EditorTabItem[] TabItems => Owner.GetTabItems(this);

    // Nothing holds the workspace, so the editor service can let it go once its project is unloaded.
    internal bool IsIdle
    {
        get
        {
            if (InactiveTabs is not null || VersionControl is not null || HasRunningOutputs)
                return false;

            lock (_workspaceOperationSync)
            {
                if (_worktreeMutationActive
                    || _activeOutputOperations != 0
                    || _activeProjectFileWrites != 0
                    || _activeEditorFileOpens != 0)
                {
                    return false;
                }
            }

            lock (_lifecycleActivitySync)
            {
                return _lifecycleActivities.Count == 0;
            }
        }
    }

    /// <summary>
    /// Disables the editors of this project until the returned handle is disposed; the editors of other projects
    /// stay usable.
    /// </summary>
    /// <remarks>
    /// A version-control transition holds this from before its pre-transition save until the
    /// project is closed. Releasing it earlier would let the user edit while the cycle awaits Git,
    /// and those edits would land after the safety snapshot and be discarded by the close.
    /// Suspensions share a per-context reference count with output execution, so they can complete
    /// in any order without re-enabling an editor still owned by another operation.
    /// </remarks>
    internal IDisposable SuspendEditors() => Owner.SuspendEditors(TabItems);

    // Resolves once no editor of this project has an edit session, so a transition waits for the user's gesture to end.
    internal Task WaitForEditSessionsAsync() => EditorService.WaitForEditSessionsAsync(TabItems);

    // Saves the scene editors of items, which belong to this project, before their tabs close.
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
                if (!await editor.SaveInReservedWorkspaceAsync())
                    throw new IOException(MessageStrings.FileSaveException);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save an editor before closing it.");
            Owner.AppServices.GetRequiredService<INotificationService>().ShowError(string.Empty, MessageStrings.FileSaveException);
            return false;
        }
    }
}
