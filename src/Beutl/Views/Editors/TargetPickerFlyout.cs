using Avalonia;
using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public sealed class TargetPickerFlyout(TargetPickerFlyoutViewModel viewModel)
    : ConfirmablePickerFlyout<TargetPickerFlyout, LibraryItemPickerFlyoutPresenter>
{
    protected override LibraryItemPickerFlyoutPresenter CreatePickerPresenter()
    {
        var presenter = new LibraryItemPickerFlyoutPresenter
        {
            ShowReferences = true
        };

        presenter.ReferenceItems = viewModel.Items;
        presenter.SelectedItem = viewModel.SelectedItem.Value;
        AvaloniaObjectExtensions.GetObservable(presenter, LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        AvaloniaObjectExtensions.GetObservable(presenter, LibraryItemPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => viewModel.SearchText.Value = v);
        return presenter;
    }
}
