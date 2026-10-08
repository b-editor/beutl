using System.Diagnostics;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using Beutl.Language;

using FluentAvalonia.UI.Controls;

using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Controls;

public sealed class DirectoryTreeView : TreeView
{
    private readonly FileSystemWatcher _watcher;
    private readonly AvaloniaList<TreeViewItem> _items = [];
    private readonly DirectoryInfo _directoryInfo;
    private readonly FAMenuFlyoutItem _open;
    private readonly FAMenuFlyoutItem _copy;
    private readonly FAMenuFlyoutItem _remove;
    private readonly FAMenuFlyoutItem _rename;
    private readonly FAMenuFlyoutItem _addfolder;
    private readonly List<Control> _menuItem;
    private readonly Func<string, object>? _contextFactory;

    public DirectoryTreeView(FileSystemWatcher watcher, Func<string, object>? contextFactory = null)
    {
        _watcher = watcher;
        _directoryInfo = new DirectoryInfo(watcher.Path);
        _contextFactory = contextFactory;
        ItemsSource = _items;
        InitSubDirectory();

        _watcher.Renamed += Watcher_Renamed;
        _watcher.Deleted += Watcher_Deleted;
        _watcher.Created += Watcher_Created;

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        _open = CreateMenuItem(Strings.Open, Icon.Open);
        _copy = CreateMenuItem(Strings.Copy, Icon.Copy);
        _remove = CreateMenuItem(Strings.Remove, Icon.Delete);
        _rename = CreateMenuItem(Strings.Rename, Icon.Rename);
        _addfolder = CreateMenuItem(Strings.NewFolder, Icon.Folder);

        _open.Click += Open;
        _copy.Click += Copy;
        _remove.Click += Remove;
        _rename.Click += Rename;
        _addfolder.Click += AddDirectory;

        _menuItem =
        [
            _open,
            new FAMenuFlyoutSubItem
            {
                Text = Strings.CreateNew,
                Items =
                {
                    _addfolder,
                },
            },
            _copy,
            _remove,
            _rename,
            new FAMenuFlyoutSeparator()
        ];

        var flyout = new FAMenuFlyout();
        foreach (var item in _menuItem) flyout.Items.Add(item);
        flyout.Opening += OnContextFlyoutOpening;
        ContextFlyout = flyout;
    }

    private static FAMenuFlyoutItem CreateMenuItem(string header, Icon icon)
    {
        return new FAMenuFlyoutItem
        {
            Text = header,
            IconSource = new FluentIconSource
            {
                Icon = icon,
                FontSize = 20,
            }
        };
    }

    private void OnContextFlyoutOpening(object? sender, EventArgs e)
    {
        _remove.IsEnabled = CanRemove();
        _open.IsEnabled = CanOpen();
    }

    protected override Type StyleKeyOverride => typeof(TreeView);

    private bool CanOpen()
    {
        return SelectedItem is DirectoryTreeItem or FileTreeItem;
    }

    private void Open(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is DirectoryTreeItem directoryTree)
        {
            directoryTree.IsExpanded = true;
        }
        else if (SelectedItem is FileTreeItem fileTree)
        {
            Process.Start(new ProcessStartInfo(fileTree.Info.FullName)
            {
                UseShellExecute = true,
            });
        }
    }

    private async void Copy(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { Clipboard: IClipboard clipboard, StorageProvider: IStorageProvider storageProvider })
        {
            if (SelectedItem is DirectoryTreeItem directoryTree)
            {
                await clipboard.SetTextAsync(directoryTree.Info.FullName);
            }
            else if (SelectedItem is FileTreeItem fileTree)
            {
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateFile(await storageProvider.TryGetFileFromPathAsync(fileTree.Info.FullName)));

                await clipboard.SetDataAsync(data);
            }
        }
    }

    private bool CanRemove()
    {
        return SelectedItem is DirectoryTreeItem or FileTreeItem;
    }

    private async void Remove(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is DirectoryTreeItem directory)
        {
            FAContentDialog dialog = CreateDeleteConfirmation(MessageStrings.ConfirmDeleteDirectory);

            if (await dialog.ShowAsync() == FAContentDialogResult.Primary)
            {
                directory.Info.Delete(true);
            }
        }
        else if (SelectedItem is FileTreeItem file)
        {
            FAContentDialog dialog = CreateDeleteConfirmation(MessageStrings.ConfirmDeleteFile);

            if (await dialog.ShowAsync() == FAContentDialogResult.Primary)
            {
                file.Info.Delete();
            }
        }
    }

    private static FAContentDialog CreateDeleteConfirmation(string content)
    {
        return new FAContentDialog
        {
            Content = content,
            PrimaryButtonText = Strings.OK,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
        };
    }

    private void Rename(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is DirectoryTreeItem directory)
        {
            directory.StartRename();
        }
        else if (SelectedItem is FileTreeItem file)
        {
            file.StartRename();
        }
    }

    private void AddDirectory(object? sender, RoutedEventArgs e)
    {
        string baseDir = GetTargetDirectory(SelectedItem);

        int count = 0;
        string str = Strings.NewFolder;
        string defaultName = str;

        while (Directory.Exists(Path.Combine(baseDir, defaultName)))
        {
            count++;
            defaultName = $"{str}{count}";
        }

        Directory.CreateDirectory(Path.Combine(baseDir, defaultName));
    }

    private string GetTargetDirectory(object? item)
    {
        string baseDir = _directoryInfo.FullName;
        if (item is DirectoryTreeItem directoryTree)
        {
            baseDir = directoryTree.Info.FullName;
        }
        else if (item is FileTreeItem fileTree && fileTree.Info.DirectoryName != null)
        {
            baseDir = fileTree.Info.DirectoryName;
        }

        return baseDir;
    }

    private void Watcher_Created(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string? parent = Path.GetDirectoryName(e.FullPath);

            if (parent == _directoryInfo.FullName)
            {
                _items.Add(DirectoryTreeNodes.CreateNode(e.FullPath, _watcher, _contextFactory));
            }

            Sort();
        });
    }

    private void Watcher_Deleted(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string? parent = Path.GetDirectoryName(e.FullPath);
            string? filename = Path.GetFileName(e.Name);

            if (parent == _directoryInfo.FullName)
            {
                DirectoryTreeNodes.RemoveNode(_items, filename);
            }
        });
    }

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string? parent = Path.GetDirectoryName(e.FullPath);
            string? oldFilename = Path.GetFileName(e.OldName);

            if (parent == _directoryInfo.FullName)
            {
                DirectoryTreeNodes.RenameNode(_items, oldFilename, e.FullPath, _contextFactory);
            }

            Sort();
        });
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File) && e.Source is ILogical logical)
        {
            e.DragEffects = DragDropEffects.Copy;

            TreeViewItem? treeViewItem = logical.FindLogicalAncestorOfType<TreeViewItem>();
            string baseDir = GetTargetDirectory(treeViewItem);

            foreach (IStorageItem src in e.DataTransfer.TryGetFiles() ?? [])
            {
                if (src is IStorageFile
                    && src.TryGetLocalPath() is string localPath)
                {
                    string dst = Path.Combine(baseDir, Path.GetFileName(localPath));
                    if (!File.Exists(dst))
                    {
                        File.Copy(localPath, dst);
                    }
                }
            }
        }
    }

    //サブフォルダツリー追加
    private void InitSubDirectory()
    {
        DirectoryTreeNodes.AddChildren(_items, _directoryInfo, _watcher, _contextFactory);
    }

    public void Sort()
    {
        DirectoryTreeNodes.SortNodes(_items);
    }
}
