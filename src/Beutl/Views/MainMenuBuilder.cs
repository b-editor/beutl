using System.Collections.Specialized;
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
            Follow(items, menuItem.Items.Clear, child => menuItem.Items.Add(CreateMenuItem(child, disposables)), disposables);

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
            Follow(items, menu.Items.Clear, child =>
            {
                if (CreateNativeMenuItem(child, disposables) is { } created)
                    menu.Items.Add(created);
            }, disposables);
        }

        return menuItem;
    }

    private static KeyGesture? GetGesture(MainMenuItem item) => item.GestureCommand is { } name
        ? GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), name)
        : null;

    private static void Follow(
        IReadOnlyList<MainMenuItem> items, Action clear, Action<MainMenuItem> add, CompositeDisposable disposables)
    {
        void Fill()
        {
            clear();
            foreach (MainMenuItem item in items)
                add(item);
        }

        Fill();
        if (items is INotifyCollectionChanged changes)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => Fill();
            changes.CollectionChanged += handler;
            disposables.Add(Disposable.Create(() => changes.CollectionChanged -= handler));
        }
    }
}
