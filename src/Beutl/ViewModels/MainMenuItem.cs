using System.Windows.Input;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;

namespace Beutl.ViewModels;

// An action that the Edit menu of a macOS app sends to whatever has the keyboard focus.
public enum StandardEditAction
{
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    SelectAll
}

// An entry of the main menu. MainView shows the entries as a Menu and MacWindow as a NativeMenu.
public sealed class MainMenuItem(string? header)
{
    public static MainMenuItem Separator { get; } = new(null);

    // Null for a separator.
    public string? Header { get; } = header;

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    // The id of the MainViewExtension command whose key gesture the item shows.
    public string? GestureCommand { get; init; }

    // The native menu shows no icons.
    public Icon? Icon { get; init; }

    // An extension's own icon, used when Icon is null.
    public FAIconSource? IconSource { get; init; }

    public IObservable<bool>? IsVisible { get; init; }

    // A list that raises CollectionChanged is followed.
    public IReadOnlyList<MainMenuItem>? Items { get; init; }

    // macOS has it in the application menu instead.
    public bool IsInWindowOnly { get; init; }

    // What macOS users expect of a menu bar. On the other platforms, the title bar or the panels have it.
    public bool IsInNativeMenuOnly { get; init; }

    // In the native menu, the focused control gets this action first, and Command does what no control took.
    public StandardEditAction? EditAction { get; init; }
}
