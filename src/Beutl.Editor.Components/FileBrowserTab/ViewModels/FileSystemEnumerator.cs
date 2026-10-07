using System.Collections.ObjectModel;
using Beutl.Editor.Components.FileBrowserTab.Services;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

// ディレクトリ内のファイル/フォルダを列挙するユーティリティ。
// 隠しファイルを除外し、ディレクトリ優先・名前順でソートする。
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

    // 指定ディレクトリ内のアイテムをViewModelとして列挙する。
    // ディレクトリが先、ファイルが後。隠しファイルは除外。名前順ソート。
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

    // コレクションをクリアして指定ディレクトリの内容で再構築する。
    public static void PopulateCollection(ObservableCollection<FileSystemItemViewModel> collection, string path)
    {
        DisposeAndClear(collection);

        foreach (var item in EnumerateDirectory(path))
        {
            collection.Add(item);
        }
    }
}
