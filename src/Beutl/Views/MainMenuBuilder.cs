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
            Icon = item.Icon is { } icon ? new FluentIcon { Icon = icon } : null
        };
        if (item.Items is { } items)
        {
            var children = new AvaloniaList<Control>();
            menuItem.ItemsSource = children;
            Follow(items, children, child => CreateMenuItem(child, disposables), disposables);
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
        if (item.Items is { } items)
        {
            var menu = new NativeMenu();
            menuItem.Menu = menu;
            Follow(items, menu.Items, child => CreateNativeMenuItem(child, disposables), disposables);
        }

        return menuItem;
    }

    private static KeyGesture? GetGesture(MainMenuItem item) => item.GestureCommand is { } name
        ? GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), name)
        : null;

    // Applies each change in place: rebuilding on every change made each open view recreate all its entries.
    private static void Follow<T>(
        IReadOnlyList<MainMenuItem> items, IList<T> target, Func<MainMenuItem, T?> create, CompositeDisposable disposables)
        where T : class
    {
        // Entries create skips (window-only) are only in fixed lists, so a change's index is also the target's.
        void Insert(int index, IEnumerable added)
        {
            foreach (MainMenuItem item in added)
            {
                if (create(item) is { } created)
                    target.Insert(index++, created);
            }
        }

        void Fill()
        {
            target.Clear();
            Insert(0, items);
        }

        Fill();
        if (items is INotifyCollectionChanged changes)
        {
            NotifyCollectionChangedEventHandler handler = (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    Fill();
                    return;
                }

                for (int i = 0; i < (e.OldItems?.Count ?? 0); i++)
                    target.RemoveAt(e.OldStartingIndex);
                if (e.NewItems is { } added)
                    Insert(e.NewStartingIndex, added);
            };
            changes.CollectionChanged += handler;
            disposables.Add(Disposable.Create(() => changes.CollectionChanged -= handler));
        }
    }
}
