using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public sealed partial class FileBrowserTabViewModel
{
    internal IProjectAssetHost? ProjectAssets { get; private set; }
    internal ReactivePropertySlim<ProjectAssetsViewModel?> ActiveProjectAssets { get; } = new();
    internal ReadOnlyReactivePropertySlim<bool> CanManageProjectAssets { get; private set; } = null!;
    public ReadOnlyReactivePropertySlim<bool> HasMenuActions { get; private set; } = null!;
    public ReadOnlyReactivePropertySlim<bool> IsProjectAssetsView { get; private set; } = null!;
    public ReadOnlyReactivePropertySlim<bool> ShowLocalBrowser { get; private set; } = null!;
    public ReadOnlyReactivePropertySlim<bool> ShowStorageBrowser { get; private set; } = null!;

    private void InitializeProjectAssets()
    {
        ProjectAssets = _editorContext.GetService<IProjectAssetHost>();
        IObservable<Project?> projects = ProjectAssets is { } host ? host.CurrentProject : Observable.Return<Project?>(null);
        CanManageProjectAssets = projects.Select(project => project?.Uri is { IsFile: true })
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        HasMenuActions = CanManageProjectAssets.Select(canManage => canManage || HasStorageProviders)
            .ToReadOnlyReactivePropertySlim(HasStorageProviders).DisposeWith(_disposables);
        IsProjectAssetsView = ActiveProjectAssets.Select(view => view != null).ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        ShowLocalBrowser = IsProjectAssetsView.CombineLatest(IsStorageView, (assets, storage) => !assets && !storage)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        ShowStorageBrowser = IsProjectAssetsView.CombineLatest(IsStorageView, (assets, storage) => !assets && storage)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        ActiveProjectAssets.DisposeWith(_disposables);

        if (ProjectAssets is { } service)
        {
            service.CurrentProject.Subscribe(project =>
            {
                if (ActiveProjectAssets.Value is { } view && !ReferenceEquals(project, view.Project)) CloseProjectAssets();
            }).DisposeWith(_disposables);
            service.AssetsChanged += ReconcileCurrentView;
            Disposable.Create(() => service.AssetsChanged -= ReconcileCurrentView).DisposeWith(_disposables);
        }
    }

    internal void OpenProjectAssets()
    {
        if (_disposed || ProjectAssets is not { CurrentProject.Value: { Uri.IsFile: true } project } host) return;
        if (ActiveProjectAssets.Value is { } existing && ReferenceEquals(existing.Project, project)) return;

        CloseProjectAssets();
        var view = new ProjectAssetsViewModel(host, project);
        ActiveProjectAssets.Value = view;
        _ = view.RefreshAsync();
    }

    internal void CloseProjectAssets()
    {
        ProjectAssetsViewModel? view = ActiveProjectAssets.Value;
        ActiveProjectAssets.Value = null;
        view?.Dispose();
    }
}
