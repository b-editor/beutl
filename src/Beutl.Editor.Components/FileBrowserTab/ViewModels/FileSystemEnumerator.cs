using System.Collections.ObjectModel;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Services;

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

    public static IEnumerable<FileSystemItemViewModel> EnumerateDirectory(FileThumbnailService thumbnails, string path)
    {
        foreach (var entry in EnumerateEntries(path))
        {
            yield return new FileSystemItemViewModel(thumbnails, entry.Path, entry.IsDirectory);
        }
    }

    private static IEnumerable<(string Path, bool IsDirectory)> EnumerateEntries(string path)
        => new DirectoryInfo(path).EnumerateFileSystemInfos().Where(IsVisible)
            .OrderByDescending(entry => entry is DirectoryInfo)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => (entry.FullName, entry is DirectoryInfo));

    internal static void RefreshCollection(FileThumbnailService thumbnails, ObservableCollection<FileSystemItemViewModel> collection, string path,
        bool includeChildren = true)
        => RefreshCollection(thumbnails, collection, EnumerateEntries(path), includeChildren);

    // Reuse existing rows in order, including a favorite that shares a path with a fixed folder.
    internal static void RefreshCollection(FileThumbnailService thumbnails, ObservableCollection<FileSystemItemViewModel> collection,
        IEnumerable<(string Path, bool IsDirectory)> entries, bool includeChildren = true)
    {
        var existing = collection.ToLookup(item => (item.FullPath, item.IsDirectory));
        var retained = new HashSet<FileSystemItemViewModel>();
        var desired = entries.Select(entry =>
        {
            var item = existing[entry].FirstOrDefault(candidate => !retained.Contains(candidate));
            if (item is not null)
                retained.Add(item);
            return (entry.Path, entry.IsDirectory, Item: item);
        }).ToArray();
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            var item = collection[i];
            if (!retained.Contains(item))
            {
                collection.RemoveAt(i);
                item.Dispose();
            }
        }
        for (int i = 0; i < desired.Length; i++)
        {
            if (desired[i].Item is { } item)
            {
                if (!ReferenceEquals(collection[i], item))
                    collection.Move(collection.IndexOf(item), i);
                item.RefreshLoadedEntries(includeChildren);
            }
            else
                collection.Insert(i, new FileSystemItemViewModel(thumbnails, desired[i].Path, desired[i].IsDirectory));
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

    public static void PopulateCollection(FileThumbnailService thumbnails, ObservableCollection<FileSystemItemViewModel> collection, string path)
    {
        DisposeAndClear(collection);

        foreach (var item in EnumerateDirectory(thumbnails, path))
        {
            collection.Add(item);
        }
    }
}
