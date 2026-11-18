using Avalonia.Controls;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Engine;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.Views;

internal static class CoreObjectPickerHelper
{
    // The type picker of an editor, with its fonts, else the control's, and its library.
    internal static SelectLibraryItemDialogViewModel CreateTypePicker(
        Control control, object? editor, string format, Type baseType, bool captureThumbnails = false)
    {
        return new SelectLibraryItemDialogViewModel(
            (editor as IServiceProvider)?.GetService<Beutl.Media.FontManager>()
                ?? Beutl.Controls.ControlFonts.GetOrCreate(control),
            ((IServiceProvider)editor!).GetRequiredService<LibraryService>(),
            format, baseType, captureThumbnails ? LibraryThumbnailCapture.ForObject(editor as IServiceProvider) : null);
    }

    public static async Task<object?> ShowTypeOrReferenceAsync(
        Control control,
        ICoreObjectEditorViewModel viewModel)
    {
        Type propertyType = viewModel.PropertyAdapter.PropertyType;
        string format = propertyType.FullName!;
        var selectVm = CreateTypePicker(control, viewModel, format, propertyType, captureThumbnails: true);

        if (PresenterTypeAttribute.GetPresenterType(propertyType) != null)
        {
            var targets = viewModel.GetAvailableTargets();
            selectVm.InitializeReferences(targets);
        }

        return await LibraryItemPickerHelper.ShowAsync(control, selectVm, format);
    }

    // A sealed type is created directly; otherwise the picked type is created, or the picked object referenced.
    public static async Task NewInstanceOrPickAsync(Control control, Control? button, ICoreObjectEditorViewModel viewModel)
    {
        Type propertyType = viewModel.PropertyAdapter.PropertyType;

        if (propertyType.IsSealed)
        {
            viewModel.SetNewInstance(propertyType);
            return;
        }

        object? result = await PickerGuard.RunAsync(control, button, () => ShowTypeOrReferenceAsync(control, viewModel));

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
}
