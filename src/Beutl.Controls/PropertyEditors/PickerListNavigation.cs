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

    // Left and Right move to the previous and next tile, Up and Down a row of `columns` tiles.
    public static void MoveSelectionInGrid(ListBox listBox, KeyEventArgs e, int columns)
    {
        int step = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -columns,
            Key.Down => columns,
            _ => 0,
        };
        if (step == 0 || listBox.ItemCount == 0) return;

        int current = listBox.SelectedIndex;
        int next = current < 0 ? 0 : current + step;
        // A row up from the first row, or down from the last, stays where it is.
        if (next < 0 || next >= listBox.ItemCount)
            next = Math.Abs(step) > 1 ? Math.Max(current, 0) : Math.Clamp(next, 0, listBox.ItemCount - 1);

        listBox.SelectedIndex = next;
        ScrollSelectedIntoView(listBox);
        e.Handled = true;
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
