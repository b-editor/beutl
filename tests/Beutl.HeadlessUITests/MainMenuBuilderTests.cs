using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Language;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class MainMenuBuilderTests
{
    [AvaloniaTest]
    public async Task Both_menus_show_the_menu_items_and_follow_the_recent_files()
    {
        await TestReset.ResetShellAsync();
        MainViewModel viewModel = TestShell.MainViewModel;
        MenuBarViewModel menuBar = viewModel.MenuBar;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        ItemCollection menu = view.FindControl<Menu>("MenuBar")!.Items;
        IList<NativeMenuItemBase> nativeMenu = NativeMenu.GetMenu(macWindow)!.Items;
        try
        {
            menuBar.RecentFileItems.Add("recent.scene");
            AssertMenus();
            (int menuCount, int nativeCount) = (menu.Count, nativeMenu.Count);
            Assert.Multiple(() =>
            {
                Assert.That(Describe(menu, menuBar), Does.Contain($"{Strings.Settings}:ShowSettings"));
                Assert.That(Describe(nativeMenu, menuBar), Does.Not.Contain($"{Strings.Settings}:ShowSettings"));
                Assert.That(Describe(nativeMenu, menuBar), Does.Contain("recent.scene:OpenRecentFile"));
            });

            menuBar.RecentFileItems.Remove("recent.scene");
            view.DataContext = macWindow.DataContext = null;
            int removed = menuBar.MenuItems.Count;
            Assert.That((menu.Count, nativeMenu.Count), Is.EqualTo((menuCount - removed, nativeCount - removed)));
            view.DataContext = macWindow.DataContext = viewModel;
            AssertMenus();
            Assert.That((menu.Count, nativeMenu.Count), Is.EqualTo((menuCount, nativeCount)));
        }
        finally
        {
            menuBar.RecentFileItems.Remove("recent.scene");
            macWindow.DataContext = null;
            macWindow.Close();
            view.DataContext = null;
        }

        void AssertMenus() => Assert.Multiple(() =>
        {
            Assert.That(Describe(menu, menuBar), Is.EqualTo(Describe(menuBar.MenuItems, true, menuBar)));
            Assert.That(Describe(nativeMenu, menuBar), Is.EqualTo(Describe(menuBar.MenuItems, false, menuBar)));
        });
    }

    // "Header:Command" per entry, depth first.
    private static IEnumerable<string> Describe(IEnumerable<MainMenuItem> items, bool inWindow, MenuBarViewModel menuBar)
        => items.Where(item => inWindow || !item.IsInWindowOnly).SelectMany(item =>
            Describe(item.Items ?? [], inWindow, menuBar).Prepend($"{item.Header ?? "-"}:{CommandName(item.Command, menuBar)}"));

    // The same for the built menus, whose roots after the model's are still declared in XAML.
    private static IEnumerable<string> Describe(IEnumerable items, MenuBarViewModel menuBar, bool roots = true)
        => items.Cast<object>().Take(roots ? menuBar.MenuItems.Count : int.MaxValue).SelectMany(item => item switch
        {
            MenuItem m => Describe(m.Items, menuBar, false).Prepend($"{m.Header}:{CommandName(m.Command, menuBar)}"),
            NativeMenuItem n => Describe(n.Menu?.Items ?? [], menuBar, false).Prepend($"{n.Header}:{CommandName(n.Command, menuBar)}"),
            _ => ["-:"]
        });

    private static string CommandName(object? command, MenuBarViewModel menuBar)
        => command is null ? "" : typeof(MenuBarViewModel).GetProperties().First(p => ReferenceEquals(p.GetValue(menuBar), command)).Name;
}
