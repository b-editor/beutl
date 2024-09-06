using Beutl.Services;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class TitleBreadcrumbBarViewModel
{
    private readonly MainViewModel _viewModel;
    private readonly EditorService _editorService;

    public TitleBreadcrumbBarViewModel(MainViewModel viewModel, EditorService editorService)
    {
        _viewModel = viewModel;
        _editorService = editorService;
        FileName = _editorService.SelectedTabItem
            .Select(i =>
                i?.FileName ?? IsProjectOpened.Select(b => b ? Message.File_is_not_selected : null))
            .Switch()
            .ToReadOnlyReactivePropertySlim();
    }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened => _viewModel.IsProjectOpened;

    public ReadOnlyReactivePropertySlim<string?> ProjectName => _viewModel.NameOfOpenProject;

    public ReadOnlyReactivePropertySlim<string?> FileName { get; }

    // TabItems
    // SelectedTabItem
    public ICoreList<EditorTabItem> TabItems => _editorService.TabItems;

    public IReactiveProperty<EditorTabItem?> SelectedTabItem => _editorService.SelectedTabItem;

    public ReactiveCommandSlim OpenFile => _viewModel.MenuBar.OpenFile;

    public ReactiveCommandSlim NewScene => _viewModel.MenuBar.NewScene;

    public ReactiveCommandSlim<EditorTabItem> CloseOrRemoveFile => _viewModel.MenuBar.CloseFileCore;
}
