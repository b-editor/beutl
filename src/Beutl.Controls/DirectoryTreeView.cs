using System.Diagnostics;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Language;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

public sealed class DirectoryTreeView : TreeView
{
    private readonly FileSystemWatcher _watcher;
    private readonly AvaloniaList<TreeViewItem> _items = [];
    private readonly DirectoryInfo _directoryInfo;
    private readonly MenuItem _open;
    private readonly MenuItem _copy;
    private readonly MenuItem _remove;
    private readonly MenuItem _rename;
    private readonly MenuItem _addfolder;
    private readonly List<object> _menuItem;
    private readonly Func<string, object> _contextFactory;

    public DirectoryTreeView(FileSystemWatcher watcher, Func<string, object> contextFactory = null)
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

        _open = new MenuItem
        {
            Header = Strings.Open,
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Open,
                FontSize = 20,
            }
        };
        _copy = new MenuItem
        {
            Header = Strings.Copy,
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Copy,
                FontSize = 20,
            }
        };
        _remove = new MenuItem
        {
            Header = Strings.Remove,
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Delete,
                FontSize = 20,
            }
        };
        _rename = new MenuItem
        {
            Header = Strings.Rename,
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Rename,
                FontSize = 20,
            }
        };
        _addfolder = new MenuItem
        {
            Header = Strings.NewFolder,
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Folder,
                FontSize = 20,
            }
        };

        _open.Click += Open;
        _copy.Click += Copy;
        _remove.Click += Remove;
        _rename.Click += Rename;
        _addfolder.Click += AddDirectory;

        _menuItem =
        [
            _open,
            new MenuItem
            {
                Header = Strings.CreateNew,
                Items = 
                {
                    _addfolder,
                },
            },
            _copy,
            _remove,
            _rename,
            new Separator()
        ];

        //foreach (var (asm, menus) in PluginManager.Default.FileMenus)
        //{
        //    foreach (var menu in menus)
        //    {
        //        var menuItem = new MenuItem
        //        {
        //            Header = menu.Name,
        //            DataContext = menu,
        //        };

        //        menuItem.Click += PluginFileMenu_Click;

        //        _menuItem.Add(menuItem);
        //    }
        //}

        ContextMenu = new ContextMenu
        {
            ItemsSource = _menuItem
        };

        ContextMenu.Opening += ContextMenu_ContextMenuOpening;
    }

    //private void PluginFileMenu_Click(object sender, RoutedEventArgs e)
    //{
    //    if (SelectedItem is FileTreeItem fileTree
    //        && sender is MenuItem menuItem
    //        && menuItem.DataContext is FileMenu fileMenu)
    //    {
    //        fileMenu.MainWindow = VisualRoot;
    //        fileMenu.Execute(fileTree.Info.FullName);
    //    }
    //}

    private void ContextMenu_ContextMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _remove.IsEnabled = CanRemove();
        _open.IsEnabled = CanOpen();

        //if (SelectedItem is FileTreeItem fileTree)
        //{
        //    foreach (var (menu, model) in _menuItem.OfType<MenuItem>()
        //        .Where(i => i.DataContext is FileMenu)
        //        .Select(i => (Menu: i, Model: (FileMenu)i.DataContext!)))
        //    {
        //        menu.IsVisible = model.IsMatch(fileTree.Info.FullName);
        //    }
        //}
        //else
        //{
        //    foreach (var menu in _menuItem.OfType<MenuItem>()
        //        .Where(i => i.DataContext is FileMenu))
        //    {
        //        menu.IsVisible = false;
        //    }
        //}
    }

    protected override Type StyleKeyOverride => typeof(TreeView);

    private bool CanOpen()
    {
        return SelectedItem is DirectoryTreeItem or FileTreeItem;
    }

    private void Open(object sender, RoutedEventArgs e)
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

    private async void Copy(object sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { Clipboard: IClipboard clipboard })
        {
            if (SelectedItem is DirectoryTreeItem directoryTree)
            {
                await clipboard.SetTextAsync(directoryTree.Info.FullName);
            }
            else if (SelectedItem is FileTreeItem fileTree)
            {
                var data = new DataObject();
                data.Set(DataFormats.Files, new string[]
                {
                    fileTree.Info.FullName
                });
                await clipboard.SetDataObjectAsync(data);
            }
        }
    }

    private bool CanRemove()
    {
        return SelectedItem is DirectoryTreeItem or FileTreeItem;
    }

    private async void Remove(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is DirectoryTreeItem directory)
        {
            var dialog = new ContentDialog
            {
                Content = Message.DoYouWantToDeleteThisDirectory,
                PrimaryButtonText = Strings.OK,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                IsSecondaryButtonEnabled = false,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                directory.Info.Delete(true);
            }
        }
        else if (SelectedItem is FileTreeItem file)
        {
            var dialog = new ContentDialog
            {
                Content = Message.DoYouWantToDeleteThisFile,
                PrimaryButtonText = Strings.OK,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                IsSecondaryButtonEnabled = false,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                file.Info.Delete();
            }
        }
    }

    private void Rename(object sender, RoutedEventArgs e)
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

    private void AddDirectory(object sender, RoutedEventArgs e)
    {
        string baseDir = _directoryInfo.FullName;
        if (SelectedItem is DirectoryTreeItem directoryTree)
        {
            baseDir = directoryTree.Info.FullName;
        }
        else if (SelectedItem is FileTreeItem fileTree && fileTree.Info.DirectoryName != null)
        {
            baseDir = fileTree.Info.DirectoryName;
        }

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

    private void Watcher_Created(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string parent = Path.GetDirectoryName(e.FullPath);

            if (parent == _directoryInfo.FullName)
            {
                if (Directory.Exists(e.FullPath))
                {
                    var di = new DirectoryInfo(e.FullPath);
                    _items.Add(new DirectoryTreeItem(di, _watcher, _contextFactory)
                    {
                        DataContext = _contextFactory?.Invoke(e.FullPath)
                    });
                }
                else
                {
                    _items.Add(new FileTreeItem(new FileInfo(e.FullPath))
                    {
                        DataContext = _contextFactory?.Invoke(e.FullPath)
                    });
                }
            }

            Sort();
        });
    }

    private void Watcher_Deleted(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string parent = Path.GetDirectoryName(e.FullPath);
            string filename = Path.GetFileName(e.Name);

            if (parent == _directoryInfo.FullName)
            {
                TreeViewItem item = _items.FirstOrDefault(i => i.Header is string str && str == filename);
                if (item != null)
                {
                    _items.Remove(item);
                }
            }
        });
    }

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string parent = Path.GetDirectoryName(e.FullPath);
            string oldFilename = Path.GetFileName(e.OldName);
            string newFilename = Path.GetFileName(e.Name);

            if (parent == _directoryInfo.FullName)
            {
                TreeViewItem item = _items.FirstOrDefault(i => i.Header is string str && str == oldFilename);
                if (item is DirectoryTreeItem dir)
                {
                    dir.Info = new DirectoryInfo(e.FullPath);
                }

                if (item is FileTreeItem file)
                {
                    file.Info = new FileInfo(e.FullPath);
                }

                item.DataContext = _contextFactory?.Invoke(e.FullPath);
            }

            Sort();
        });
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files) && e.Source is ILogical logical)
        {
            e.DragEffects = DragDropEffects.Copy;

            TreeViewItem treeViewItem = logical.FindLogicalAncestorOfType<TreeViewItem>();
            string baseDir = _directoryInfo.FullName;

            if (treeViewItem is DirectoryTreeItem directoryTreeItem)
                baseDir = directoryTreeItem.Info.FullName;
            else if (treeViewItem is FileTreeItem fileTree && fileTree.Info.DirectoryName != null)
                baseDir = fileTree.Info.DirectoryName;

            foreach (IStorageItem src in e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>())
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
        //すべてのサブフォルダを追加
        foreach (DirectoryInfo item in _directoryInfo.GetDirectories())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                _items.Add(new DirectoryTreeItem(item, _watcher, _contextFactory)
                {
                    DataContext = _contextFactory?.Invoke(item.FullName)
                });
            }
        }

        // 全てのファイル追加
        foreach (FileInfo item in _directoryInfo.GetFiles())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                _items.Add(new FileTreeItem(item)
                {
                    DataContext = _contextFactory?.Invoke(item.FullName)
                });
            }
        }
    }

    public void Sort()
    {
        static string Func(TreeViewItem item)
        {
            if (item.Header is string header)
            {
                return header;
            }
            else if (item.Header is TextBlock tb)
            {
                return tb.Text;
            }
            else
            {
                return item.Header.ToString();
            }
        }

        FileTreeItem[] fileArray = [.. _items.OfType<FileTreeItem>().OrderBy(Func)];
        DirectoryTreeItem[] dirArray = [.. _items.OfType<DirectoryTreeItem>().OrderBy(Func)];
        _items.Clear();
        _items.AddRange(dirArray);
        _items.AddRange(fileArray);

        foreach (DirectoryTreeItem item in dirArray)
        {
            item.Sort();
        }
    }
}

