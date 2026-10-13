using System.Collections.ObjectModel;
using Beutl.Editor.Components.FileBrowserTab.Services;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

internal static class FileSystemEnumerator
{
    internal static bool IsVisible(FileSystemInfo entry)
        => (entry.Attributes & FileAttributes.Hidden) == 0
           && (entry is DirectoryInfo || !DirectoryWatcherService.IsEditorSaveTemporaryFile(entry.FullName));

    internal static bool HasEntriesChanged(IEnumerable<FileSystemItemViewModel> items, string path)
    {
        var displayed = items.Select(item => (Path.GetFileName(item.FullPath), item.IsDirectory)).ToHashSet();
        return !displayed.SetEquals(new DirectoryInfo(path).EnumerateFileSystemInfos()
            .Where(IsVisible).Select(entry => (entry.Name, entry is DirectoryInfo)));
    }

    public static IEnumerable<FileSystemItemViewModel> EnumerateDirectory(string path)
    {
        var dirInfo = new DirectoryInfo(path);

        foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (IsVisible(dir))
            {
                yield return new FileSystemItemViewModel(dir.FullName, true);
            }
        }

        foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (IsVisible(file))
            {
                yield return new FileSystemItemViewModel(file.FullName, false);
            }
        }
    }

    // Items own their thumbnail and metadata loads, so they are disposed before they are dropped.
    internal static void DisposeAndClear(ObservableCollection<FileSystemItemViewModel> collection)
    {
        foreach (var item in collection)
        {
            item.Dispose();
        }
        collection.Clear();
    }

    public static void PopulateCollection(ObservableCollection<FileSystemItemViewModel> collection, string path)
    {
        DisposeAndClear(collection);

        foreach (var item in EnumerateDirectory(path))
        {
            collection.Add(item);
        }
    }
}
