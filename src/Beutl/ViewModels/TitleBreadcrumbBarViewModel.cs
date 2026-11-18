using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class TitleBreadcrumbBarViewModel
{
    private readonly MainViewModel _viewModel;
    private readonly EditorService _editorService;
    internal INotificationService Notifications => _editorService.AppServices.GetRequiredService<INotificationService>();

    public TitleBreadcrumbBarViewModel(MainViewModel viewModel, EditorService editorService)
    {
        _viewModel = viewModel;
        _editorService = editorService;
        FileName = _editorService.SelectedTabItem
            .Select(i =>
                i?.FileName ?? IsProjectOpened.Select(b => b ? MessageStrings.FileNotSelected : null))
            .Switch()
            .ToReadOnlyReactivePropertySlim();
    }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened => _viewModel.IsProjectOpened;

    public ReadOnlyReactivePropertySlim<string?> ProjectName => _viewModel.NameOfOpenProject;

    public ReadOnlyReactivePropertySlim<string?> FileName { get; }

    public ICoreList<EditorTabItem> TabItems => _editorService.TabItems;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem => _editorService.SelectedTabItem;

    public AsyncReactiveCommand OpenFile => _viewModel.MenuBar.OpenFile;

    public AsyncReactiveCommand NewScene => _viewModel.MenuBar.NewScene;

    public ReactiveCommandSlim<EditorTabItem> CloseOrRemoveFile => _viewModel.MenuBar.CloseFileCore;
}
