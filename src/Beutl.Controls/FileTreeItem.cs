using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;

using Beutl.Language;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

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
            string @new = Path.Combine(Info.DirectoryName ?? throw new InvalidOperationException("The file has no parent directory."), tb.Text ?? Info.Name);
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
                File.Move(old, @new);
                _info = new FileInfo(@new);
            }


            tb.RemoveHandler(KeyUpEvent, TextBox_KeyUp);
            tb.TemplateApplied -= TextBox_TemplateApplied;
            tb.LostFocus -= TextBox_LostFocus;
            Header = _info.Name;
        }
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TopLevel.GetTopLevel(this) is not { StorageProvider: IStorageProvider storageProvider })
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {

            TreeView? parent = this.FindLogicalAncestorOfType<TreeView>();
            if (parent != null)
                parent.SelectedItem = this;
            Refresh();

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(await storageProvider.TryGetFileFromPathAsync(Info.FullName)));

            // ドラッグ開始
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy).ConfigureAwait(false);
        }
    }

    private void FileTreeItem_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        Refresh();
        Process.Start(new ProcessStartInfo(Info.FullName)
        {
            UseShellExecute = true,
        });
    }
}
