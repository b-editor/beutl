using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Editor.Components.DockLayoutTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Reactive.Bindings;

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
    public async Task Tool_windows_appear_directly_in_View_and_follow_registration_and_removal()
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
            HeadlessTestHelpers.Settle();
            MenuItem inWindowView = inWindow.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.View));
            NativeMenu nativeView = native.OfType<NativeMenuItem>().Single(item => item.Header == Strings.View).Menu!;
            MenuItem[] originalInWindow = InWindowToolWindows();
            NativeMenuItem[] originalNative = NativeToolWindows();

            viewModel.ExtensionProvider.AddExtensions(packageId, [extension]);
            HeadlessTestHelpers.Settle();
            MenuItem[] inWindowEntries = InWindowToolWindows();
            NativeMenuItem[] nativeEntries = NativeToolWindows();

            Assert.Multiple(() =>
            {
                Assert.That(inWindow.OfType<MenuItem>().Select(item => item.Header), Does.Not.Contain(Strings.Tools));
                Assert.That(native.OfType<NativeMenuItem>().Select(item => item.Header), Does.Not.Contain(Strings.Tools));
                Assert.That(inWindowEntries.Select(item => item.CommandParameter),
                    Is.EqualTo(viewModel.ToolWindowExtensions).And.Contain(extension));
                Assert.That(nativeEntries.Select(item => item.CommandParameter),
                    Is.EqualTo(viewModel.ToolWindowExtensions).And.Contain(extension));
                Assert.That(inWindowView.Items[2], Is.TypeOf<Separator>());
                Assert.That(inWindowView.Items.Skip(3).Take(inWindowEntries.Length), Is.EqualTo(inWindowEntries));
                Assert.That(inWindowView.Items[3 + inWindowEntries.Length], Is.TypeOf<Separator>());
                Assert.That(nativeView.Items[2], Is.TypeOf<NativeMenuItemSeparator>());
                Assert.That(nativeView.Items.Skip(3).Take(nativeEntries.Length), Is.EqualTo(nativeEntries));
                Assert.That(nativeView.Items[3 + nativeEntries.Length], Is.TypeOf<NativeMenuItemSeparator>());
            });

            viewModel.ExtensionProvider.RemoveExtensions(packageId);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(InWindowToolWindows(), Is.EqualTo(originalInWindow));
                Assert.That(NativeToolWindows(), Is.EqualTo(originalNative));
            });

            MenuItem[] InWindowToolWindows() => [.. inWindowView.Items.OfType<MenuItem>()
                .Where(item => ReferenceEquals(item.Command, viewModel.MenuBar.OpenToolWindow))];

            NativeMenuItem[] NativeToolWindows() => [.. nativeView.Items.OfType<NativeMenuItem>()
                .Where(item => ReferenceEquals(item.Command, viewModel.MenuBar.OpenToolWindow))];
        }
        finally
        {
            viewModel.ExtensionProvider.RemoveExtensions(packageId);
            macWindow.DataContext = view.DataContext = null;
            macWindow.Close();
        }
    }

    [AvaloniaTest]
    public async Task ImportMediaIsEnabledInBothFileMenusWhenASceneIsOpen()
    {
        await TestReset.ResetShellAsync();
        MainViewModel viewModel = TestShell.MainViewModel;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        var window = new Window { Width = 1000, Height = 680 };
        try
        {
            window.Show();
            window.Content = view;
            Menu menu = view.FindControl<Menu>("MenuBar")!;
            menu.IsVisible = true;
            MenuItem file = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.File));
            MenuItem import = file.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.ImportMedia));
            NativeMenuItem nativeImport = NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == Strings.File).Menu!.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == Strings.ImportMedia);

            file.Open();
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(import.Command, Is.SameAs(viewModel.MenuBar.ImportMedia));
                Assert.That(nativeImport.Command, Is.SameAs(viewModel.MenuBar.ImportMedia));
                Assert.That((import.IsEffectivelyEnabled, nativeImport.IsEnabled), Is.EqualTo((false, false)));
            });

            file.Close();
            await TestShell.OpenNewSceneEditorAsync("file-menu-import-media");
            file.Open();
            HeadlessTestHelpers.Render(3);
            Assert.That((import.IsEffectivelyEnabled, nativeImport.IsEnabled), Is.EqualTo((true, true)));
            HeadlessCapture.SaveIfRequested(window, nameof(MainMenuBuilderTests), "file-menu-import-media");

            await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!, saveChanges: false);
            HeadlessTestHelpers.Settle();
            Assert.That((import.IsEffectivelyEnabled, nativeImport.IsEnabled), Is.EqualTo((false, false)));
        }
        finally
        {
            macWindow.DataContext = null;
            macWindow.Close();
            view.DataContext = null;
            window.Close();
            await TestReset.ResetShellAsync();
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
    public async Task Scene_menu_commands_are_disabled_while_the_editor_is_suspended()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("scene-menu-suspension");
        MainViewModel viewModel = TestShell.MainViewModel;
        var view = new MainView { DataContext = viewModel };
        var macWindow = new MacWindow { DataContext = viewModel };
        var window = new Window { Width = 1000, Height = 680 };
        try
        {
            window.Show();
            window.Content = view;
            Menu menu = view.FindControl<Menu>("MenuBar")!;
            menu.IsVisible = true;
            MenuItem scene = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.Scene));
            MenuItem[] sections = [.. scene.Items.OfType<MenuItem>()
                .Where(item => Equals(item.Header, Strings.Markers) || Equals(item.Header, Strings.Playback))];
            NativeMenu nativeScene = NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == Strings.Scene).Menu!;
            NativeMenuItem[] nativeEntries = [.. nativeScene.Items.OfType<NativeMenuItem>()
                .Where(item => item.Header == Strings.Markers || item.Header == Strings.Playback)
                .SelectMany(item => item.Menu!.Items.OfType<NativeMenuItem>())
                .Where(item => item is not NativeMenuItemSeparator)];
            var marker = (AsyncReactiveCommand)sections.SelectMany(item => item.Items.OfType<MenuItem>())
                .Single(item => Equals(item.Header, Strings.ToggleMarker)).Command!;
            NativeMenuItem nativeMarker = nativeEntries.Single(item => item.Header == Strings.ToggleMarker);

            HeadlessTestHelpers.Settle();
            Assert.That(nativeMarker.IsEnabled, Is.True);
            using (TestShell.Editor.SuspendEditors())
            {
                Assert.That(editor.IsEnabled.Value, Is.False);
                // Run the command directly to exercise the guard even if a menu still shows its old state.
                await marker.ExecuteAsync();
                HeadlessTestHelpers.Settle();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(editor.Scene.Markers, Is.Empty);
                    Assert.That(nativeEntries.Select(item => item.IsEnabled), Is.All.False);
                    scene.Open();
                    foreach (MenuItem section in sections)
                    {
                        section.Open();
                        HeadlessTestHelpers.Render();
                        Assert.That(section.Items.OfType<MenuItem>().Select(item => item.IsEffectivelyEnabled), Is.All.False);
                        section.Close();
                    }
                }
            }

            HeadlessTestHelpers.Settle();
            Assert.That(nativeMarker.IsEnabled, Is.True);
        }
        finally
        {
            macWindow.DataContext = view.DataContext = null;
            macWindow.Close();
            window.Close();
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
                NativeMenuItem delete = NativeEntry(native, Strings.Edit, Strings.Delete);
                Assert.That(((FocusedEditCommand)delete.Command!).Fallback, Is.SameAs(menuBar.DeleteLayer));
                Assert.That(delete.Gesture, Is.Null);
                Assert.That(native.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Scene).Menu!.Items
                    .OfType<NativeMenuItem>().Select(item => item.Header), Does.Not.Contain(Strings.Element));
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
                Assert.That(tops.Single(item => Equals(item.Header, Strings.Scene)).Items.OfType<MenuItem>()
                    .Select(item => item.Header), Does.Contain(Strings.Element));
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

    [AvaloniaTest]
    public async Task Native_delete_targets_the_focused_text_box_or_the_selected_element()
    {
        (_, var scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("native-delete-menu");
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(1));
        Element element = scene.Children.Single();
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(element);

        var box = new TextBox { Text = "Selection" };
        var button = new Button { Content = "Timeline" };
        var window = new Window { Content = new StackPanel { Children = { box, button } } };
        var menu = new NativeMenu();
        using var subscriptions = new CompositeDisposable();
        MainMenuBuilder.AddMenus(menu, TestShell.MainViewModel.MenuItems, window, subscriptions);
        NativeMenuItem deleteItem = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Edit).Menu!.Items
            .OfType<NativeMenuItem>().Single(item => item.Header == Strings.Delete);
        ICommand delete = deleteItem.Command!;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(box.Focus(), Is.True);
            box.SelectAll();
            delete.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(box.Text, Is.Empty);
                Assert.That(scene.Children, Does.Contain(element));
            });

            Assert.That(button.Focus(), Is.True);
            using (TestShell.Editor.SuspendEditors())
            {
                HeadlessTestHelpers.Settle();
                Assert.That(deleteItem.IsEnabled, Is.False);
                delete.Execute(null);
                HeadlessTestHelpers.Settle();
                Assert.That(scene.Children, Does.Contain(element));
            }

            HeadlessTestHelpers.Settle();
            Assert.That(deleteItem.IsEnabled, Is.True);
            delete.Execute(null);
            await HeadlessTestHelpers.WaitUntilAsync(() => !scene.Children.Contains(element));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
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
