using Avalonia.Controls;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

internal static class PickerListNavigation
{
    public static bool IsSearchShortcut(KeyEventArgs e)
    {
        return e is { Key: Key.F, KeyModifiers: KeyModifiers.Control or KeyModifiers.Meta };
    }

    public static bool IsTabSwitch(KeyEventArgs e)
    {
        return e is { Key: Key.Tab, KeyModifiers: KeyModifiers.None or KeyModifiers.Shift };
    }

    public static void MoveSelection(ListBox listBox, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                listBox.SelectedIndex = listBox.SelectedIndex <= 0
                    ? 0
                    : listBox.SelectedIndex - 1;
                ScrollSelectedIntoView(listBox);
                e.Handled = true;
                break;
            case Key.Down:
                listBox.SelectedIndex = listBox.SelectedIndex >= listBox.ItemCount - 1
                    ? listBox.ItemCount - 1
                    : listBox.SelectedIndex + 1;
                ScrollSelectedIntoView(listBox);
                e.Handled = true;
                break;
        }
    }

    public static void FocusList(ListBox? target)
    {
        if (target is null) return;
        if (target.SelectedIndex < 0 && target.ItemCount > 0)
            target.SelectedIndex = 0;

        target.Focus();
    }

    private static void ScrollSelectedIntoView(ListBox listBox)
    {
        if (listBox.SelectedItem is { } item)
        {
            listBox.ScrollIntoView(item);
        }
    }
}
