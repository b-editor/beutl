using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Engine;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public static class TargetSelectionHelper
{
    public static async Task<T?> ShowTargetPickerAsync<T>(
        Control host,
        Func<IReadOnlyList<TargetObjectInfo>> getAvailableTargets)
        where T : CoreObject
    {
        var targets = getAvailableTargets();
        var pickerVm = new TargetPickerFlyoutViewModel();
        pickerVm.Initialize(targets);

        var flyout = new TargetPickerFlyout(pickerVm);
        flyout.ShowAt(host);

        var tcs = new TaskCompletionSource<T?>();
        flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
        flyout.Confirmed += (_, _) => tcs.TrySetResult(
            (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as T);

        return await tcs.Task;
    }

    public static async Task HandleSelectTargetRequestAsync<TViewModel, TTarget>(
        Control host,
        TViewModel? viewModel,
        Func<TViewModel, IReadOnlyList<TargetObjectInfo>> getAvailableTargets,
        Action<TViewModel, TTarget?> setTarget)
        where TViewModel : class
        where TTarget : CoreObject
    {
        if (viewModel == null) return;

        var result = await ShowTargetPickerAsync<TTarget>(
            host,
            () => getAvailableTargets(viewModel));

        if (result != null)
        {
            setTarget(viewModel, result);
        }
    }
}
