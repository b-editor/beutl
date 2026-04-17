using Avalonia.Controls;

using Beutl.Engine;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

internal static class CoreObjectPickerHelper
{
    public static async Task<object?> ShowTypeOrReferenceAsync(
        Control control,
        ICoreObjectEditorViewModel viewModel)
    {
        Type propertyType = viewModel.PropertyAdapter.PropertyType;
        string format = propertyType.FullName!;
        var selectVm = new SelectLibraryItemDialogViewModel(format, propertyType);

        if (PresenterTypeAttribute.GetPresenterType(propertyType) != null)
        {
            var targets = viewModel.GetAvailableTargets();
            selectVm.InitializeReferences(targets);
        }

        return await LibraryItemPickerHelper.ShowAsync(control, selectVm, format);
    }
}
