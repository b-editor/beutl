using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace Beutl.Views.Editors;

// Opens one picker at a time per editor: the pickers do not light-dismiss, so another click would open a
// second one. An editor's type and target pickers share its entry.
internal static class PickerGuard
{
    private static readonly ConditionalWeakTable<Control, object> s_open = new();
    private static readonly object s_entry = new();

    public static async Task<T?> RunAsync<T>(Control owner, Func<Task<T?>> pick)
    {
        if (!s_open.TryAdd(owner, s_entry)) return default;

        try
        {
            return await pick();
        }
        finally
        {
            s_open.Remove(owner);
        }
    }

    public static async Task RunAsync(Control owner, Func<Task> pick)
    {
        if (!s_open.TryAdd(owner, s_entry)) return;

        try
        {
            await pick();
        }
        finally
        {
            s_open.Remove(owner);
        }
    }
}
