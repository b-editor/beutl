using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.Language;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

public sealed class DirectoryTreeItem : TreeViewItem
{
    private readonly AvaloniaList<TreeViewItem> _items = [];
    private readonly FileSystemWatcher _watcher;
    private readonly Func<string, object>? _contextFactory;
    // //サブフォルダを作成済みかどうか
    private bool _isAdd;
    private DirectoryInfo _info;
    // 名前を変更中
    private bool _isRenaming;

    public DirectoryTreeItem(DirectoryInfo info, FileSystemWatcher watcher, Func<string, object>? contextFactory = null)
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
                return tb.Text ?? string.Empty;
            }
            else
            {
                return item.Header?.ToString() ?? string.Empty;
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

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        EndRename();
    }

    private void TextBox_TemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.Focus();
    }

    private void TextBox_KeyUp(object? sender, KeyEventArgs e)
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
            string @new = Path.Combine(Info.Parent?.FullName ?? throw new InvalidOperationException("The directory has no parent."), tb.Text ?? Info.Name);
            bool isDifferentPath = !string.Equals(old, @new, StringComparison.Ordinal);
            if (isDifferentPath && DirectoryTreeRename.HasDistinctDestination(old, @new))
            {
                string content = MessageStrings.RenameConflict;
                content = string.Format(content, Info.Name, tb.Text);
                var dialog = new FAContentDialog()
                {
                    CloseButtonText = Strings.Close,
                    Content = content,
                    DefaultButton = FAContentDialogButton.None,
                    IsPrimaryButtonEnabled = false,
                    IsSecondaryButtonEnabled = false,
                };

                await dialog.ShowAsync();
            }
            else if (isDifferentPath)
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
            string? parent = Path.GetDirectoryName(e.FullPath);

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
            string? parent = Path.GetDirectoryName(e.FullPath);
            string? filename = Path.GetFileName(e.Name);

            if (parent == Info.FullName)
            {
                TreeViewItem? item = _items.FirstOrDefault(i => i.Header is string str && str == filename);
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
            string? parent = Path.GetDirectoryName(e.FullPath);
            string? oldFilename = Path.GetFileName(e.OldName);
            string? newFilename = Path.GetFileName(e.Name);

            if (parent == Info.FullName)
            {
                TreeViewItem? item = _items.FirstOrDefault(i => i.Header is string str && str == oldFilename);
                if (item is DirectoryTreeItem dir)
                {
                    dir.Info = new DirectoryInfo(e.FullPath);
                }

                if (item is FileTreeItem file)
                {
                    file.Info = new FileInfo(e.FullPath);
                }

                if (item != null)
                    item.DataContext = _contextFactory?.Invoke(e.FullPath);
            }

            Sort();
        });
    }
}
