using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Services;

// The assets of one project, the one of workspace: each loaded project has its own, so the file browser of an editor
// keeps showing and changing its own project's assets while another project is active.
internal sealed class ProjectAssetCoordinator(ProjectService projects, EditorWorkspace workspace, DecoderRegistry decoders)
    : IProjectAssetHost
{
    private readonly ProjectAssetService _assets = new(decoders);
    private readonly EditorService editors = workspace.Owner;

    // The loaded model of the workspace's project.
    public IReadOnlyReactiveProperty<Project?> CurrentProject => workspace.Project;
    public event Action? AssetsChanged;

    // Opens the assets in a file browser of the editor; the project must be the active one.
    internal void OpenInFileTab()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (CurrentProject.Value is not { Uri.IsFile: true } project
            || !ReferenceEquals(projects.CurrentProject.Value, project)) return;
        using IDisposable? open = workspace.TryBeginEditorFileOpen();
        if (open == null) return;
        if (editors.SelectedTabItem.Value?.Context.Value is not EditViewModel editor)
        {
            if (project.Items.OfType<Scene>().FirstOrDefault(scene => scene.Uri is { IsFile: true }) is { } scene)
                editors.ActivateTabItem(scene);
            if (editors.SelectedTabItem.Value?.Context.Value is not EditViewModel opened)
            {
                editors.AppServices.GetRequiredService<INotificationService>().ShowWarning(ProjectAssetsStrings.Title, ProjectAssetsStrings.OpenScene);
                return;
            }
            editor = opened;
        }

        FileBrowserTabViewModel? existing = editor.FindToolTab<FileBrowserTabViewModel>(tab => tab.IsProjectAssetsView.Value)
            ?? editor.FindToolTab<FileBrowserTabViewModel>();
        FileBrowserTabViewModel browser = existing ?? new FileBrowserTabViewModel(editor);
        if (editor.OpenToolTab(browser)) browser.OpenProjectAssets();
        else if (existing == null) browser.Dispose();
    }

    public Task<ProjectAssetPreview> PreviewAsync(Project project, CancellationToken token)
        => ScanAsync(project, (paths, references) => _assets.Analyze(paths, references, Guid.NewGuid(), token).Preview, token);

    internal Task<ProjectAssetService.Inspection> InspectAsync(Project project, CancellationToken token)
        => ScanAsync(project, (paths, references) => _assets.Inspect(paths, references, token), token, includeElementIds: true);

    private async Task<T> ScanAsync<T>(Project project, Func<ProjectAssetPaths, ProjectAssetReferences, T> scan,
        CancellationToken token, bool includeElementIds = false)
    {
        VerifyCurrentProject(project);
        var paths = new ProjectAssetPaths(project);
        ProjectAssetReferences references = Capture(project, token, includeElementIds);
        T result = await Task.Run(() => scan(paths, references), token);
        VerifyCurrentProject(project);
        return result;
    }

    public Task ApplyAsync(Project project, ProjectAssetPreview preview, ProjectAssetAction action,
        IReadOnlyList<ProjectAssetItem> selected, CancellationToken token)
        => projects.RunExclusiveOfTransitionsAsync(async () =>
        {
            VerifyCurrentProject(project);
            await workspace.WaitForEditSessionsAsync().WaitAsync(token);
            using IDisposable suspension = workspace.SuspendEditors();
            using IProjectFileWriteLease write = await workspace.BeginProjectFileWriteAsync(token);
            VerifyCurrentProject(project);

            EditViewModel[] openEditors = [.. workspace.TabItems.Select(item => item.Context.Value).OfType<EditViewModel>()];
            foreach (EditViewModel editor in openEditors) editor.HistoryManager.FlushPendingMutations();
            if (action == ProjectAssetAction.Collect)
                await Task.WhenAll(openEditors.Select(editor => editor.Player.Pause()));

            var paths = new ProjectAssetPaths(project);
            ProjectAssetReferences references = Capture(project, token);
            var current = await Task.Run(() => _assets.Analyze(paths, references, preview.Batch, token), token);
            using var suppressRecording = new CompositeDisposable(openEditors.Where(IsTabOpen)
                .Select(editor => editor.HistoryManager.SuppressRecording()));
            var savedDocuments = await _assets.ApplyAsync(project, paths, current, action, selected, token);
            if (action == ProjectAssetAction.Collect)
            {
                var savedByScene = savedDocuments.ToLookup(
                    document => document as Scene ?? (document as IHierarchical)?.FindHierarchicalParent<Scene>(),
                    ReferenceEqualityComparer.Instance);
                foreach (EditViewModel editor in openEditors.Where(IsTabOpen))
                    editor.OnProjectAssetsRelinked(savedByScene[editor.Scene]);
            }
            AssetsChanged?.Invoke();
        }, token);

    // The editor still has its tab, open or put aside with its project.
    private bool IsTabOpen(EditViewModel editor)
        => workspace.TabItems.Any(item => ReferenceEquals(item.Context.Value, editor));

    private ProjectAssetReferences Capture(Project project, CancellationToken token, bool includeElementIds = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        var contexts = workspace.TabItems.Select(item => item.Context.Value).OfType<IEditorContext>().ToArray();
        var otherObjects = contexts.Select(context => context.Object).OfType<CoreObject>()
            .Where(obj => !ReferenceEquals(obj, project) && !project.Items.Any(item => ReferenceEquals(item, obj))
                && (obj is not IHierarchical hierarchical || !ReferenceEquals(hierarchical.FindHierarchicalParent<Project>(), project)));
        var history = contexts.Select(context => context.GetService(typeof(HistoryManager))).OfType<HistoryManager>()
            .SelectMany(history => history.GetRetainedOperations());
        return ProjectAssetReferences.Capture(project, otherObjects, history, token, includeElementIds);
    }

    // The plan concerns the loaded model of this workspace's project, active or in the background.
    private void VerifyCurrentProject(Project project)
    {
        if (!ReferenceEquals(CurrentProject.Value, project) || project.Uri is not { IsFile: true } uri
            || !File.Exists(uri.LocalPath))
            throw new ProjectAssetPlanChangedException();
    }
}
