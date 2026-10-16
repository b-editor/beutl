using System.Windows.Input;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;

namespace Beutl.ViewModels;

// An entry of the main menu. MainView shows the entries as a Menu and MacWindow as a NativeMenu.
public sealed class MainMenuItem(string? header)
{
    public static MainMenuItem Separator { get; } = new(null);

    // Null for a separator.
    public string? Header { get; } = header;

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    // The MainViewExtension command whose key gesture the item shows.
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
}
