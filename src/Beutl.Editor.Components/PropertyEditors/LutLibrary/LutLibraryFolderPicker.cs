using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// Asks for another folder to scan for LUTs, the same way the property menu and the picker's toolbar do.
internal static class LutLibraryFolderPicker
{
    public static async Task<string?> PickAsync(Control host)
    {
        if (TopLevel.GetTopLevel(host)?.StorageProvider is not { } storage)
            return null;

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = Strings.LutLibraryAddFolder
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
