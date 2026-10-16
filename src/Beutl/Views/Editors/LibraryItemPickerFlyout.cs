using Avalonia;
using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.Core;
using Reactive.Bindings.Extensions;

namespace Beutl.Views.Editors;

internal sealed class LibraryItemPickerFlyout(SelectLibraryItemDialogViewModel viewModel)
    : ConfirmablePickerFlyout<LibraryItemPickerFlyout, LibraryItemPickerFlyoutPresenter>
{
    public event TypedEventHandler<LibraryItemPickerFlyout, PinnableLibraryItem>? Pinned;

    public event TypedEventHandler<LibraryItemPickerFlyout, PinnableLibraryItem>? Unpinned;

    protected override LibraryItemPickerFlyoutPresenter CreatePickerPresenter()
    {
        var pfp = new LibraryItemPickerFlyoutPresenter();
        pfp.Pinned += item => Pinned?.Invoke(this, item);
        pfp.Unpinned += item => Unpinned?.Invoke(this, item);
        pfp.Items = viewModel.Items;
        pfp.ShowAll = viewModel.ShowAll.Value;
        pfp.ShowReferencesTab = viewModel.HasReferencesTab;
        if (viewModel.ShowsOnlyReferences)
            pfp.ShowReferences = true;
        pfp.ReferenceItems = viewModel.ReferenceItems;
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.ShowAllProperty)
            .Subscribe(v => viewModel.ShowAll.Value = v);
        pfp.GetObservable(LibraryItemPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => viewModel.SearchText.Value = v);
        viewModel.IsBusy.ObserveOnUIDispatcher()
            .Subscribe(v => pfp.IsBusy = v);
        return pfp;
    }
}
