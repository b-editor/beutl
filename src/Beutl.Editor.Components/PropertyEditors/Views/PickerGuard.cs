using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// Opens one picker at a time per editor: the pickers do not light-dismiss, so another click would open a second
// one. An editor's type and target pickers share its entry. The button that asked for a picker closes it again, so
// the picker opens below the button; a menu item has nothing to click again, so its picker opens at the pointer.
// Any other request while a picker is open does nothing.
internal static class PickerGuard
{
    private sealed class Entry(Control? button)
    {
        public Control? Button { get; } = button;

        public PopupFlyoutBase? Picker { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, Entry> s_open = new();

    public static async Task<T?> RunAsync<T>(Control owner, Control? button, Func<Task<T?>> pick)
    {
        if (!TryBegin(owner, button)) return default;

        try
        {
            return await pick();
        }
        finally
        {
            s_open.Remove(owner);
        }
    }

    public static async Task RunAsync(Control owner, Control? button, Func<Task> pick)
    {
        if (!TryBegin(owner, button)) return;

        try
        {
            await pick();
        }
        finally
        {
            s_open.Remove(owner);
        }
    }

    // Shows the picker that owner's current request made.
    public static void Show(Control owner, PopupFlyoutBase picker)
    {
        if (s_open.TryGetValue(owner, out Entry? entry) && entry.Button is { } button)
        {
            entry.Picker = picker;
            picker.Placement = PlacementMode.BottomEdgeAlignedRight;
            picker.ShowAt(button);
        }
        else
        {
            picker.ShowAt(owner, true);
        }
    }

    private static bool TryBegin(Control owner, Control? button)
    {
        if (s_open.TryAdd(owner, new Entry(button))) return true;

        if (button != null && s_open.TryGetValue(owner, out Entry? open) && open.Button == button)
        {
            open.Picker?.Hide();
        }

        return false;
    }
}
