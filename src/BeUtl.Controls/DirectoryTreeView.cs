using System.Diagnostics;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Controls;

public sealed class DirectoryTreeView : TreeView, IStyleable
{
    private readonly FileSystemWatcher _watcher;
    private readonly AvaloniaList<TreeViewItem> _items = new();
    private readonly DirectoryInfo _directoryInfo;
    private readonly MenuItem _open;
    private readonly MenuItem _copy;
    private readonly MenuItem _remove;
    private readonly MenuItem _rename;
    private readonly MenuItem _addfolder;
    private readonly List<object> _menuItem;

    public DirectoryTreeView(FileSystemWatcher watcher)
    {
        _watcher = watcher;
        _directoryInfo = new DirectoryInfo(watcher.Path);
        Items = _items;
        InitSubDirectory();

        _watcher.Renamed += Watcher_Renamed;
        _watcher.Deleted += Watcher_Deleted;
        _watcher.Created += Watcher_Created;

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        _open = new MenuItem
        {
            [!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension("OpenString"),
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Open,
                FontSize = 20,
            }
        };
        _copy = new MenuItem
        {
            [!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension("CopyString"),
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Copy,
                FontSize = 20,
            }
        };
        _remove = new MenuItem
        {
            [!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension("RemoveString"),
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Delete,
                FontSize = 20,
            }
        };
        _rename = new MenuItem
        {
            [!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension("RenameString"),
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Rename,
                FontSize = 20,
            }
        };
        _addfolder = new MenuItem
        {
            [!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension("NewFolderString"),
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

        _menuItem = new List<object>
        {
            _open,
            new MenuItem
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = new DynamicResourceExtension("CreateNewString"),
                Items = new object[]
                {
                    _addfolder,
                },
            },
            _copy,
            _remove,
            _rename,
        };

        _menuItem.Add(new Separator());

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
            Items = _menuItem
        };

        ContextMenu.ContextMenuOpening += ContextMenu_ContextMenuOpening;
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

    Type IStyleable.StyleKey => typeof(TreeView);

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
        if (SelectedItem is DirectoryTreeItem directoryTree)
        {
            await Application.Current.Clipboard.SetTextAsync(directoryTree.Info.FullName);
        }
        else if (SelectedItem is FileTreeItem fileTree)
        {
            var data = new DataObject();
            data.Set(DataFormats.FileNames, new string[]
            {
                fileTree.Info.FullName
            });
            await Application.Current.Clipboard.SetDataObjectAsync(data);
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
                [!ContentControl.ContentProperty] = new DynamicResourceExtension("S.DirectoryTreeView.DoYouWantToDeleteThisDirectory"),
                [!ContentDialog.PrimaryButtonTextProperty] = new DynamicResourceExtension("S.DirectoryTreeView.OK"),
                [!ContentDialog.CloseButtonTextProperty] = new DynamicResourceExtension("S.DirectoryTreeView.Cancel"),
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
                [!ContentControl.ContentProperty] = new DynamicResourceExtension("S.DirectoryTreeView.DoYouWantToDeleteThisFile"),
                [!ContentDialog.PrimaryButtonTextProperty] = new DynamicResourceExtension("S.DirectoryTreeView.OK"),
                [!ContentDialog.CloseButtonTextProperty] = new DynamicResourceExtension("S.DirectoryTreeView.Cancel"),
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
        string str = Application.Current.FindResource("S.DirectoryTreeView.NewFolder") as string ?? "New Folder";
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
                    _items.Add(new DirectoryTreeItem(di, _watcher));
                }
                else
                {
                    _items.Add(new FileTreeItem(new FileInfo(e.FullPath)));
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
            }

            Sort();
        });
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.FileNames))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.FileNames) && e.Source is ILogical logical)
        {
            e.DragEffects = DragDropEffects.Copy;

            TreeViewItem treeViewItem = logical.FindLogicalAncestorOfType<TreeViewItem>();
            string baseDir = _directoryInfo.FullName;

            if (treeViewItem is DirectoryTreeItem directoryTreeItem)
                baseDir = directoryTreeItem.Info.FullName;
            else if (treeViewItem is FileTreeItem fileTree && fileTree.Info.DirectoryName != null)
                baseDir = fileTree.Info.DirectoryName;

            foreach (string src in e.Data.GetFileNames() ?? Enumerable.Empty<string>())
            {
                string dst = Path.Combine(baseDir, Path.GetFileName(src));
                if (!File.Exists(dst))
                {
                    File.Copy(src, dst);
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
                _items.Add(new DirectoryTreeItem(item, _watcher));
            }
        }

        // 全てのファイル追加
        foreach (FileInfo item in _directoryInfo.GetFiles())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                _items.Add(new FileTreeItem(item));
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

        FileTreeItem[] fileArray = _items.OfType<FileTreeItem>().OrderBy(Func).ToArray();
        DirectoryTreeItem[] dirArray = _items.OfType<DirectoryTreeItem>().OrderBy(Func).ToArray();
        _items.Clear();
        _items.AddRange(dirArray);
        _items.AddRange(fileArray);

        foreach (DirectoryTreeItem item in dirArray)
        {
            item.Sort();
        }
    }
}

