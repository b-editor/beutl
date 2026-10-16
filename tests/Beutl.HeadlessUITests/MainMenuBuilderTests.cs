using System.Collections;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
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
            HeadlessTestHelpers.Settle();
            menuBar.RecentFileItems.Add("recent.scene");
            MenuItem recent = RecentFileEntry("recent.scene");
            menuBar.RecentFileItems.Insert(0, "first.scene");
            AssertMenus();
            Assert.That(RecentFileEntry("recent.scene"), Is.SameAs(recent), "A change leaves the other entries in place.");
            (int menuCount, int nativeCount) = (menu.Count, nativeMenu.Count);
            Assert.Multiple(() =>
            {
                Assert.That(Describe(menu, viewModel), Does.Contain($"{Strings.Settings}:ShowSettings"));
                Assert.That(Describe(nativeMenu, viewModel), Does.Not.Contain($"{Strings.Settings}:ShowSettings"));
                Assert.That(Describe(nativeMenu, viewModel), Does.Contain("recent.scene:OpenRecentFile"));
                foreach (string command in new[] { "SwitchEditor", "OpenToolTab", "OpenToolWindow" })
                    Assert.That(Describe(nativeMenu, viewModel), Has.Some.EndsWith(":" + command), "Extension entries");
            });

            menuBar.RecentFileItems.Remove("recent.scene");
            AssertMenus();
            view.DataContext = macWindow.DataContext = null;
            int removed = viewModel.MenuItems.Count;
            Assert.That((menu.Count, nativeMenu.Count), Is.EqualTo((menuCount - removed, nativeCount - removed)));
            view.DataContext = macWindow.DataContext = viewModel;
            AssertMenus();
            Assert.That((menu.Count, nativeMenu.Count), Is.EqualTo((menuCount, nativeCount)));
        }
        finally
        {
            menuBar.RecentFileItems.Remove("recent.scene");
            menuBar.RecentFileItems.Remove("first.scene");
            macWindow.DataContext = null;
            macWindow.Close();
            view.DataContext = null;
        }

        MenuItem RecentFileEntry(string path) => ((MenuItem)menu[0]!).Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, Strings.RecentFiles)).Items.OfType<MenuItem>().Single(item => Equals(item.Header, path));

        void AssertMenus() => Assert.Multiple(() =>
        {
            Assert.That(Describe(menu, viewModel), Is.EqualTo(Describe(viewModel.MenuItems, true, viewModel)));
            Assert.That(Describe(nativeMenu, viewModel), Is.EqualTo(Describe(viewModel.MenuItems, false, viewModel)));
        });
    }

    [AvaloniaTest]
    public async Task A_renamed_dock_layout_preset_is_renamed_in_both_menus_whose_submenu_shows_only_with_presets()
    {
        await TestReset.ResetShellAsync();
        MainViewModel viewModel = TestShell.MainViewModel;
        DockLayoutPresetService presets = DockLayoutPresetService.Instance;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        DockLayoutPresetItem preset = presets.Save("Menu preset", new JsonObject())!;
        try
        {
            presets.Rename(preset, "Renamed preset");
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(InWindowPresets().Items.OfType<MenuItem>().Select(item => item.Header),
                    Does.Contain("Renamed preset").And.Not.Contain("Menu preset"));
                Assert.That(NativePresets().Menu!.Items.OfType<NativeMenuItem>().Select(item => item.Header),
                    Does.Contain("Renamed preset").And.Not.Contain("Menu preset"));
            });

            presets.Remove(preset);
            HeadlessTestHelpers.Settle();
            bool shown = presets.Items.Count > 0;
            Assert.That((InWindowPresets().IsVisible, NativePresets().IsVisible), Is.EqualTo((shown, shown)));
        }
        finally
        {
            presets.Remove(preset);
            macWindow.DataContext = null;
            macWindow.Close();
            view.DataContext = null;
        }

        MenuItem InWindowPresets() => view.FindControl<Menu>("MenuBar")!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, Strings.View)).Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, Strings.ApplyDockLayout));

        NativeMenuItem NativePresets() => NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>()
            .Single(item => item.Header == Strings.View).Menu!.Items.OfType<NativeMenuItem>()
            .Single(item => item.Header == Strings.ApplyDockLayout);
    }

    // "Header:Command" per entry, depth first.
    private static IEnumerable<string> Describe(IEnumerable<MainMenuItem> items, bool inWindow, MainViewModel viewModel)
        => items.Where(item => inWindow || !item.IsInWindowOnly).SelectMany(item =>
            Describe(item.Items ?? [], inWindow, viewModel).Prepend($"{item.Header ?? "-"}:{CommandName(item.Command, viewModel)}"));

    // The same for the built menus.
    private static IEnumerable<string> Describe(IEnumerable items, MainViewModel viewModel)
        => items.Cast<object>().SelectMany(item => item switch
        {
            MenuItem m => Describe(m.Items, viewModel).Prepend($"{m.Header}:{CommandName(m.Command, viewModel)}"),
            NativeMenuItem n => Describe(n.Menu?.Items ?? [], viewModel).Prepend($"{n.Header}:{CommandName(n.Command, viewModel)}"),
            _ => ["-:"]
        });

    private static string CommandName(object? command, MainViewModel viewModel)
        => command is null ? "" : typeof(MenuBarViewModel).GetProperties().First(p => ReferenceEquals(p.GetValue(viewModel.MenuBar), command)).Name;
}
