using System.Runtime.CompilerServices;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Input;

namespace Beutl.Controls.NativeContextMenu;

internal enum ContextMenuEntryKind
{
    Item,
    SubMenu,
    Separator
}

// One menu item as a native menu sees it. Its values are read from the item on demand, so that a binding
// that changes while the native menu is open reaches it.
internal sealed class ContextMenuEntry
{
    private ContextMenuEntry(Control source, ContextMenuEntryKind kind, IReadOnlyList<ContextMenuEntry> children)
    {
        Source = source;
        Kind = kind;
        Children = children;
    }

    public Control Source { get; }

    public ContextMenuEntryKind Kind { get; }

    public IReadOnlyList<ContextMenuEntry> Children { get; }

    public bool IsVisible => Source.IsVisible;

    public bool IsEnabled => Source.IsEffectivelyEnabled;

    public string Title => Source switch
    {
        FAMenuFlyoutItem item => item.Text ?? string.Empty,
        FAMenuFlyoutSubItem item => item.Text ?? string.Empty,
        MenuItem item => GetHeaderText(item) ?? string.Empty,
        FACommandBarButton button => button.Label ?? string.Empty,
        FACommandBarToggleButton button => button.Label ?? string.Empty,
        _ => string.Empty
    };

    public bool IsChecked => Source switch
    {
        FAToggleMenuFlyoutItem item => item.IsChecked,
        FARadioMenuFlyoutItem item => item.IsChecked,
        MenuItem { ToggleType: not MenuItemToggleType.None } item => item.IsChecked,
        FACommandBarToggleButton button => button.IsChecked == true,
        _ => false
    };

    public KeyGesture? Gesture => Source switch
    {
        FAMenuFlyoutItem item => item.InputGesture,
        MenuItem item => item.InputGesture,
        Button button => GetGesture(button),
        _ => null
    };

    public FAIconSource? Icon => Source switch
    {
        FAMenuFlyoutItem item => item.IconSource,
        FAMenuFlyoutSubItem item => item.IconSource,
        FACommandBarButton button => button.IconSource,
        FACommandBarToggleButton button => button.IconSource,
        _ => null
    };

    public static bool IsTracked(AvaloniaProperty property) =>
        property == Visual.IsVisibleProperty
        || property == InputElement.IsEffectivelyEnabledProperty
        || property == FAMenuFlyoutItem.TextProperty
        || property == HeaderedSelectingItemsControl.HeaderProperty
        || property == FACommandBarButton.LabelProperty
        || property == FACommandBarToggleButton.LabelProperty
        || property == FAToggleMenuFlyoutItem.IsCheckedProperty
        || property == FARadioMenuFlyoutItem.IsCheckedProperty
        || property == MenuItem.IsCheckedProperty
        || property == ToggleButton.IsCheckedProperty;

    // Null when the menu holds something a native menu cannot show. It then stays an Avalonia menu.
    public static IReadOnlyList<ContextMenuEntry>? TryRead(ItemsControl presenter)
    {
        var entries = new ContextMenuEntry[presenter.ItemCount];
        for (int i = 0; i < entries.Length; i++)
        {
            if (presenter.ContainerFromIndex(i) is not { } container
                || TryRead(container) is not { } entry)
            {
                return null;
            }

            entries[i] = entry;
        }

        return entries;
    }

    public static IReadOnlyList<ContextMenuEntry>? TryRead(FACommandBarFlyout flyout)
    {
        var entries = new List<ContextMenuEntry>();
        foreach (IFACommandBarElement command in flyout.PrimaryCommands)
        {
            if (TryRead(command) is not { } entry) return null;
            entries.Add(entry);
        }

        if (entries.Count > 0 && flyout.SecondaryCommands.Count > 0)
            entries.Add(new ContextMenuEntry(new Separator(), ContextMenuEntryKind.Separator, []));

        foreach (IFACommandBarElement command in flyout.SecondaryCommands)
        {
            if (TryRead(command) is not { } entry) return null;
            entries.Add(entry);
        }

        return entries;
    }

