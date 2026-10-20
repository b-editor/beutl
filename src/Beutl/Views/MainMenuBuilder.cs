using System.Collections;
using System.Collections.Specialized;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Editor.Components.Helpers;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using FluentIcons.Avalonia.Fluent;

namespace Beutl.Views;

// Builds the in-window Menu and the macOS NativeMenu from the same MainMenuItem entries.
internal static class MainMenuBuilder
{
    // Puts the menus first and takes them out again when disposables is disposed.
    public static void AddMenus(Menu menu, IReadOnlyList<MainMenuItem> items, CompositeDisposable disposables)
    {
        Control[] menus = [.. items.Select(item => CreateMenuItem(item, disposables))];
        for (int i = 0; i < menus.Length; i++)
            menu.Items.Insert(i, menus[i]);

        disposables.Add(Disposable.Create(() => Array.ForEach(menus, item => menu.Items.Remove(item))));
    }

    public static void AddMenus(NativeMenu menu, IReadOnlyList<MainMenuItem> items, CompositeDisposable disposables)
    {
        NativeMenuItemBase[] menus = [.. items.Select(item => CreateNativeMenuItem(item, disposables)).OfType<NativeMenuItemBase>()];
        for (int i = 0; i < menus.Length; i++)
            menu.Items.Insert(i, menus[i]);

        disposables.Add(Disposable.Create(() => Array.ForEach(menus, item => menu.Items.Remove(item))));
    }

    private static Control CreateMenuItem(MainMenuItem item, CompositeDisposable disposables)
    {
        if (item.Header is null)
            return new Separator();

        var menuItem = new MenuItem
        {
            Header = item.Header,
            Command = item.Command,
            CommandParameter = item.CommandParameter,
            InputGesture = GetGesture(item),
            Icon = item.Icon is { } icon ? new FluentIcon { Icon = icon } : item.IconSource
        };
        if (item.IsVisible is { } isVisible)
            disposables.Add(isVisible.Subscribe(visible => menuItem.IsVisible = visible));
        if (item.Items is { } items)
        {
            // As in the former XAML, a followed list's entries join the logical tree only when its submenu opens.
            IList children = items is INotifyCollectionChanged
                ? (IList)(menuItem.ItemsSource = new AvaloniaList<Control>())
                : menuItem.Items;
            Follow<Control>(items, CreateMenuItem, children.Insert, children.RemoveAt, disposables);
        }

        return menuItem;
    }

    private static NativeMenuItemBase? CreateNativeMenuItem(MainMenuItem item, CompositeDisposable disposables)
    {
        if (item.IsInWindowOnly)
            return null;
        if (item.Header is null)
            return new NativeMenuItemSeparator();

        var menuItem = new NativeMenuItem
        {
            Header = item.Header,
            Command = item.Command,
            CommandParameter = item.CommandParameter,
            Gesture = GetGesture(item)
        };
        if (item.IsVisible is { } isVisible)
            disposables.Add(isVisible.Subscribe(visible => menuItem.IsVisible = visible));
        if (item.Items is { } items)
        {
            var menu = new NativeMenu();
            menuItem.Menu = menu;
            Follow(items, CreateNativeMenuItem, menu.Items.Insert, menu.Items.RemoveAt, disposables);
        }

        return menuItem;
    }

    private static KeyGesture? GetGesture(MainMenuItem item) => item.GestureCommand is { } id
        ? GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), id)
        : null;

    // Applies each change in place: rebuilding on every change made each open view recreate all its entries.
    private static void Follow<T>(
        IReadOnlyList<MainMenuItem> items, Func<MainMenuItem, CompositeDisposable, T?> create, Action<int, T> insert,
        Action<int> removeAt, CompositeDisposable disposables)
        where T : class
    {
        // The subscriptions of each shown entry, which end when the entry goes.
        var shown = new List<CompositeDisposable>();

        // create skips window-only entries, which only fixed lists have, so a change's index is also the target's.
        void Insert(int index, IEnumerable added)
        {
            foreach (MainMenuItem item in added)
            {
                var entry = new CompositeDisposable();
                if (create(item, entry) is { } created)
                {
                    insert(index, created);
                    shown.Insert(index++, entry);
                }
            }
        }

        void RemoveAt(int index)
        {
            removeAt(index);
            shown[index].Dispose();
            shown.RemoveAt(index);
        }

        Insert(0, items);
        disposables.Add(Disposable.Create(() => shown.ForEach(entry => entry.Dispose())));
        if (items is INotifyCollectionChanged changes)
        {
            NotifyCollectionChangedEventHandler handler = (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    while (shown.Count > 0)
                        RemoveAt(shown.Count - 1);
                    Insert(0, items);
                    return;
                }

                for (int i = 0; i < (e.OldItems?.Count ?? 0); i++)
                    RemoveAt(e.OldStartingIndex);
                if (e.NewItems is { } added)
                    Insert(e.NewStartingIndex, added);
            };
            changes.CollectionChanged += handler;
            disposables.Add(Disposable.Create(() => changes.CollectionChanged -= handler));
        }
    }
}
