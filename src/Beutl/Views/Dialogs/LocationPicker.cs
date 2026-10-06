using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Beutl.Views.Dialogs;

internal static class LocationPicker
{
    // The local folder picked on the window that holds owner; null when there is no such window
    // or the pick has no local path.
    public static async Task<string?> PickFolderAsync(Control owner)
    {
        if (TopLevel.GetTopLevel(owner) is not Window parent)
            return null;

        var options = new FolderPickerOpenOptions();
        IReadOnlyList<IStorageFolder> result = await parent.StorageProvider.OpenFolderPickerAsync(options);

        return result.Count > 0 && result[0].TryGetLocalPath() is string localPath
            ? localPath
            : null;
    }
}