public sealed class FileTreeItem : TreeViewItem, IStyleable
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

    Type IStyleable.StyleKey => typeof(TreeViewItem);

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
                string content = (string)Application.Current.FindResource("S.DirectoryTreeView.CannotRenameBecauseNewNameConflicts");
                content = string.Format(content, Info.Name, tb.Text);
                var dialog = new ContentDialog()
                {
                    [!ContentDialog.CloseButtonTextProperty] = new DynamicResourceExtension("CloseString"),
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
            dataObject.Set(DataFormats.FileNames, new string[] { Info.FullName });

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

public sealed class DirectoryTreeItem : TreeViewItem, IStyleable
{
    private readonly AvaloniaList<TreeViewItem> _items = new();
    private readonly FileSystemWatcher _watcher;
    // //サブフォルダを作成済みかどうか
    private bool _isAdd;
    private DirectoryInfo _info;
    // 名前を変更中
    private bool _isRenaming;

    public DirectoryTreeItem(DirectoryInfo info, FileSystemWatcher watcher)
    {
        _info = info;
        Header = info.Name;
        Items = _items;
        _watcher = watcher;
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

    Type IStyleable.StyleKey => typeof(TreeViewItem);

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
                _items.Add(new DirectoryTreeItem(item, _watcher));
            }
        }

        // 全てのファイル追加
        foreach (FileInfo item in Info.GetFiles())
        {
            if (!item.Attributes.HasAnyFlag(FileAttributes.Hidden | FileAttributes.System))
            {
                _items.Add(new FileTreeItem(item));
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

        FileTreeItem[] fileArray = _items.OfType<FileTreeItem>().OrderBy(Func).ToArray();
        DirectoryTreeItem[] dirArray = _items.OfType<DirectoryTreeItem>().OrderBy(Func).ToArray();
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
                string content = (string)Application.Current.FindResource("S.DirectoryTreeView.CannotRenameBecauseNewNameConflicts");
                content = string.Format(content, Info.Name, tb.Text);
                var dialog = new ContentDialog()
                {
                    [!ContentDialog.CloseButtonTextProperty] = new DynamicResourceExtension("CloseString"),
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

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);

        _watcher.Renamed += Watcher_Renamed;
        _watcher.Deleted += Watcher_Deleted;
        _watcher.Created += Watcher_Created;
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
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
                    TreeViewItem last = _items.LastOrDefault(i => i is DirectoryTreeItem);
                    if (last != null)
                    {
                        int index = _items.IndexOf(last);
                        _items.Insert(index, new DirectoryTreeItem(di, _watcher));
                    }
                    else
                    {
                        _items.Add(new DirectoryTreeItem(di, _watcher));
                    }
                }
                else
                {
                    _items.Add(new FileTreeItem(new FileInfo(e.FullPath)));
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
            }

            Sort();
        });
    }
}
