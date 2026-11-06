using Avalonia.Controls;
using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public static class TargetSelectionHelper
{
    // Opens the picker through PickerGuard, so a second request from the same button closes it, and any other request
    // to the same host while it is open does nothing.
    public static Task PickTargetAsync(Control host, Control? button, ITargetPickerViewModel viewModel)
    {
        return PickerGuard.RunAsync(host, button, async () =>
        {
            var pickerVm = new SelectLibraryItemDialogViewModel(viewModel.GetAvailableTargets());
            object? result = await LibraryItemPickerHelper.ShowAsync(host, pickerVm, string.Empty);

            if (result is CoreObject target)
            {
                viewModel.SetTarget(target);
            }
        });
    }
}
