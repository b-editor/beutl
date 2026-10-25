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
        foreach (var entry in EnumerateEntries(path))
        {
            yield return new FileSystemItemViewModel(entry.Path, entry.IsDirectory);
        }
    }

    private static IEnumerable<(string Path, bool IsDirectory)> EnumerateEntries(string path)
        => new DirectoryInfo(path).EnumerateFileSystemInfos().Where(IsVisible)
            .OrderByDescending(entry => entry is DirectoryInfo)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => (entry.FullName, entry is DirectoryInfo));

    internal static void RefreshCollection(ObservableCollection<FileSystemItemViewModel> collection, string path,
        bool includeChildren = true)
        => RefreshCollection(collection, EnumerateEntries(path), includeChildren);

    // Reconcile startup snapshots without replacing selected or expanded items that still exist.
    internal static void RefreshCollection(ObservableCollection<FileSystemItemViewModel> collection,
        IEnumerable<(string Path, bool IsDirectory)> entries, bool includeChildren = true)
    {
        var desired = entries.ToArray();
        var paths = desired.ToHashSet();
        var existing = collection.ToDictionary(item => (item.FullPath, item.IsDirectory));
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            var item = collection[i];
            if (!paths.Contains((item.FullPath, item.IsDirectory)))
            {
                collection.RemoveAt(i);
                item.Dispose();
            }
        }
        for (int i = 0; i < desired.Length; i++)
        {
            if (existing.TryGetValue(desired[i], out var item))
            {
                if (!ReferenceEquals(collection[i], item))
                    collection.Move(collection.IndexOf(item), i);
                item.RefreshLoadedEntries(includeChildren);
            }
            else
                collection.Insert(i, new FileSystemItemViewModel(desired[i].Path, desired[i].IsDirectory));
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