    public void Invoke()
    {
        switch (Source)
        {
            case FAMenuFlyoutItem item:
                RaiseClick(item);
                break;

            // What a click on the item does in an Avalonia menu.
            case MenuItem item:
                if (item.ToggleType == MenuItemToggleType.CheckBox)
                    item.SetCurrentValue(MenuItem.IsCheckedProperty, !item.IsChecked);
                else if (item.ToggleType == MenuItemToggleType.Radio)
                    item.SetCurrentValue(MenuItem.IsCheckedProperty, true);

                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                break;

            case Button button:
                ClickButton(button);
                break;
        }
    }

    private static ContextMenuEntry? TryRead(IFACommandBarElement command) => command switch
    {
        FACommandBarSeparator separator => new ContextMenuEntry(separator, ContextMenuEntryKind.Separator, []),
        FACommandBarButton { Flyout: null } button => new ContextMenuEntry(button, ContextMenuEntryKind.Item, []),
        FACommandBarToggleButton button => new ContextMenuEntry(button, ContextMenuEntryKind.Item, []),
        _ => null
    };

    private static ContextMenuEntry? TryRead(Control control)
    {
        switch (control)
        {
            case FAMenuFlyoutSeparator or Separator or MenuItem { Header: "-" }:
                return new ContextMenuEntry(control, ContextMenuEntryKind.Separator, []);

            case FAMenuFlyoutSubItem subItem:
                return TryReadSubMenu(subItem);

            case FAMenuFlyoutItem:
                return new ContextMenuEntry(control, ContextMenuEntryKind.Item, []);

            case MenuItem item when item.Header is null or string or TextBlock:
                return item.ItemCount > 0
                    ? TryReadSubMenu(item)
                    : new ContextMenuEntry(control, ContextMenuEntryKind.Item, []);

            default:
                return null;
        }
    }

    // A sub menu makes its presenter when it first opens, and the presenter its containers when it is laid out. Only then
    // do the items have a logical parent, and so their data context. The panel alone is enough for that, and off the
    // screen a measure stops short of making it.
    private static ContextMenuEntry? TryReadSubMenu(FAMenuFlyoutSubItem subItem)
    {
        InitPopup(subItem);
        if (subItem.GetLogicalChildren().OfType<Popup>().FirstOrDefault()?.Child is not ItemsControl presenter)
            return null;

        presenter.Measure(Size.Infinity);
        presenter.Presenter?.ApplyTemplate();
        return TryRead(presenter) is { } children
            ? new ContextMenuEntry(subItem, ContextMenuEntryKind.SubMenu, children)
            : null;
    }

    private static ContextMenuEntry? TryReadSubMenu(MenuItem item)
    {
        var children = new List<ContextMenuEntry>();
        foreach (object? child in item.Items)
        {
            // Only an open sub menu turns data into containers.
            if (child is not Control control || TryRead(control) is not { } entry)
                return null;

            children.Add(entry);
        }

        return new ContextMenuEntry(item, ContextMenuEntryKind.SubMenu, children);
    }

    // FluentAvalonia's standard commands name Ctrl on every platform.
    private static KeyGesture? GetGesture(Button button) =>
        button is { Command: FAStandardUICommand, HotKey: { } gesture } && gesture.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? new KeyGesture(gesture.Key, (gesture.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta)
            : button.HotKey;

    private static string? GetHeaderText(MenuItem item) => item.Header switch
    {
        string text => RemoveAccessKeys(text),
        TextBlock block => block.Text,
        _ => null
    };

    // A menu item's header marks its access key with an underscore, and a doubled one stands for itself.
    private static string RemoveAccessKeys(string text)
    {
        if (!text.Contains('_')) return text;

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '_' && i + 1 < text.Length) i++;
            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RaiseClick")]
    private static extern void RaiseClick(FAMenuFlyoutItem item);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "InitPopup")]
    private static extern void InitPopup(FAMenuFlyoutSubItem item);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "OnClick")]
    private static extern void ClickButton(Button button);
}
