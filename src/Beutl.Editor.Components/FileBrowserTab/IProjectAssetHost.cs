using Beutl.Editor;
using Reactive.Bindings;

namespace Beutl.Editor.Components.FileBrowserTab;

internal interface IProjectAssetHost
{
    IReadOnlyReactiveProperty<Project?> CurrentProject { get; }
    event Action? AssetsChanged;
    Task<ProjectAssetPreview> PreviewAsync(Project project, CancellationToken token);
    Task ApplyAsync(Project project, ProjectAssetPreview preview, ProjectAssetAction action,
        IReadOnlyList<ProjectAssetItem> selected, CancellationToken token);
}
