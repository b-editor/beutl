using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Services;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public static class LibraryItemPickerHelper
{
    public static Task<object?> ShowAsync(
        Control host,
        SelectLibraryItemDialogViewModel selectVm,
        string multipleTypeFormat)
    {
        var dialog = new LibraryItemPickerFlyout(selectVm);
        PickerGuard.Show(host, dialog);
        var tcs = new TaskCompletionSource<object?>();
        dialog.Pinned += (_, item) => selectVm.Pin(item);
        dialog.Unpinned += (_, item) => selectVm.Unpin(item);
        dialog.Dismissed += (_, _) =>
        {
            selectVm.ReleaseThumbnails();
            tcs.TrySetResult(null);
        };
        dialog.Confirmed += (_, _) =>
        {
            selectVm.ReleaseThumbnails();
            switch (selectVm.SelectedItem.Value?.UserData)
            {
                case TargetObjectInfo target:
                    tcs.TrySetResult(target.Object);
                    break;
                case SingleTypeLibraryItem single:
                    tcs.TrySetResult(single.ImplementationType);
                    break;
                case MultipleTypeLibraryItem multi:
                    tcs.TrySetResult(multi.Types.GetValueOrDefault(multipleTypeFormat));
                    break;
                default:
                    tcs.TrySetResult(null);
                    break;
            }
        };

        return tcs.Task;
    }

    // Shows the same picker for a picker of its own, returning the item it confirmed.
    internal static Task<PinnableLibraryItem?> ShowItemAsync(Control host, ILibraryItemPickerViewModel selectVm)
    {
        var dialog = new LibraryItemPickerFlyout(selectVm);
        PickerGuard.Show(host, dialog);
        var tcs = new TaskCompletionSource<PinnableLibraryItem?>();
        dialog.Pinned += (_, item) => selectVm.Pin(item);
        dialog.Unpinned += (_, item) => selectVm.Unpin(item);
        dialog.Dismissed += (_, _) =>
        {
            tcs.TrySetResult(null);
        };
        dialog.Confirmed += (_, _) =>
        {
            tcs.TrySetResult(selectVm.SelectedItem.Value);
        };

        return tcs.Task;
    }
}