public sealed class FileTreeItem : TreeViewItem
{
    private FileInfo _info;
    // 名前を変更中
    private bool _isRenaming;

    public FileTreeItem(FileInfo info)
    {
        _info = info;
        Header = Info.Name;
        DoubleTapped += FileTreeItem_DoubleTapped;
    }

    public FileInfo Info
    {
        get => _info;
        set
        {
            _info = value;
            Header = _info.Name;
        }
    }

    protected override Type StyleKeyOverride => typeof(TreeViewItem);

    public void Refresh()
    {
        Info.Refresh();
        Header = Info.Name;
    }

    public void StartRename()
    {
        if (!_isRenaming)
        {
            _isRenaming = true;

            TextBox tb;
            Header = tb = new TextBox
            {
                Text = Info.Name
            };

            tb.SelectAll();
            tb.AddHandler(KeyUpEvent, TextBox_KeyUp, RoutingStrategies.Tunnel);
            tb.TemplateApplied += TextBox_TemplateApplied;
            tb.LostFocus += TextBox_LostFocus;
        }
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        EndRename();
    }

    private void TextBox_TemplateApplied(object sender, TemplateAppliedEventArgs e)
    {
        ((TextBox)sender).Focus();
    }

    private void TextBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    EndRename();
                    break;
                case Key.Escape:
                    tb.Text = Info.Name;
                    EndRename();
                    break;
                default:
                    break;
            }
        }
    }

    public async void EndRename()
    {
        if (_isRenaming && Header is TextBox tb)
        {
            _isRenaming = false;
            string old = Info.FullName;
            string @new = Path.Combine(Info.DirectoryName, tb.Text);
            if (File.Exists(@new))
            {
                string content = Message.CannotRenameBecauseConflicts;
                content = string.Format(content, Info.Name, tb.Text);
                var dialog = new ContentDialog()
                {
                    CloseButtonText = Strings.Close,
                    Content = content,
                    DefaultButton = ContentDialogButton.None,
                    IsPrimaryButtonEnabled = false,
                    IsSecondaryButtonEnabled = false,
                };

                await dialog.ShowAsync();
            }
            else if (string.Compare(old, @new, StringComparison.OrdinalIgnoreCase) != 0)
            {
                File.Move(old, @new);
                _info = new FileInfo(@new);
            }


            tb.RemoveHandler(KeyUpEvent, TextBox_KeyUp);
            tb.TemplateApplied -= TextBox_TemplateApplied;
            tb.LostFocus -= TextBox_LostFocus;
            Header = _info.Name;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            TreeView parent = this.FindLogicalAncestorOfType<TreeView>();
            parent.SelectedItem = this;
            Refresh();

            var dataObject = new DataObject();
            dataObject.Set(DataFormats.Files, new string[] { Info.FullName });

            // ドラッグ開始
            DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy).ConfigureAwait(false);
        }
    }

    private void FileTreeItem_DoubleTapped(object sender, RoutedEventArgs e)
    {
        Refresh();
        Process.Start(new ProcessStartInfo(Info.FullName)
        {
            UseShellExecute = true,
        });
    }
}

