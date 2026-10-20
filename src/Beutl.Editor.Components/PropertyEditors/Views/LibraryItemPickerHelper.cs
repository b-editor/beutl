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
        dialog.ShowAt(host, true);
        var tcs = new TaskCompletionSource<object?>();
        dialog.Pinned += (_, item) => selectVm.Pin(item);
        dialog.Unpinned += (_, item) => selectVm.Unpin(item);
        dialog.Dismissed += (_, _) => tcs.SetResult(null);
        dialog.Confirmed += (_, _) =>
        {
            switch (selectVm.SelectedItem.Value?.UserData)
            {
                case TargetObjectInfo target:
                    tcs.SetResult(target.Object);
                    break;
                case SingleTypeLibraryItem single:
                    tcs.SetResult(single.ImplementationType);
                    break;
                case MultipleTypeLibraryItem multi:
                    tcs.SetResult(multi.Types.GetValueOrDefault(multipleTypeFormat));
                    break;
                default:
                    tcs.SetResult(null);
                    break;
            }
        };

        return tcs.Task;
    }
}
