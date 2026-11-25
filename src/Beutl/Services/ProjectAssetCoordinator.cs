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

internal sealed class ProjectAssetCoordinator(ProjectService projects, EditorService editors, DecoderRegistry decoders)
    : IProjectAssetHost
{
    private readonly ProjectAssetService _assets = new(decoders);

    public IReadOnlyReactiveProperty<Project?> CurrentProject => projects.CurrentProject;
    public event Action? AssetsChanged;

    internal void OpenInFileTab()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (CurrentProject.Value is not { Uri.IsFile: true } project) return;
        using IDisposable? open = editors.TryBeginEditorFileOpen();
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

    public async Task<ProjectAssetPreview> PreviewAsync(Project project, CancellationToken token)
    {
        VerifyCurrentProject(project);
        var paths = new ProjectAssetPaths(project);
        ProjectAssetReferences references = Capture(project, token);
        var analysis = await Task.Run(() => _assets.Analyze(paths, references, Guid.NewGuid(), token), token);
        VerifyCurrentProject(project);
        return analysis.Preview;
    }

    public Task ApplyAsync(Project project, ProjectAssetPreview preview, ProjectAssetAction action,
        IReadOnlyList<ProjectAssetItem> selected, CancellationToken token)
        => projects.RunExclusiveOfTransitionsAsync(async () =>
        {
            VerifyCurrentProject(project);
            await editors.WaitForEditSessionsAsync().WaitAsync(token);
            using IDisposable suspension = editors.SuspendEditors();
            using IProjectFileWriteLease write = await editors.BeginProjectFileWriteAsync(token);
            VerifyCurrentProject(project);

            EditViewModel[] openEditors = [.. editors.TabItems.Select(item => item.Context.Value).OfType<EditViewModel>()];
            foreach (EditViewModel editor in openEditors) editor.HistoryManager.FlushPendingMutations();
            if (action == ProjectAssetAction.Collect)
                await Task.WhenAll(openEditors.Select(editor => editor.Player.Pause()));

            var paths = new ProjectAssetPaths(project);
            ProjectAssetReferences references = Capture(project, token);
            var current = await Task.Run(() => _assets.Analyze(paths, references, preview.Batch, token), token);
            using var suppressRecording = new CompositeDisposable(openEditors.Where(editor => editors.TryGetTabItem(editor.Scene, out _))
                .Select(editor => editor.HistoryManager.SuppressRecording()));
            await _assets.ApplyAsync(project, paths, current, action, selected, token);
            if (action == ProjectAssetAction.Collect)
            {
                foreach (EditViewModel editor in openEditors.Where(editor => editors.TryGetTabItem(editor.Scene, out _)))
                    editor.OnProjectAssetsRelinked();
            }
            AssetsChanged?.Invoke();
        }, token);

    private ProjectAssetReferences Capture(Project project, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var contexts = editors.TabItems.Select(item => item.Context.Value).OfType<IEditorContext>().ToArray();
        var otherObjects = contexts.Select(context => context.Object).OfType<CoreObject>()
            .Where(obj => !ReferenceEquals(obj, project) && !project.Items.Any(item => ReferenceEquals(item, obj))
                && (obj is not IHierarchical hierarchical || !ReferenceEquals(hierarchical.FindHierarchicalParent<Project>(), project)));
        var history = contexts.Select(context => context.GetService(typeof(HistoryManager))).OfType<HistoryManager>()
            .SelectMany(history => history.GetRetainedOperations());
        return ProjectAssetReferences.Capture(project, otherObjects, history, token);
    }

    private void VerifyCurrentProject(Project project)
    {
        if (!ReferenceEquals(projects.CurrentProject.Value, project) || project.Uri is not { IsFile: true } uri
            || !File.Exists(uri.LocalPath))
            throw new ProjectAssetPlanChangedException();
    }
}
