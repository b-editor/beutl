using Avalonia.Controls;
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
}
