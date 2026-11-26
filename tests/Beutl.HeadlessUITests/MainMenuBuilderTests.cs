using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Editor.Components.DockLayoutTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Services.PrimitiveImpls;
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
                Assert.That(Describe(menu), Does.Contain((Strings.Settings, (ICommand)menuBar.ShowSettings)));
                Assert.That(Describe(nativeMenu), Does.Not.Contain((Strings.Settings, (ICommand)menuBar.ShowSettings)));
                Assert.That(Describe(nativeMenu), Does.Contain(("recent.scene", (ICommand)menuBar.OpenRecentFile)));
                foreach (ICommand command in new ICommand[] { menuBar.SwitchEditor, menuBar.OpenToolTab, menuBar.OpenToolWindow })
                    Assert.That(Describe(nativeMenu).Select(entry => entry.Command), Does.Contain(command), "Extension entries");
            });

            menuBar.RecentFileItems.Remove("recent.scene");
            AssertMenus();
            view.DataContext = macWindow.DataContext = null;
            Assert.That((menu.Count, nativeMenu.Count), Is.EqualTo((
                menuCount - viewModel.MenuItems.Count(item => !item.IsInNativeMenuOnly),
                nativeCount - viewModel.MenuItems.Count(item => !item.IsInWindowOnly))));
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
            Assert.That(Describe(menu), Is.EqualTo(Describe(viewModel.MenuItems, true)));
            Assert.That(Describe(nativeMenu), Is.EqualTo(Describe(viewModel.MenuItems, false)));
        });
    }

    [AvaloniaTest]
    public async Task Tool_windows_registered_after_the_menus_appear_under_View_and_follow_removal()
    {
        await TestReset.ResetShellAsync();
        MainViewModel viewModel = TestShell.MainViewModel;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        ItemCollection inWindow = view.FindControl<Menu>("MenuBar")!.Items;
        IList<NativeMenuItemBase> native = NativeMenu.GetMenu(macWindow)!.Items;
        var extension = new SampleToolWindowExtension();
        const int packageId = -4633;
        try
        {
            viewModel.ExtensionProvider.AddExtensions(packageId, [extension]);
            HeadlessTestHelpers.Settle();
            MenuItem inWindowList = inWindow.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.View))
                .Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.ToolWindows));
            NativeMenu nativeList = native.OfType<NativeMenuItem>().Single(item => item.Header == Strings.View)
                .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.ToolWindows).Menu!;

            Assert.Multiple(() =>
            {
                Assert.That(inWindow.OfType<MenuItem>().Select(item => item.Header), Does.Not.Contain(Strings.Tools));
                Assert.That(native.OfType<NativeMenuItem>().Select(item => item.Header), Does.Not.Contain(Strings.Tools));
                Assert.That(inWindowList.Items.OfType<MenuItem>().Select(item => item.CommandParameter),
                    Is.EqualTo(viewModel.ToolWindowExtensions).And.Contain(extension));
                Assert.That(nativeList.Items.OfType<NativeMenuItem>().Select(item => item.CommandParameter),
                    Is.EqualTo(viewModel.ToolWindowExtensions).And.Contain(extension));
            });

            viewModel.ExtensionProvider.RemoveExtensions(packageId);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(inWindowList.Items.OfType<MenuItem>().Select(item => item.CommandParameter), Does.Not.Contain(extension));
                Assert.That(nativeList.Items.OfType<NativeMenuItem>().Select(item => item.CommandParameter), Does.Not.Contain(extension));
            });
        }
        finally
        {
            viewModel.ExtensionProvider.RemoveExtensions(packageId);
            macWindow.DataContext = view.DataContext = null;
            macWindow.Close();
        }
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

    // DockLayoutPresetService is app-wide, so a disposed view model must stop following it.
    [AvaloniaTest]
    public async Task A_disposed_view_model_stops_following_the_dock_layout_presets()
    {
        var viewModel = new MainViewModel(TestShell.MainViewModel.AppServices, TestShell.MainViewModel.SessionId);
        DockLayoutPresetService presets = DockLayoutPresetService.Instance;
        IReadOnlyList<MainMenuItem> entries = viewModel.MenuItems.Single(item => item.Header == Strings.View).Items!
            .Single(item => item.Header == Strings.ApplyDockLayout).Items!;
        DockLayoutPresetItem before = presets.Save("Before disposal", new JsonObject())!;
        DockLayoutPresetItem? after = null;
        try
        {
            HeadlessTestHelpers.Settle();
            Assert.That(entries.Select(entry => entry.Header), Does.Contain("Before disposal"));

            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            after = presets.Save("After disposal", new JsonObject());
            HeadlessTestHelpers.Settle();
            Assert.That(entries.Select(entry => entry.Header), Does.Not.Contain("After disposal"));
        }
        finally
        {
            presets.Remove(before);
            if (after is not null)
                presets.Remove(after);
        }
    }

    [AvaloniaTest]
    public void Every_menu_shortcut_names_a_registered_command()
    {
        MainViewModel viewModel = TestShell.MainViewModel;
        var shortcuts = Entries(viewModel.MenuItems).Where(item => item.GestureCommand is not null)
            .GroupBy(item => item.InWindowGestureExtensionType ?? typeof(MainViewExtension));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shortcuts.Select(group => group.Key),
                Is.EquivalentTo(new[] { typeof(MainViewExtension), typeof(SceneEditorExtension) }));
            foreach (var group in shortcuts)
            {
                string[] commands = [.. group.Select(item => item.GestureCommand!).Distinct()];
                ContextCommandEntry[] definitions = [.. viewModel.ContextCommandManager.GetDefinitions(group.Key)];
                Assert.That(commands, Is.SubsetOf(definitions.Select(entry => entry.Definition.Id)));
                Assert.That(
                    definitions.Where(entry => commands.Contains(entry.Definition.Id)
                        && !entry.KeyGestures.Any(gesture => gesture.KeyGesture is not null
                            && (group.Key != typeof(MainViewExtension) || gesture.Platform == OSPlatform.OSX)))
                        .Select(entry => entry.Definition.Id),
                    Is.Empty);
            }
        }
    }

    [AvaloniaTest]
    public async Task Scene_marker_and_playback_entries_run_the_selected_scene_commands()
    {
        await TestReset.ResetShellAsync();
        MainViewModel viewModel = TestShell.MainViewModel;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        try
        {
            MenuItem scene = view.FindControl<Menu>("MenuBar")!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, Strings.Scene));
            NativeMenu nativeScene = NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == Strings.Scene).Menu!;
            string[] groups = [Strings.Markers, Strings.Playback];
            MenuItem[] entries = [.. groups.SelectMany(group => scene.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, group)).Items.OfType<MenuItem>())];
            NativeMenuItem[] nativeEntries = [.. groups.SelectMany(group => nativeScene.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == group).Menu!.Items.OfType<NativeMenuItem>())
                .Where(item => item is not NativeMenuItemSeparator)];
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(entries.Select(item => item.Command!.CanExecute(null)), Is.All.False);
                Assert.That(nativeEntries.Select(item => item.Command!.CanExecute(null)), Is.All.False);
                Assert.That(entries.Select(item => item.InputGesture), Is.All.Not.Null);
                Assert.That(nativeEntries.Select(item => item.Gesture), Is.All.Null);
            });

            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("scene-menu-commands", reset: false);
            ICommand toggleMarker = entries.Single(item => Equals(item.Header, Strings.ToggleMarker)).Command!;
            ICommand toggleLoop = nativeEntries.Single(item => item.Header == Strings.ToggleLoop).Command!;
            await MenuBarViewModel.ExecuteCommandAsync(toggleMarker);
            await MenuBarViewModel.ExecuteCommandAsync(toggleLoop);
            Assert.Multiple(() =>
            {
                Assert.That(editor.Scene.Markers, Has.Count.EqualTo(1));
                Assert.That(editor.Player.IsLoopEnabled.Value, Is.True);
            });
        }
        finally
        {
            macWindow.DataContext = view.DataContext = null;
            macWindow.Close();
        }
    }

    [AvaloniaTest]
    public async Task Menus_expose_the_expected_edit_and_window_commands()
    {
        await TestReset.ResetShellAsync();
        MainViewModel viewModel = TestShell.MainViewModel;
        MenuBarViewModel menuBar = viewModel.MenuBar;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        try
        {
            HeadlessTestHelpers.Settle();
            IList<NativeMenuItemBase> native = NativeMenu.GetMenu(macWindow)!.Items;
            ItemCollection inWindow = view.FindControl<Menu>("MenuBar")!.Items;

            Assert.Multiple(() =>
            {
                foreach ((string header, ICommand command, StandardEditAction action) in new (string, ICommand, StandardEditAction)[]
                {
                    (Strings.Cut, menuBar.CutLayer, StandardEditAction.Cut),
                    (Strings.Copy, menuBar.CopyLayer, StandardEditAction.Copy),
                    (Strings.Paste, menuBar.PasteLayer, StandardEditAction.Paste),
                    (Strings.SelectAll, menuBar.SelectAll, StandardEditAction.SelectAll),
                })
                {
                    NativeMenuItem entry = NativeEntry(native, Strings.Edit, header);
                    Assert.That(((FocusedEditCommand)entry.Command!).Fallback, Is.SameAs(command));
                    Assert.That(entry.Gesture, Is.EqualTo(FocusedEditCommand.GetKeyGesture(action)));
                }

                Assert.That(((FocusedEditCommand)NativeEntry(native, Strings.Edit, Strings.Undo).Command!).Fallback, Is.SameAs(menuBar.Undo));
                Assert.That(((FocusedEditCommand)NativeEntry(native, Strings.Edit, Strings.Redo).Command!).Fallback, Is.SameAs(menuBar.Redo));
                Assert.That(NativeEntry(native, Strings.View, Strings.ToggleFullScreen).Command, Is.SameAs(menuBar.ToggleFullScreen));
                Assert.That(NativeEntry(native, Strings.Window, Strings.MinimizeWindow).Command, Is.SameAs(menuBar.MinimizeWindow));
                Assert.That(NativeEntry(native, Strings.Window, Strings.ZoomWindow).Command, Is.SameAs(menuBar.ZoomWindow));
                Assert.That(NativeEntry(native, Strings.File, Strings.CloseTab).Command, Is.SameAs(menuBar.CloseTab));

                MenuItem[] tops = [.. inWindow.OfType<MenuItem>()];
                Assert.That(tops.Select(item => item.Header), Does.Not.Contain(Strings.Window));
                Assert.That(tops.Single(item => Equals(item.Header, Strings.Edit)).Items.OfType<MenuItem>().Select(item => item.Header),
                    Is.EqualTo(new[] { Strings.Undo, Strings.Redo }));
                Assert.That(tops.Single(item => Equals(item.Header, Strings.Edit)).Items.OfType<MenuItem>().First().Command, Is.SameAs(menuBar.Undo));
                MenuItem fullScreen = tops.Single(item => Equals(item.Header, Strings.View)).Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, Strings.ToggleFullScreen));
                Assert.That(fullScreen.Command, Is.SameAs(menuBar.ToggleFullScreen));

                MenuItem newScene = tops.Single(item => Equals(item.Header, Strings.Scene)).Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, Strings.CreateNew));
                NativeMenuItem nativeNewScene = NativeEntry(native, Strings.Scene, Strings.CreateNew);
                KeyGesture gesture = GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), "main.createNewFile")!;
                Assert.That(gesture, Is.Not.Null);
                Assert.That(newScene.Command, Is.SameAs(menuBar.NewScene));
                Assert.That(newScene.InputGesture, Is.EqualTo(gesture));
                Assert.That(nativeNewScene.Command, Is.SameAs(menuBar.NewScene));
                Assert.That(nativeNewScene.Gesture, Is.EqualTo(gesture));
            });
        }
        finally
        {
            macWindow.DataContext = null;
            macWindow.Close();
            view.DataContext = null;
        }

        static NativeMenuItem NativeEntry(IEnumerable<NativeMenuItemBase> menus, string top, string entry)
            => menus.OfType<NativeMenuItem>().Single(item => item.Header == top).Menu!.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == entry);
    }

    // The window's menu is built with what keeps an entry's state with the focus.
    [AvaloniaTest]
    public async Task An_edit_entry_follows_a_focused_text_box_when_no_scene_is_open()
    {
        await TestReset.ResetShellAsync();
        var macWindow = new MacWindow { DataContext = TestShell.MainViewModel };
        var main = (MainView)macWindow.Content!;
        var box = new TextBox();
        ((Grid)main.Content!).Children.Add(box);
        macWindow.Show();
        try
        {
            HeadlessTestHelpers.Render(2);
            NativeMenuItem copy = NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Edit)
                .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Copy);

            main.Focus();
            HeadlessTestHelpers.Settle();
            Assert.That(copy.IsEnabled, Is.False);

            box.Focus();
            HeadlessTestHelpers.Settle();
            Assert.That(copy.IsEnabled, Is.True);

            main.Focus();
            HeadlessTestHelpers.Settle();
            Assert.That(copy.IsEnabled, Is.False);
        }
        finally
        {
            macWindow.DataContext = null;
            macWindow.Close();
        }
    }

    // Both menus show the key the key map gives a command, when the commands are registered after the menus are built and
    // when the key map moves it, and an edit entry hands on the key it shows (#4053).
    [AvaloniaTest]
    public async Task Both_menus_follow_commands_registered_after_them_and_the_key_map()
    {
        await TestReset.ResetShellAsync();
        MainViewModel viewModel = TestShell.MainViewModel;
        Func<ContextCommandManager?>? previous = AppHelper.GetContextCommandManager;
        var commands = new ContextCommandManager(new ContextCommandSettingsStore(new JsonObject(), persist: false));
        AppHelper.GetContextCommandManager = () => commands;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        try
        {
            HeadlessTestHelpers.Settle();
            MenuItem undo = view.FindControl<Menu>("MenuBar")!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.Edit))
                .Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.Undo));
            NativeMenuItem nativeUndo = NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Edit)
                .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Undo);
            MenuItem marker = view.FindControl<Menu>("MenuBar")!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.Scene))
                .Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.Markers))
                .Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.ToggleMarker));
            NativeMenuItem nativeMarker = NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Scene)
                .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Markers)
                .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.ToggleMarker);
            Assert.That(nativeUndo.Gesture, Is.Null, "No command is registered yet.");
            Assert.That(marker.InputGesture, Is.Null, "No scene command is registered yet.");

            commands.Register(MainViewExtension.Instance);
            commands.Register(SceneEditorExtension.Instance);
            HeadlessTestHelpers.Settle();
            ContextCommandEntry entry = commands.GetDefinitions<MainViewExtension>().Single(command => command.Definition.Id == "main.undo");
            ContextCommandEntry markerEntry = commands.GetDefinitions<SceneEditorExtension>()
                .Single(command => command.Definition.Id == "editor.toggleMarker");
            KeyGesture? registered = ContextCommandManager.CurrentGestures(entry).First();
            Assert.Multiple(() =>
            {
                Assert.That(registered, Is.Not.Null);
                Assert.That(undo.InputGesture, Is.EqualTo(registered));
                Assert.That(nativeUndo.Gesture, Is.EqualTo(registered));
                Assert.That(marker.InputGesture, Is.EqualTo(ContextCommandManager.CurrentGestures(markerEntry).First()));
                Assert.That(nativeMarker.Gesture, Is.Null);
            });

            var moved = new KeyGesture(Key.U, KeyModifiers.Alt | KeyModifiers.Shift);
            commands.ChangeKeyGesture(entry, moved, ContextCommandManager.CurrentPlatform);
            var markerMoved = new KeyGesture(Key.M, KeyModifiers.Control | KeyModifiers.Shift);
            commands.ChangeKeyGesture(markerEntry, markerMoved, ContextCommandManager.CurrentPlatform);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(undo.InputGesture, Is.EqualTo(moved));
                Assert.That(nativeUndo.Gesture, Is.EqualTo(moved));
                Assert.That(((FocusedEditCommand)nativeUndo.Command!).ShownGesture, Is.EqualTo(moved));
                Assert.That(marker.InputGesture, Is.EqualTo(markerMoved));
                Assert.That(nativeMarker.Gesture, Is.Null, "AppKit must leave this key with the focused control.");
            });
        }
        finally
        {
            AppHelper.GetContextCommandManager = previous;
            macWindow.DataContext = null;
            macWindow.Close();
            view.DataContext = null;
        }
    }

    private static IEnumerable<MainMenuItem> Entries(IEnumerable<MainMenuItem> items)
        => items.SelectMany(item => Entries(item.Items ?? []).Prepend(item));

    // Entries in depth-first order.
    private static IEnumerable<(string? Header, ICommand? Command)> Describe(IEnumerable<MainMenuItem> items, bool inWindow)
        => items.Where(item => inWindow ? !item.IsInNativeMenuOnly : !item.IsInWindowOnly).SelectMany(item =>
            Describe(item.Items ?? [], inWindow).Prepend((item.Header, item.Command)));

    private static IEnumerable<(string? Header, ICommand? Command)> Describe(IEnumerable items)
        => items.Cast<object>().SelectMany(item => item switch
        {
            MenuItem m => Describe(m.Items).Prepend(((string?)m.Header, m.Command)),
            NativeMenuItemSeparator => [(null, null)],
            NativeMenuItem n => Describe(n.Menu?.Items ?? []).Prepend((n.Header, (n.Command as FocusedEditCommand)?.Fallback ?? n.Command)),
            _ => [(null, null)]
        });

    private sealed class SampleToolWindowExtension : ToolWindowExtension
    {
        public override string DisplayName => "Sample tool window";

        public override bool TryCreateContent([NotNullWhen(true)] out Window? window)
        {
            window = null;
            return false;
        }

        public override bool TryCreateContext([NotNullWhen(true)] out IToolWindowContext? context)
        {
            context = null;
            return false;
        }
    }
}
