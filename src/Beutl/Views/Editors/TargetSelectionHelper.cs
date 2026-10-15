using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Engine;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public static class TargetSelectionHelper
{
    private static async Task<T?> ShowTargetPickerAsync<T>(
        Control host,
        IReadOnlyList<TargetObjectInfo> targets)
        where T : CoreObject
    {
        var pickerVm = new SelectLibraryItemDialogViewModel(targets);
        var flyout = new LibraryItemPickerFlyout(pickerVm);
        flyout.ShowAt(host, true);

        var tcs = new TaskCompletionSource<T?>();
        flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
        flyout.Confirmed += (_, _) => tcs.TrySetResult(
            (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as T);

        return await tcs.Task;
    }

    public static async Task HandleSelectTargetRequestAsync<TViewModel, TTarget>(
        Control host,
        TViewModel viewModel,
        Func<TViewModel, IReadOnlyList<TargetObjectInfo>> getAvailableTargets,
        Action<TViewModel, TTarget?> setTarget)
        where TViewModel : class
        where TTarget : CoreObject
    {
        var result = await ShowTargetPickerAsync<TTarget>(host, getAvailableTargets(viewModel));

        if (result != null)
        {
            setTarget(viewModel, result);
        }
    }
}
