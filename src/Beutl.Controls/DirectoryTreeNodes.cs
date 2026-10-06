using Avalonia.Collections;
using Avalonia.Controls;

namespace Beutl.Controls;

internal static class DirectoryTreeNodes
{
    public static void AddChildren(
        AvaloniaList<TreeViewItem> items,
        DirectoryInfo directory,
        FileSystemWatcher watcher,
        Func<string, object>? contextFactory)
    {
        // すべてのサブフォルダを追加
        foreach (DirectoryInfo item in directory.GetDirectories())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                items.Add(new DirectoryTreeItem(item, watcher, contextFactory)
                {
                    DataContext = contextFactory?.Invoke(item.FullName)
                });
            }
        }

        // 全てのファイル追加
        foreach (FileInfo item in directory.GetFiles())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                items.Add(new FileTreeItem(item)
                {
                    DataContext = contextFactory?.Invoke(item.FullName)
                });
            }
        }
    }

    public static TreeViewItem CreateNode(string fullPath, FileSystemWatcher watcher, Func<string, object>? contextFactory)
    {
        if (Directory.Exists(fullPath))
        {
            var di = new DirectoryInfo(fullPath);
            return new DirectoryTreeItem(di, watcher, contextFactory)
            {
                DataContext = contextFactory?.Invoke(fullPath)
            };
        }
        else
        {
            return new FileTreeItem(new FileInfo(fullPath))
            {
                DataContext = contextFactory?.Invoke(fullPath)
            };
        }
    }

    public static void SortNodes(AvaloniaList<TreeViewItem> items)
    {
        FileTreeItem[] fileArray = [.. items.OfType<FileTreeItem>().OrderBy(GetSortKey)];
        DirectoryTreeItem[] dirArray = [.. items.OfType<DirectoryTreeItem>().OrderBy(GetSortKey)];
        items.Clear();
        items.AddRange(dirArray);
        items.AddRange(fileArray);

        foreach (DirectoryTreeItem item in dirArray)
        {
            item.Sort();
        }
    }

    public static void RemoveNode(AvaloniaList<TreeViewItem> items, string? name)
    {
        TreeViewItem? item = FindByHeader(items, name);
        if (item != null)
        {
            items.Remove(item);
        }
    }

    public static void RenameNode(
        AvaloniaList<TreeViewItem> items,
        string? oldName,
        string fullPath,
        Func<string, object>? contextFactory)
    {
        TreeViewItem? item = FindByHeader(items, oldName);
        if (item is DirectoryTreeItem dir)
        {
            dir.Info = new DirectoryInfo(fullPath);
        }

        if (item is FileTreeItem file)
        {
            file.Info = new FileInfo(fullPath);
        }

        if (item != null)
            item.DataContext = contextFactory?.Invoke(fullPath);
    }

    private static TreeViewItem? FindByHeader(AvaloniaList<TreeViewItem> items, string? name)
    {
        return items.FirstOrDefault(i => i.Header is string str && str == name);
    }

    private static string GetSortKey(TreeViewItem item)
    {
        if (item.Header is string header)
        {
            return header;
        }
        else if (item.Header is TextBlock tb)
        {
            return tb.Text ?? string.Empty;
        }
        else
        {
            return item.Header?.ToString() ?? string.Empty;
        }
    }
}
