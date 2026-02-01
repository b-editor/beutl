using System.Collections.ObjectModel;

namespace Beutl.ViewModels.Tools;

// ディレクトリ内のファイル/フォルダを列挙するユーティリティ。
// 隠しファイルを除外し、ディレクトリ優先・名前順でソートする。
internal static class FileSystemEnumerator
{
    // 指定ディレクトリ内のアイテムをViewModelとして列挙する。
    // ディレクトリが先、ファイルが後。隠しファイルは除外。名前順ソート。
    public static IEnumerable<FileSystemItemViewModel> EnumerateDirectory(string path)
    {
        var dirInfo = new DirectoryInfo(path);

        foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            if ((dir.Attributes & FileAttributes.Hidden) == 0)
            {
                yield return new FileSystemItemViewModel(dir.FullName, true);
            }
        }

        foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if ((file.Attributes & FileAttributes.Hidden) == 0)
            {
                yield return new FileSystemItemViewModel(file.FullName, false);
            }
        }
    }

    // コレクションをクリアして指定ディレクトリの内容で再構築する。
    public static void PopulateCollection(ObservableCollection<FileSystemItemViewModel> collection, string path)
    {
        foreach (var item in collection)
        {
            item.Dispose();
        }
        collection.Clear();

        foreach (var item in EnumerateDirectory(path))
        {
            collection.Add(item);
        }
    }
}
