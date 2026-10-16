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

    // A sealed type is created directly; otherwise the picked type is created, or the picked object referenced.
    public static async Task NewInstanceOrPickAsync(Control control, ICoreObjectEditorViewModel viewModel)
    {
        Type propertyType = viewModel.PropertyAdapter.PropertyType;

        if (propertyType.IsSealed)
        {
            viewModel.SetNewInstance(propertyType);
            return;
        }

        object? result = await PickerGuard.RunAsync(control, () => ShowTypeOrReferenceAsync(control, viewModel));

        switch (result)
        {
            case Type selectedType:
                viewModel.SetNewInstance(selectedType);
                break;
            case CoreObject target:
                viewModel.SetTarget(target);
                break;
        }
    }

    public static Task PickTargetAsync(Control control, ICoreObjectEditorViewModel viewModel)
    {
        return TargetSelectionHelper.HandleSelectTargetRequestAsync<ICoreObjectEditorViewModel, CoreObject>(
            control,
            viewModel,
            vm => vm.GetAvailableTargets(),
            (vm, target) => vm.SetTarget(target));
    }
}
