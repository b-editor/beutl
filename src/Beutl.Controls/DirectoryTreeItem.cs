using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
        DirectoryTreeNodes.AddChildren(_items, Info, _watcher, _contextFactory);

        _isAdd = true;
    }

    public void Sort()
    {
        if (!_isAdd)
            return;

        DirectoryTreeNodes.SortNodes(_items);
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
            DirectoryTreeRename.BeginEdit(this, Info.Name, TextBox_KeyUp, TextBox_LostFocus);
        }
    }

    private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        EndRename();
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
                FAContentDialog dialog = DirectoryTreeRename.CreateConflictDialog(Info.Name, tb.Text);
                await dialog.ShowAsync();
            }
            else if (isDifferentPath)
            {
                Directory.Move(old, @new);
                _info = new DirectoryInfo(@new);
            }


            DirectoryTreeRename.EndEdit(tb, TextBox_KeyUp, TextBox_LostFocus);
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
                _items.Add(DirectoryTreeNodes.CreateNode(e.FullPath, _watcher, _contextFactory));

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
                DirectoryTreeNodes.RemoveNode(_items, filename);
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

            if (parent == Info.FullName)
            {
                DirectoryTreeNodes.RenameNode(_items, oldFilename, e.FullPath, _contextFactory);
            }

            Sort();
        });
    }
}
