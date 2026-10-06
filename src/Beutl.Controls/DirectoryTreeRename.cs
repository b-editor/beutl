using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Language;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

internal static class DirectoryTreeRename
{
    public static bool HasDistinctDestination(string source, string destination)
    {
        if (!File.Exists(destination) && !Directory.Exists(destination))
            return false;

        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            return true;

        string? parent = Path.GetDirectoryName(destination);
        if (parent is null)
            return false;

        string targetName = Path.GetFileName(destination);
        return Directory.EnumerateFileSystemEntries(parent)
            .Any(entry => string.Equals(Path.GetFileName(entry), targetName, StringComparison.Ordinal));
    }

    public static void BeginEdit(
        TreeViewItem item,
        string name,
        EventHandler<KeyEventArgs> keyUp,
        EventHandler<FocusChangedEventArgs> lostFocus)
    {
        TextBox tb;
        item.Header = tb = new TextBox
        {
            Text = name
        };

        tb.SelectAll();
        tb.AddHandler(InputElement.KeyUpEvent, keyUp, RoutingStrategies.Tunnel);
        tb.TemplateApplied += FocusEditor;
        tb.LostFocus += lostFocus;
    }

    public static void EndEdit(TextBox tb, EventHandler<KeyEventArgs> keyUp, EventHandler<FocusChangedEventArgs> lostFocus)
    {
        tb.RemoveHandler(InputElement.KeyUpEvent, keyUp);
        tb.TemplateApplied -= FocusEditor;
        tb.LostFocus -= lostFocus;
    }

    public static FAContentDialog CreateConflictDialog(string name, string? newName)
    {
        string content = MessageStrings.RenameConflict;
        content = string.Format(content, name, newName);
        return new FAContentDialog()
        {
            CloseButtonText = Strings.Close,
            Content = content,
            DefaultButton = FAContentDialogButton.None,
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
        };
    }

    private static void FocusEditor(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.Focus();
    }
}