public sealed class DirectoryTreeItem : TreeViewItem
{
    private readonly AvaloniaList<TreeViewItem> _items = [];
    private readonly FileSystemWatcher _watcher;
    private readonly Func<string, object> _contextFactory;
    // //サブフォルダを作成済みかどうか
    private bool _isAdd;
    private DirectoryInfo _info;
    // 名前を変更中
    private bool _isRenaming;

    public DirectoryTreeItem(DirectoryInfo info, FileSystemWatcher watcher, Func<string, object> contextFactory = null)
    {
        _info = info;
        Header = info.Name;
        ItemsSource = _items;
        _watcher = watcher;
        _contextFactory = contextFactory;
        if (info.EnumerateFileSystemInfos().Any())
        {
            _items.Add(new TreeViewItem());
        }

        this.GetObservable(IsExpandedProperty).Subscribe(v =>
        {
            if (!_isAdd && v)
            {
                InitSubDirectory();
            }
        });
    }

    public DirectoryInfo Info
    {
        get => _info;
        set
        {
            _info = value;
            Header = _info.Name;
        }
    }

    protected override Type StyleKeyOverride => typeof(TreeViewItem);

    //サブフォルダツリー追加
    private void InitSubDirectory()
    {
        Refresh();
        _items.Clear();
        // すべてのサブフォルダを追加
        foreach (DirectoryInfo item in Info.GetDirectories())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                _items.Add(new DirectoryTreeItem(item, _watcher, _contextFactory)
                {
                    DataContext = _contextFactory?.Invoke(item.FullName)
                });
            }
        }

        // 全てのファイル追加
        foreach (FileInfo item in Info.GetFiles())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                _items.Add(new FileTreeItem(item)
                {
                    DataContext = _contextFactory?.Invoke(item.FullName)
                });
            }
        }

        _isAdd = true;
    }

    public void Sort()
    {
        static string Func(TreeViewItem item)
        {
            if (item.Header is string header)
            {
                return header;
            }
            else if (item.Header is TextBlock tb)
            {
                return tb.Text;
            }
            else
            {
                return item.Header?.ToString();
            }
        }

        if (!_isAdd)
            return;

        FileTreeItem[] fileArray = [.. _items.OfType<FileTreeItem>().OrderBy(Func)];
        DirectoryTreeItem[] dirArray = [.. _items.OfType<DirectoryTreeItem>().OrderBy(Func)];
        _items.Clear();
        _items.AddRange(dirArray);
        _items.AddRange(fileArray);

        foreach (DirectoryTreeItem item in dirArray)
        {
            item.Sort();
        }
    }

    public void Refresh()
    {
        Info.Refresh();
        Header = Info.Name;
    }

    public void StartRename()
    {
        if (!_isRenaming)
        {
            _isRenaming = true;

            TextBox tb;
            Header = tb = new TextBox
            {
                Text = Info.Name
            };

            tb.SelectAll();
            tb.AddHandler(KeyUpEvent, TextBox_KeyUp, RoutingStrategies.Tunnel);
            tb.TemplateApplied += TextBox_TemplateApplied;
            tb.LostFocus += TextBox_LostFocus;
        }
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        EndRename();
    }

    private void TextBox_TemplateApplied(object sender, TemplateAppliedEventArgs e)
    {
        ((TextBox)sender).Focus();
    }

    private void TextBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    EndRename();
                    break;
                case Key.Escape:
                    tb.Text = Info.Name;
                    EndRename();
                    break;
                default:
                    break;
            }
        }
    }

    public async void EndRename()
    {
        if (_isRenaming && Header is TextBox tb)
        {
            _isRenaming = false;
            string old = Info.FullName;
            string @new = Path.Combine(Info.Parent.FullName, tb.Text);
            if (Directory.Exists(@new))
            {
                string content = Message.CannotRenameBecauseConflicts;
                content = string.Format(content, Info.Name, tb.Text);
                var dialog = new ContentDialog()
                {
                    CloseButtonText = Strings.Close,
                    Content = content,
                    DefaultButton = ContentDialogButton.None,
                    IsPrimaryButtonEnabled = false,
                    IsSecondaryButtonEnabled = false,
                };

                await dialog.ShowAsync();
            }
            else if (string.Compare(old, @new, StringComparison.OrdinalIgnoreCase) != 0)
            {
                Directory.Move(old, @new);
                _info = new DirectoryInfo(@new);
            }


            tb.RemoveHandler(KeyUpEvent, TextBox_KeyUp);
            tb.TemplateApplied -= TextBox_TemplateApplied;
            tb.LostFocus -= TextBox_LostFocus;
            Header = _info.Name;
        }
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);

        _watcher.Renamed += Watcher_Renamed;
        _watcher.Deleted += Watcher_Deleted;
        _watcher.Created += Watcher_Created;
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);

        _watcher.Renamed -= Watcher_Renamed;
        _watcher.Deleted -= Watcher_Deleted;
        _watcher.Created -= Watcher_Created;
    }

    private void Watcher_Created(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Refresh();
            string parent = Path.GetDirectoryName(e.FullPath);

            if (parent == Info.FullName)
            {
                if (Directory.Exists(e.FullPath))
                {
                    var di = new DirectoryInfo(e.FullPath);
                    _items.Add(new DirectoryTreeItem(di, _watcher, _contextFactory)
                    {
                        DataContext = _contextFactory?.Invoke(e.FullPath)
                    });
                }
                else
                {
                    _items.Add(new FileTreeItem(new FileInfo(e.FullPath))
                    {
                        DataContext = _contextFactory?.Invoke(e.FullPath)
                    });
                }

                Sort();
            }
        });
    }

    private void Watcher_Deleted(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Refresh();
            string parent = Path.GetDirectoryName(e.FullPath);
            string filename = Path.GetFileName(e.Name);

            if (parent == Info.FullName)
            {
                TreeViewItem item = _items.FirstOrDefault(i => i.Header is string str && str == filename);
                if (item != null)
                {
                    _items.Remove(item);
                }
            }
        });
    }

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Refresh();
            string parent = Path.GetDirectoryName(e.FullPath);
            string oldFilename = Path.GetFileName(e.OldName);
            string newFilename = Path.GetFileName(e.Name);

            if (parent == Info.FullName)
            {
                TreeViewItem item = _items.FirstOrDefault(i => i.Header is string str && str == oldFilename);
                if (item is DirectoryTreeItem dir)
                {
                    dir.Info = new DirectoryInfo(e.FullPath);
                }

                if (item is FileTreeItem file)
                {
                    file.Info = new FileInfo(e.FullPath);
                }

                item.DataContext = _contextFactory?.Invoke(e.FullPath);
            }

            Sort();
        });
    }
}
