using Avalonia;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using FluentAvalonia.Core;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public sealed class LibraryItemPickerFlyout : ConfirmablePickerFlyout<LibraryItemPickerFlyout, LibraryItemPickerFlyoutPresenter>
{
    private readonly ILibraryItemPickerViewModel _viewModel;

    public LibraryItemPickerFlyout(SelectLibraryItemDialogViewModel viewModel)
        : this((ILibraryItemPickerViewModel)viewModel)
    {
    }

    internal LibraryItemPickerFlyout(ILibraryItemPickerViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public event TypedEventHandler<LibraryItemPickerFlyout, PinnableLibraryItem>? Pinned;

    public event TypedEventHandler<LibraryItemPickerFlyout, PinnableLibraryItem>? Unpinned;

    protected override LibraryItemPickerFlyoutPresenter CreatePickerPresenter()
    {
        var pfp = new LibraryItemPickerFlyoutPresenter();
        pfp.Pinned += item => Pinned?.Invoke(this, item);
        pfp.Unpinned += item => Unpinned?.Invoke(this, item);
        pfp.Items = _viewModel.Items;
        pfp.Categories = _viewModel.Categories;
        pfp.SelectedCategoryIndex = _viewModel.SelectedCategoryIndex.Value;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SelectedCategoryIndexProperty)
            .Subscribe(v => _viewModel.SelectedCategoryIndex.Value = v);
        _viewModel.SelectedCategoryIndex.Skip(1)
            .Subscribe(v => pfp.SelectedCategoryIndex = v);
        pfp.ShowAll = _viewModel.ShowAll.Value;
        pfp.CanShowAll = _viewModel.CanShowAll;
        if (_viewModel is ILutLibraryFolderPicker folderPicker)
        {
            pfp.CanAddFolder = true;
            pfp.AddFolderClicked += () => _ = folderPicker.AddFolderAsync();
        }
        pfp.ShowReferencesTab = _viewModel.HasReferencesTab;
        if (_viewModel.ShowsOnlyReferences)
            pfp.ShowReferences = true;
        pfp.ReferenceItems = _viewModel.ReferenceItems;
        pfp.CanShowTiles = _viewModel.HasThumbnails;
        pfp.ShowTiles = _viewModel.ShowTiles.Value;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.ShowTilesProperty)
            .Subscribe(v => _viewModel.ShowTiles.Value = v);
        pfp.SelectedItem = _viewModel.SelectedItem.Value;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => _viewModel.SelectedItem.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.ShowAllProperty)
            .Subscribe(v => _viewModel.ShowAll.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => _viewModel.SearchText.Value = v);
        _viewModel.IsBusy.ObserveOnUIDispatcher()
            .Subscribe(v => pfp.IsBusy = v);
        return pfp;
    }

    protected override void FlushPendingSearch() => _viewModel.FlushSearch();
}
