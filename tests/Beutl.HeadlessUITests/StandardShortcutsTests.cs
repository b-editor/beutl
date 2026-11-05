using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Helpers;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views;
using Dock.Model.Core;
using FluentAvalonia.UI.Controls;
using Iciclecreek.Terminal;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class StandardShortcutsTests
{
    [AvaloniaTest]
    [TestCase("main.showSettings", "Cmd+OemComma")]
    [TestCase("main.closeTab", "Cmd+W")]
    [TestCase("main.closeProject", "Cmd+Shift+W")]
    [TestCase("main.showTutorials", "Cmd+Shift+OemQuestion")]
    [TestCase("main.minimizeWindow", "Cmd+M")]
    [TestCase("main.toggleFullScreen", "Cmd+Ctrl+F")]
    [TestCase("timeline.selectAll", "Cmd+A")]
    [TestCase("timeline.deselectAll", "Cmd+Shift+A")]
    [TestCase("timeline.zoomIn", "Cmd+OemPlus")]
    [TestCase("timeline.zoomIn", "Cmd+Shift+OemPlus")]
    [TestCase("timeline.zoomOut", "Cmd+OemMinus")]
    [TestCase("timeline.resetZoom", "Cmd+D0")]
    public void A_standard_macOS_key_belongs_to_its_command_alone(string id, string gesture)
    {
        KeyGesture expected = KeyGesture.Parse(gesture);
        string[] owners = [.. TestShell.MainViewModel.ContextCommandManager.GetDefinitions()
            .Where(entry => entry.KeyGestures.Any(g => g.Platform == OSPlatform.OSX && expected.Equals(g.KeyGesture)))
            .Select(entry => entry.Definition.Id)];

        Assert.That(owners, Is.EqualTo(new[] { id }));
    }

    // Undo in a text box: the regression that key equivalents caused, since the menu takes the key before the box.
    [AvaloniaTest]
    public void An_edit_entry_undoes_the_focused_text_box_and_not_the_editor()
    {
        using var fixture = new TextBoxFixture();
        fixture.Window.KeyTextInput("hello");
        HeadlessTestHelpers.Settle();
        var fallback = new RecordingCommand();

        new FocusedEditCommand(StandardEditAction.Undo, fallback, fixture.Window).Execute(null);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Box.Text ?? "", Is.Empty);
            Assert.That(fallback.Runs, Is.Zero);
        });

        new FocusedEditCommand(StandardEditAction.Redo, fallback, fixture.Window).Execute(null);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Box.Text, Is.EqualTo("hello"));
            Assert.That(fallback.Runs, Is.Zero);
        });
    }

    [AvaloniaTest]
    public void The_other_edit_entries_reach_the_focused_text_box()
    {
        using var fixture = new TextBoxFixture();
        fixture.Window.KeyTextInput("hello");
        HeadlessTestHelpers.Settle();
        var fallback = new RecordingCommand();
        void Run(StandardEditAction action)
        {
            new FocusedEditCommand(action, fallback, fixture.Window).Execute(null);
            HeadlessTestHelpers.Settle();
        }

        Run(StandardEditAction.SelectAll);
        Assert.That(fixture.Box.SelectedText, Is.EqualTo("hello"));

        Run(StandardEditAction.Copy);
        Run(StandardEditAction.Cut);
        Assert.That(fixture.Box.Text ?? "", Is.Empty);

        Run(StandardEditAction.Paste);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Box.Text, Is.EqualTo("hello"));
            Assert.That(fallback.Runs, Is.Zero);
        });
    }

    [AvaloniaTest]
    public void A_panel_that_handles_the_key_gets_it_with_the_modifier_of_the_platform()
    {
        var fallback = new RecordingCommand();
        var panel = new Border { Focusable = true };
        KeyEventArgs? seen = null;
        panel.KeyDown += (_, e) =>
        {
            seen = e;
            e.Handled = true;
        };
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            panel.Focus();
            new FocusedEditCommand(StandardEditAction.Copy, fallback, window).Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(FocusedEditCommand.GetKeyGesture(StandardEditAction.Copy).Matches(seen), Is.True);
                Assert.That(seen!.Source, Is.SameAs(panel));
                Assert.That(fallback.Runs, Is.Zero);
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A key map can give the entry another key, and the standard one to another command.
    [AvaloniaTest]
    public void An_entry_hands_over_the_key_it_shows()
    {
        var shown = new KeyGesture(Key.U, KeyModifiers.Meta | KeyModifiers.Alt);
        var panel = new Border { Focusable = true };
        KeyEventArgs? seen = null;
        panel.KeyDown += (_, e) =>
        {
            seen = e;
            e.Handled = true;
        };
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            panel.Focus();
            new FocusedEditCommand(StandardEditAction.Undo, new RecordingCommand(), window, shown).Execute(null);

            Assert.That(shown.Matches(seen), Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    // A text box knows only the standard keys, and what it leaves would bubble to the command with that key.
    [AvaloniaTest]
    public void A_text_box_gets_the_standard_key_whichever_the_entry_shows()
    {
        using var fixture = new TextBoxFixture();
        fixture.Window.KeyTextInput("hello");
        HeadlessTestHelpers.Settle();
        var shown = new KeyGesture(Key.U, KeyModifiers.Meta | KeyModifiers.Alt);

        new FocusedEditCommand(StandardEditAction.Undo, new RecordingCommand(), fixture.Window, shown).Execute(null);
        HeadlessTestHelpers.Settle();

        Assert.That(fixture.Box.Text ?? "", Is.Empty);
    }

    // Without a scene the fallbacks are off, as in the dialog that creates one, and the menu must not be.
    [AvaloniaTest]
    public void A_focused_text_input_enables_an_entry_whose_fallback_is_off()
    {
        var fallback = new RecordingCommand { CanRun = false };
        var box = new TextBox();
        var panel = new Border { Focusable = true };
        var window = new Window { Content = new StackPanel { Children = { box, panel } } };
        window.Show();
        var command = new FocusedEditCommand(StandardEditAction.Copy, fallback, window);
        int changes = 0;
        command.CanExecuteChanged += (_, _) => changes++;
        using IDisposable following = command.FollowFocus();
        try
        {
            panel.Focus();
            Assert.That(command.CanExecute(null), Is.False);

            int before = changes;
            box.Focus();
            Assert.Multiple(() =>
            {
                Assert.That(command.CanExecute(null), Is.True);
                Assert.That(changes, Is.GreaterThan(before));
            });

            before = changes;
            panel.Focus();
            Assert.Multiple(() =>
            {
                Assert.That(command.CanExecute(null), Is.False);
                Assert.That(changes, Is.GreaterThan(before));
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A key does what it does without the menu: Cut only reaches a panel that handles it. Undo acted on the editor from
    // anywhere before the entries sent it to the focus, and choosing an entry with the mouse names the selected clip.
    [AvaloniaTest]
    [TestCase(StandardEditAction.Cut, true, 0)]
    [TestCase(StandardEditAction.Copy, true, 0)]
    [TestCase(StandardEditAction.Paste, true, 0)]
    [TestCase(StandardEditAction.SelectAll, true, 0)]
    [TestCase(StandardEditAction.Undo, true, 1)]
    [TestCase(StandardEditAction.Redo, true, 1)]
    [TestCase(StandardEditAction.Cut, false, 1)]
    [TestCase(StandardEditAction.SelectAll, false, 1)]
    public void What_no_control_takes_falls_back_unless_a_key_has_no_panel_for_it(StandardEditAction action, bool isKey, int runs)
    {
        var fallback = new RecordingCommand();
        var panel = new Border { Focusable = true };
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            panel.Focus();
            new FocusedEditCommand(action, fallback, window) { IsKeyEquivalent = () => isKey }.Execute(null);

            Assert.That(fallback.Runs, Is.EqualTo(runs));
        }
        finally
        {
            window.Close();
        }
    }

    // The terminal is a text input that lets the key through; the clip behind it must stay out of it.
    [AvaloniaTest]
    public void A_text_input_that_leaves_the_key_gets_no_fallback()
    {
        var fallback = new RecordingCommand();
        var terminal = new Border { Focusable = true };
        ContextCommandInput.SetIsTextInput(terminal, true);
        var window = new Window { Content = terminal };
        window.Show();
        try
        {
            terminal.Focus();
            new FocusedEditCommand(StandardEditAction.Paste, fallback, window).Execute(null);

            Assert.That(fallback.Runs, Is.Zero);
        }
        finally
        {
            window.Close();
        }
    }

    // A text box leaves Right alone at its end and Left at its start, and the key bubbles to the editor around it.
    [AvaloniaTest]
    [TestCase(Key.Right, 3)]
    [TestCase(Key.Left, 0)]
    public async Task A_key_that_a_text_box_leaves_does_not_move_the_playhead(Key key, int caret)
    {
        (Window window, MainView view, EditViewModel editor) = await ShowEditorAsync($"shortcuts-caret-{key}");
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        MediaTime start = MediaTime.FromSeconds(1);
        clock.Seek(start);
        Panel panel = view.GetVisualDescendants().OfType<EditorHostView>().Single().GetVisualDescendants().OfType<Panel>().First();
        var box = new TextBox { Text = "abc", CaretIndex = caret };
        var other = new Border { Focusable = true };
        panel.Children.Add(box);
        panel.Children.Add(other);
        try
        {
            HeadlessTestHelpers.Render();
            box.Focus();
            box.RaiseEvent(KeyDown(box, key, KeyModifiers.None));
            HeadlessTestHelpers.Settle();
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(start));

            // The same key anywhere else in the editor still moves it.
            other.Focus();
            other.RaiseEvent(KeyDown(other, key, KeyModifiers.None));
            HeadlessTestHelpers.Settle();
            Assert.That(clock.CurrentTime.Value, Is.Not.EqualTo(start));
        }
        finally
        {
            panel.Children.Remove(box);
            panel.Children.Remove(other);
            await CloseAsync(window, view);
        }
    }

    // A dialog does not always take the focus: with no default button it can stay on what the dialog covers.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task A_dialog_keeps_the_entries_of_the_menu_bar_from_acting_behind_it(bool focusInDialog)
    {
        (Window window, MainView view, EditViewModel editor) = await ShowEditorAsync($"shortcuts-dialog-{focusInDialog}");
        var button = new Button { Content = "OK" };
        var dialog = new FAContentDialog { Content = button };
        var fallback = new RecordingCommand();
        BeutlDockFactory factory = editor.DockHost.Factory;
        BeutlToolDockable tool = FocusFirstTool(factory);
        int keys = 0;
        view.AddHandler(InputElement.KeyDownEvent, (_, _) => keys++, RoutingStrategies.Tunnel);
        try
        {
            _ = dialog.ShowAsync(window);
            HeadlessTestHelpers.Settle();
            if (focusInDialog)
                button.Focus();
            else
                view.Focus();

            new FocusedEditCommand(StandardEditAction.Paste, fallback, window).Execute(null);
            TestShell.MainViewModel.MenuBar.CloseTab.Execute();
            TestShell.MainViewModel.MenuBar.ShowTutorials.Execute();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(fallback.Runs, Is.Zero);
                Assert.That(keys, Is.Zero);
                Assert.That(factory.EnumerateTools(), Does.Contain(tool));
                Assert.That(FocusedEditCommand.IsDialogOpen(window), Is.True);
                Assert.That(
                    OverlayLayer.GetOverlayLayer(window)!.GetVisualDescendants().OfType<FAContentDialog>().Count(),
                    Is.EqualTo(1));
            });
        }
        finally
        {
            dialog.Hide();
            HeadlessTestHelpers.Settle();
            await CloseAsync(window, view);
        }
    }

    [AvaloniaTest]
    public void An_edit_entry_is_enabled_as_its_fallback_is()
    {
        var fallback = new RecordingCommand { CanRun = false };
        var window = new Window();
        var command = new FocusedEditCommand(StandardEditAction.Undo, fallback, window);
        int changes = 0;
        command.CanExecuteChanged += (_, _) => changes++;

        Assert.That(command.CanExecute(null), Is.False);

        fallback.CanRun = true;
        fallback.RaiseCanExecuteChanged();

        Assert.Multiple(() =>
        {
            Assert.That(command.CanExecute(null), Is.True);
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task Close_tab_closes_the_focused_tool_tab_from_the_menu_and_the_key_and_keeps_the_focus_in_the_view()
    {
        (Window window, MainView view, EditViewModel editor) = await ShowEditorAsync("shortcuts-close-tab");
        BeutlDockFactory factory = editor.DockHost.Factory;
        try
        {
            BeutlToolDockable first = FocusFirstTool(factory);
            HeadlessTestHelpers.Render(2);
            var inside = (Control)view.GetVisualDescendants().OfType<ToolTabContent>()
                .First(content => ReferenceEquals(content.DataContext, first)).Content!;
            inside.Focusable = true;
            inside.Focus();
            TestShell.MainViewModel.MenuBar.CloseTab.Execute();
            HeadlessTestHelpers.Settle();
            Assert.That(factory.EnumerateTools(), Does.Not.Contain(first));
            Assert.That(window.FocusManager?.GetFocusedElement(), Is.Not.Null.And.Not.SameAs(inside));

            view.Focus();
            BeutlToolDockable second = FocusFirstTool(factory);
            Press(window, GetGesture(MainViewExtension.CloseTabCommandId));
            Assert.That(factory.EnumerateTools(), Does.Not.Contain(second));
        }
        finally
        {
            await CloseAsync(window, view);
        }
    }

    [AvaloniaTest]
    public async Task Close_tab_leaves_the_preview_which_cannot_close()
    {
        (Window window, MainView view, EditViewModel editor) = await ShowEditorAsync("shortcuts-close-preview");
        BeutlDockFactory factory = editor.DockHost.Factory;
        try
        {
            IDockable player = BeutlDockFactory.Traverse(editor.DockHost.Layout.Value).First(d => d is PlayerToolDockable);
            factory.SetActiveDockable(player);
            factory.SetFocusedDockable((IDock)player.Owner!, player);
            HeadlessTestHelpers.Settle();
            int tools = factory.EnumerateTools().Count();

            TestShell.MainViewModel.MenuBar.CloseTab.Execute();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(factory.EnumerateTools().Count(), Is.EqualTo(tools));
                Assert.That(BeutlDockFactory.Traverse(editor.DockHost.Layout.Value), Does.Contain(player));
                Assert.That(window.IsVisible, Is.True);
            });
        }
        finally
        {
            await CloseAsync(window, view);
        }
    }

    // With nothing left to close, a macOS window closes itself. On the other platforms the key would quit the app.
    [AvaloniaTest]
    public async Task Close_tab_with_no_editor_open_closes_only_a_macOS_window()
    {
        await TestReset.ResetShellAsync();
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Content = view };
        window.Show();
        HeadlessTestHelpers.Render(2);
        try
        {
            TestShell.MainViewModel.MenuBar.CloseTab.Execute();
            HeadlessTestHelpers.Settle();

            Assert.That(window.IsVisible, Is.EqualTo(!OperatingSystem.IsMacOS()));
        }
        finally
        {
            await CloseAsync(window, view);
        }
    }

    [AvaloniaTest]
    public async Task The_window_entries_change_the_state_of_the_window()
    {
        (Window window, MainView view, _) = await ShowEditorAsync("shortcuts-window-state");
        MenuBarViewModel menu = TestShell.MainViewModel.MenuBar;
        try
        {
            menu.ZoomWindow.Execute();
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
            menu.ZoomWindow.Execute();
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));

            menu.ToggleFullScreen.Execute();
            Assert.That(window.WindowState, Is.EqualTo(WindowState.FullScreen));
            menu.ZoomWindow.Execute();
            Assert.That(window.WindowState, Is.EqualTo(WindowState.FullScreen), "Zoom has no meaning in full screen.");
            menu.ToggleFullScreen.Execute();
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));

            menu.MinimizeWindow.Execute();
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Minimized));
        }
        finally
        {
            await CloseAsync(window, view);
        }
    }

    [AvaloniaTest]
    public async Task Settings_opens_once_and_keeps_its_page()
    {
        await TestReset.ResetShellAsync();
        MenuBarViewModel menu = TestShell.MainViewModel.MenuBar;
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Content = view, Width = 1000, Height = 720 };
        window.Show();
        HeadlessTestHelpers.Render(2);
        try
        {
            menu.ShowSettings.Execute();
            HeadlessTestHelpers.Settle();
            SettingsDialog? open = SettingsDialog.Current;
            Assert.That(open, Is.Not.Null);

            menu.ShowSettings.Execute();
            menu.ShowInformation.Execute();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(SettingsDialog.Current, Is.SameAs(open));
                Assert.That(window.OwnedWindows.OfType<SettingsDialog>().Count(), Is.EqualTo(1));
            });

            open!.Close();
            HeadlessTestHelpers.Settle();
            Assert.That(SettingsDialog.Current, Is.Null);
        }
        finally
        {
            SettingsDialog.Current?.Close();
            await CloseAsync(window, view);
        }
    }

    // Only the main window has a menu bar on macOS, so the others take the same key themselves.
    [AvaloniaTest]
    public void The_close_tab_key_closes_a_secondary_window_and_not_the_main_one()
    {
        KeyGesture gesture = GetGesture(MainViewExtension.CloseTabCommandId);
        var secondary = new Window();
        var main = new Window { DataContext = TestShell.MainViewModel };
        secondary.Show();
        main.Show();
        try
        {
            KeyEventArgs other = KeyDown(secondary, Key.Q, KeyModifiers.None);
            MacOSKeyBindings.CloseSecondaryWindow(secondary, other);
            Assert.That((secondary.IsVisible, other.Handled), Is.EqualTo((true, false)));

            KeyEventArgs fromMain = KeyDown(main, gesture.Key, gesture.KeyModifiers);
            MacOSKeyBindings.CloseSecondaryWindow(main, fromMain);
            Assert.That((main.IsVisible, fromMain.Handled), Is.EqualTo((true, false)));

            KeyEventArgs close = KeyDown(secondary, gesture.Key, gesture.KeyModifiers);
            MacOSKeyBindings.CloseSecondaryWindow(secondary, close);
            Assert.That((secondary.IsVisible, close.Handled), Is.EqualTo((false, true)));
        }
        finally
        {
            secondary.Close();
            main.DataContext = null;
            main.Close();
        }
    }

    // A floating dock window can hold several tabs, and only the last one takes the window with it.
    [AvaloniaTest]
    public async Task The_close_tab_key_closes_the_focused_tab_of_a_floating_window()
    {
        (Window window, MainView view, EditViewModel editor) = await ShowEditorAsync("shortcuts-floating");
        BeutlDockFactory factory = editor.DockHost.Factory;
        BeutlToolDockable[] tools = [.. factory.EnumerateTools().Take(2)];
        KeyGesture gesture = GetGesture(MainViewExtension.CloseTabCommandId);
        factory.SplitToWindow((IDock)tools[0].Owner!, tools[0], 20, 20, 500, 400, null);
        HeadlessTestHelpers.Render(2);
        var floating = (Window)factory.FindRoot(tools[0], _ => true)!.Window!.Host!;
        try
        {
            factory.MoveDockable((IDock)tools[1].Owner!, (IDock)tools[0].Owner!, tools[1], null);
            factory.SetFocusedDockable((IDock)tools[1].Owner!, tools[1]);
            HeadlessTestHelpers.Render(2);

            KeyEventArgs first = KeyDown(floating, gesture.Key, gesture.KeyModifiers);
            MacOSKeyBindings.CloseSecondaryWindow(floating, first);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(first.Handled, Is.True);
                Assert.That(factory.EnumerateTools(), Does.Not.Contain(tools[1]));
                Assert.That(factory.EnumerateTools(), Does.Contain(tools[0]));
                Assert.That(floating.IsVisible, Is.True);
            });

            MacOSKeyBindings.CloseSecondaryWindow(floating, KeyDown(floating, gesture.Key, gesture.KeyModifiers));
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(factory.EnumerateTools(), Does.Not.Contain(tools[0]));
                Assert.That(floating.IsVisible, Is.False);
            });
        }
        finally
        {
            floating.Close();
            await CloseAsync(window, view);
        }
    }

    [AvaloniaTest]
    public void Hide_others_moves_to_its_standard_key_and_the_rest_of_the_menu_stays()
    {
        var hideOthers = new NativeMenuItem("Hide Others") { Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta | KeyModifiers.Alt) };
        var quit = new NativeMenuItem("Quit") { Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta) };
        var settings = new NativeMenuItem("Settings");
        var menu = new NativeMenu { settings, new NativeMenuItemSeparator(), hideOthers, quit };

        MacOSKeyBindings.FixHideOthersGesture(menu);

        Assert.Multiple(() =>
        {
            Assert.That(hideOthers.Gesture, Is.EqualTo(new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt)));
            Assert.That(quit.Gesture, Is.EqualTo(new KeyGesture(Key.Q, KeyModifiers.Meta)));
            Assert.That(settings.Gesture, Is.Null);
        });
    }

    [AvaloniaTest]
    public void Text_navigation_gains_the_keys_of_macOS_text_fields()
    {
        PlatformHotkeyConfiguration hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        List<KeyGesture>[] lists =
        [
            hotkeys.MoveCursorToTheStartOfDocument, hotkeys.MoveCursorToTheEndOfDocument,
            hotkeys.MoveCursorToTheStartOfDocumentWithSelection, hotkeys.MoveCursorToTheEndOfDocumentWithSelection,
            hotkeys.MoveCursorToTheStartOfLine, hotkeys.MoveCursorToTheEndOfLine
        ];
        int[] counts = [.. lists.Select(list => list.Count)];
        using var fixture = new TextBoxFixture("line one\nline two", caret: 12);
        int Caret(Key key, KeyModifiers modifiers)
        {
            fixture.Box.RaiseEvent(KeyDown(fixture.Box, key, modifiers));
            return fixture.Box.CaretIndex;
        }

        try
        {
            MacOSKeyBindings.AddTextNavigation(hotkeys);

            Assert.Multiple(() =>
            {
                Assert.That(Caret(Key.Up, KeyModifiers.Meta), Is.Zero);
                Assert.That(Caret(Key.Down, KeyModifiers.Meta), Is.EqualTo(17));
                Caret(Key.Up, KeyModifiers.Meta | KeyModifiers.Shift);
                Assert.That(fixture.Box.SelectedText, Is.EqualTo("line one\nline two"));
                fixture.Box.ClearSelection();
                fixture.Box.CaretIndex = 12;
                Caret(Key.Down, KeyModifiers.Meta | KeyModifiers.Shift);
                Assert.That(fixture.Box.SelectedText, Is.EqualTo("e two"));
                fixture.Box.ClearSelection();
                fixture.Box.CaretIndex = 12;
                Assert.That(Caret(Key.E, KeyModifiers.Control), Is.EqualTo(17));
                // Ctrl+A only moves on macOS, whose select-all is on Cmd. Elsewhere SelectAll comes first.
                Assert.That(hotkeys.MoveCursorToTheStartOfLine, Does.Contain(new KeyGesture(Key.A, KeyModifiers.Control)));
            });
        }
        finally
        {
            for (int i = 0; i < lists.Length; i++)
                lists[i].RemoveRange(counts[i], lists[i].Count - counts[i]);
        }
    }

    [AvaloniaTest]
    [TestCase(Key.Back, KeyModifiers.Meta, "line one\ne two", 9)]
    [TestCase(Key.K, KeyModifiers.Control, "line one\nlin", 12)]
    [TestCase(Key.D, KeyModifiers.Control, "line one\nlin two", 12)]
    [TestCase(Key.H, KeyModifiers.Control, "line one\nlie two", 11)]
    [TestCase(Key.F, KeyModifiers.Control, "line one\nline two", 13)]
    [TestCase(Key.B, KeyModifiers.Control, "line one\nline two", 11)]
    public void A_macOS_text_command_edits_the_box_and_takes_the_key(Key key, KeyModifiers modifiers, string text, int caret)
    {
        using var fixture = new TextBoxFixture("line one\nline two", caret: 12);

        KeyEventArgs args = KeyDown(fixture.Box, key, modifiers);
        MacOSKeyBindings.OnTextBoxKeyDown(fixture.Box, args);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Box.Text, Is.EqualTo(text));
            Assert.That(fixture.Box.CaretIndex, Is.EqualTo(caret));
            Assert.That(args.Handled, Is.True);
        });
    }

    [AvaloniaTest]
    [TestCase(Key.N, 3, 12)]
    [TestCase(Key.P, 12, 3)]
    public void A_macOS_text_command_moves_the_caret_between_lines(Key key, int from, int to)
    {
        using var fixture = new TextBoxFixture("line one\nline two", caret: from);

        KeyEventArgs args = KeyDown(fixture.Box, key, KeyModifiers.Control);
        MacOSKeyBindings.OnTextBoxKeyDown(fixture.Box, args);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Box.CaretIndex, Is.EqualTo(to));
            Assert.That(args.Handled, Is.True);
        });
    }

    [AvaloniaTest]
    public void Deleting_to_the_line_start_deletes_only_a_selection_when_there_is_one()
    {
        using var fixture = new TextBoxFixture("line one", caret: 5);
        fixture.Box.SelectionStart = 5;
        fixture.Box.SelectionEnd = 8;

        MacOSKeyBindings.OnTextBoxKeyDown(fixture.Box, KeyDown(fixture.Box, Key.Back, KeyModifiers.Meta));

        Assert.That(fixture.Box.Text, Is.EqualTo("line "));
    }

    [AvaloniaTest]
    [TestCase(Key.A, KeyModifiers.Control)]
    [TestCase(Key.K, KeyModifiers.Meta)]
    [TestCase(Key.Back, KeyModifiers.None)]
    public void A_key_that_is_no_text_command_stays_with_the_box(Key key, KeyModifiers modifiers)
    {
        using var fixture = new TextBoxFixture("line one", caret: 5);

        KeyEventArgs args = KeyDown(fixture.Box, key, modifiers);
        MacOSKeyBindings.OnTextBoxKeyDown(fixture.Box, args);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Box.Text, Is.EqualTo("line one"));
            Assert.That(args.Handled, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task The_timeline_selects_all_and_keeps_what_was_selected()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("shortcuts-select-all", duration: MediaTime.FromSeconds(30));
        try
        {
            await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(1));
            await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), layer: 1);
            TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
            selection.SelectedElements.Clear();
            selection.SelectedElements.Add(scene.Children[1]);

            await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.selectAll"));
            HeadlessTestHelpers.Settle();
            Assert.That(selection.SelectedElements, Is.EquivalentTo(scene.Children));

            await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.deselectAll"));
            HeadlessTestHelpers.Settle();
            Assert.That(selection.SelectedElements, Is.Empty);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Zooming_the_timeline_with_the_keys_keeps_the_playhead_in_place_within_the_limits()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("shortcuts-zoom", duration: MediaTime.FromSeconds(30));
        try
        {
            TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            MediaTime playhead = MediaTime.FromSeconds(2);
            ((IEditorClock)editor.GetService(typeof(IEditorClock))!).Seek(playhead);
            HeadlessTestHelpers.Settle();
            timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = new System.Numerics.Vector2(100, 0) };
            double PlayheadInView() => playhead.TimeToPixel(timeline.Options.Value.Scale) - timeline.Options.Value.Offset.X;
            double before = PlayheadInView();

            await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.zoomIn"));
            Assert.Multiple(() =>
            {
                Assert.That(timeline.Options.Value.Scale, Is.EqualTo(TimelineTabViewModel.ZoomStep).Within(1e-4));
                Assert.That(PlayheadInView(), Is.EqualTo(before).Within(1e-2));
            });

            await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.zoomOut"));
            await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.zoomOut"));
            Assert.Multiple(() =>
            {
                Assert.That(timeline.Options.Value.Scale, Is.EqualTo(1 / TimelineTabViewModel.ZoomStep).Within(1e-4));
                Assert.That(PlayheadInView(), Is.EqualTo(before).Within(1e-2));
            });

            await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.resetZoom"));
            Assert.That(timeline.Options.Value.Scale, Is.EqualTo(1));

            for (int i = 0; i < 20; i++)
                await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.zoomIn"));
            Assert.That(timeline.Options.Value.Scale, Is.EqualTo(TimelineOptions.MaxScale));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(KeyModifiers.Control)]
    [TestCase(KeyModifiers.Meta)]
    public void The_command_key_moves_on_from_a_multiline_box_as_ctrl_does(KeyModifiers modifiers)
    {
        var box = new TextBox { AcceptsReturn = true };
        var next = new Button();
        TextBoxAttachment.SetEnterDownBehavior(box, TextBoxAttachment.EnterBehaviorMode.Auto);
        var panel = new StackPanel { Children = { box, next } };
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            box.Focus();
            box.RaiseEvent(KeyDown(box, Key.Enter, modifiers));
            Assert.That(next.IsFocused, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(KeyModifiers.Control)]
    [TestCase(KeyModifiers.Meta)]
    public void The_command_key_confirms_an_expression_as_ctrl_does(KeyModifiers modifiers)
    {
        var anchor = new Button();
        var window = new Window { Content = anchor };
        window.Show();
        var flyout = new ExpressionEditorFlyout { ExpressionText = "1" };
        string? confirmed = null;
        flyout.Confirmed += (_, e) => confirmed = e.ExpressionText;
        try
        {
            flyout.ShowAt(anchor);
            HeadlessTestHelpers.Settle();
            var presenter = (ExpressionEditorFlyoutPresenter)window.OpenedPopups.Single(popup => popup.Child is ExpressionEditorFlyoutPresenter).Child!;

            presenter.RaiseEvent(KeyDown(presenter, Key.Enter, modifiers));

            Assert.That(confirmed, Is.EqualTo("1"));
        }
        finally
        {
            flyout.Hide();
            window.Close();
        }
    }

    // The flyout's popup routes its keys through the window, where Cmd+W would close the window it is assigned in.
    [AvaloniaTest]
    public async Task A_chord_being_assigned_stops_at_the_flyout_and_the_row_gets_keys_again_after_it()
    {
        await TestReset.ResetShellAsync();
        using var model = TestShell.MainViewModel.CreateSettingsDialog();
        var page = new KeyMapSettingsPage { DataContext = model.KeyMap };
        var window = new Window { Content = page, Width = 1000, Height = 900 };
        window.Show();
        try
        {
            (Button button, ContentControl presenter) = OpenKeyMapFlyout(page, window);

            // A key the box inside would use for itself.
            TextBox gestureBox = ((Grid)presenter.Content!).Children.OfType<TextBox>().Single();
            KeyEventArgs inBox = KeyDown(gestureBox, Key.A, KeyModifiers.Control);
            gestureBox.RaiseEvent(inBox);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(inBox.Handled, Is.True);
                Assert.That(gestureBox.Text, Is.EqualTo(new KeyGesture(Key.A, KeyModifiers.Control).ToString()));
            });

            // And one that it would take without a modifier.
            gestureBox.RaiseEvent(KeyDown(gestureBox, Key.Home, KeyModifiers.None));
            HeadlessTestHelpers.Settle();
            Assert.That(gestureBox.Text, Is.EqualTo(new KeyGesture(Key.Home).ToString()));

            KeyEventArgs chord = KeyDown(presenter, Key.W, KeyModifiers.Meta);
            presenter.RaiseEvent(chord);
            KeyEventArgs plain = KeyDown(presenter, Key.F5, KeyModifiers.None);
            presenter.RaiseEvent(plain);
            KeyEventArgs onRow = KeyDown(button, Key.W, KeyModifiers.Meta);
            button.RaiseEvent(onRow);
            Assert.Multiple(() =>
            {
                Assert.That(chord.Handled, Is.True);
                Assert.That(plain.Handled, Is.False);
                Assert.That(onRow.Handled, Is.True);
            });

            presenter.RaiseEvent(KeyDown(presenter, Key.Escape, KeyModifiers.None));
            HeadlessTestHelpers.Render(3);

            KeyEventArgs afterwards = KeyDown(button, Key.W, KeyModifiers.Meta);
            button.RaiseEvent(afterwards);
            Assert.That(afterwards.Handled, Is.False, "With the flyout closed, the key is the window's again.");
        }
        finally
        {
            window.Close();
        }
    }

    // Enter acts on the button with the focus, and is not what is being assigned.
    [AvaloniaTest]
    public async Task Enter_on_the_checkmark_saves_the_chord_pressed_before_it()
    {
        await TestReset.ResetShellAsync();
        using var model = TestShell.MainViewModel.CreateSettingsDialog();
        var page = new KeyMapSettingsPage { DataContext = model.KeyMap };
        var window = new Window { Content = page, Width = 1000, Height = 900 };
        window.Show();
        (Button row, ContentControl presenter) = OpenKeyMapFlyout(page, window);
        var item = (KeyMapSettingsItem)row.DataContext!;
        KeyGesture? original = item.KeyGesture.Value;
        try
        {
            presenter.RaiseEvent(KeyDown(presenter, Key.K, KeyModifiers.Meta | KeyModifiers.Alt));
            Button accept = presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton");
            accept.RaiseEvent(KeyDown(accept, Key.Enter, KeyModifiers.None));
            HeadlessTestHelpers.Render(3);

            Assert.That(item.KeyGesture.Value, Is.EqualTo(new KeyGesture(Key.K, KeyModifiers.Meta | KeyModifiers.Alt)));
        }
        finally
        {
            item.SetKeyGesture(original);
            window.Close();
        }
    }

    private static (Button Row, ContentControl Presenter) OpenKeyMapFlyout(KeyMapSettingsPage page, Window window)
    {
        HeadlessTestHelpers.Render();
        OptionsDisplayItem group = page.GetVisualDescendants().OfType<OptionsDisplayItem>().First(row => row.Expands);
        group.ContentTransition = null;
        group.IsExpanded = true;
        HeadlessTestHelpers.Render();
        var button = (Button)page.GetVisualDescendants().OfType<OptionsDisplayItem>().First(row => row.ActionButton is Button).ActionButton!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        return (button, (ContentControl)window.OpenedPopups.Single(popup => popup.Child is ContentControl).Child!);
    }

    [AvaloniaTest]
    [TestCase(Key.C)]
    [TestCase(Key.V)]
    public async Task The_terminal_takes_the_command_keys_that_copy_and_paste(Key key)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("shortcuts-terminal");
        using var terminalViewModel = new TerminalTabViewModel(editor);
        using var terminalTab = new TerminalTabView { DataContext = terminalViewModel };
        var window = new Window { Content = terminalTab };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            TerminalView terminal = terminalTab.FindControl<TerminalControl>("Terminal")!
                .GetVisualDescendants().OfType<TerminalView>().Single();

            KeyEventArgs args = KeyDown(terminal, key, KeyModifiers.Meta);
            terminal.RaiseEvent(args);
            HeadlessTestHelpers.Settle();

            Assert.That(args.Handled, Is.True);
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    private static KeyGesture GetGesture(string id)
        => GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), id)!;

    private static KeyEventArgs KeyDown(Interactive source, Key key, KeyModifiers modifiers) => new()
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Source = source,
        Key = key,
        KeyModifiers = modifiers
    };

    private static void Press(Window window, KeyGesture gesture)
    {
        window.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, PhysicalKey.None, null);
        HeadlessTestHelpers.Settle();
    }

    private static BeutlToolDockable FocusFirstTool(BeutlDockFactory factory)
    {
        BeutlToolDockable tool = factory.EnumerateTools().First();
        factory.SetActiveDockable(tool);
        factory.SetFocusedDockable((IDock)tool.Owner!, tool);
        HeadlessTestHelpers.Settle();
        return tool;
    }

    private static async Task<(Window Window, MainView View, EditViewModel Editor)> ShowEditorAsync(string name)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(name);
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Content = view, Width = 1000, Height = 720 };
        window.Show();
        HeadlessTestHelpers.Render(2);
        return (window, view, editor);
    }

    private static async Task CloseAsync(Window window, MainView view)
    {
        view.DataContext = null;
        window.Close();
        HeadlessTestHelpers.Settle();
        await TestReset.ResetShellAsync();
    }

    private sealed class TextBoxFixture : IDisposable
    {
        public TextBoxFixture(string? text = null, int caret = 0)
        {
            Box = new TextBox { AcceptsReturn = true, Text = text };
            Window = new Window { Content = Box };
            Window.Show();
            HeadlessTestHelpers.Render();
            Box.Focus();
            Box.CaretIndex = caret;
        }

        public Window Window { get; }

        public TextBox Box { get; }

        public void Dispose() => Window.Close();
    }

    private sealed class RecordingCommand : ICommand
    {
        public int Runs { get; private set; }

        public bool CanRun { get; set; } = true;

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        public bool CanExecute(object? parameter) => CanRun;

        public void Execute(object? parameter) => Runs++;
    }
}
