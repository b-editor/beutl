using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Engine;

namespace Beutl.Editor.Components.PropertyEditors.Views;

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

    // Opens the picker through PickerGuard, so a second request to the same host while it is open does nothing.
    public static Task HandleSelectTargetRequestAsync<TViewModel, TTarget>(
        Control host,
        TViewModel viewModel,
        Func<TViewModel, IReadOnlyList<TargetObjectInfo>> getAvailableTargets,
        Action<TViewModel, TTarget?> setTarget)
        where TViewModel : class
        where TTarget : CoreObject
    {
        return PickerGuard.RunAsync(host, async () =>
        {
            var result = await ShowTargetPickerAsync<TTarget>(host, getAvailableTargets(viewModel));

            if (result != null)
            {
                setTarget(viewModel, result);
            }
        });
    }
}
