using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Model.Core;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// A tool floated out of the main window gets the keys that it would get there (b-editor/beutl#4048).
[TestFixture, NonParallelizable]
public sealed class FloatingToolWindowShortcutsTests
{
    [AvaloniaTest]
    public async Task A_key_that_a_floating_tool_leaves_reaches_the_editor_and_the_main_window()
    {
        await using Floated floated = await Floated.CreateAsync("floating-shortcuts-keys");
        Scene scene = floated.Editor.Scene;

        floated.Press(EditorGesture("editor.toggleMarker"));
        Assert.That(scene.Markers, Has.Count.EqualTo(1), "The editor's key toggles a marker.");

        floated.Press(MainGesture("main.undo"));
        // Undo runs asynchronously.
        await HeadlessTestHelpers.WaitUntilAsync(() => scene.Markers.Count == 0, message: "The main window's key undoes it.");
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Switching_scenes_hides_and_restores_their_floating_tools(bool useMenu)
    {
        await using Floated floated = await Floated.CreateAsync($"floating-scene-visibility-{useMenu}");
        var tool = (BeutlToolDockable)floated.Floating.GetVisualDescendants().OfType<ToolTabContent>().Single().DataContext!;
        var position = floated.Floating.Position;
        var size = floated.Floating.ClientSize;
        var owner = floated.Floating.Owner;
        int closed = 0;
        floated.Floating.Closed += (_, _) => closed++;
        HeadlessCapture.SaveIfRequested(floated.Floating, nameof(FloatingToolWindowShortcutsTests), "scene-a-floating");
        EditViewModel other = OpenAnotherScene(floated);
        other.ToggleMarkerAtCurrentTime();
        Assert.That(floated.Floating.IsVisible, Is.False, "An inactive scene's floating tools are hidden.");
        Assert.That(closed, Is.Zero, "Hiding a window preserves its tools and layout.");
        (Window otherWindow, _) = FloatTool(other);
        floated.Window.Activate();
        HeadlessTestHelpers.Render(2);
        HeadlessCapture.SaveIfRequested(floated.Window, nameof(FloatingToolWindowShortcutsTests), "other-scene-selected");
        int otherTools = other.DockHost.Factory.EnumerateTools().Count();
        int activations = 0;
        floated.Floating.Activated += (_, _) => activations++;

        TestShell.Editor.ActivateTabItem(floated.Editor.Scene);
        HeadlessTestHelpers.Render(2);

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Editor.SelectedTabItem.Value!.Context.Value, Is.SameAs(floated.Editor));
            Assert.That(floated.Floating.IsVisible, Is.True);
            Assert.That(otherWindow.IsVisible, Is.False);
            Assert.That(floated.Floating.Position, Is.EqualTo(position));
            Assert.That(floated.Floating.ClientSize, Is.EqualTo(size));
            Assert.That(floated.Floating.Owner, Is.SameAs(owner));
            Assert.That(activations, Is.Zero, "Restoring a window must not take focus from the scene switcher.");
            Assert.That(closed, Is.Zero);
        });
        HeadlessCapture.SaveIfRequested(floated.Floating, nameof(FloatingToolWindowShortcutsTests), "scene-a-restored");
        floated.Floating.Activate();
        floated.Inside.Focus();
        floated.Press(EditorGesture("editor.toggleMarker"));
        Assert.That(floated.Editor.Scene.Markers, Has.Count.EqualTo(1));
        if (useMenu)
        {
            if (OperatingSystem.IsMacOS())
            {
                NativeMenuItem undo = NativeMenu.GetMenu(floated.Floating)!.Items.OfType<NativeMenuItem>()
                    .Single(item => item.Header == Strings.Edit).Menu!.Items.OfType<NativeMenuItem>()
                    .Single(item => item.Header == Strings.Undo);
                undo.Command!.Execute(null);
            }
            else
            {
                await TestShell.MainViewModel.MenuBar.Undo.ExecuteAsync();
            }
        }
        else
        {
            floated.Press(MainGesture("main.undo"));
        }

        await HeadlessTestHelpers.WaitUntilAsync(() => floated.Editor.Scene.Markers.Count == 0);
        Assert.That(other.Scene.Markers, Has.Count.EqualTo(1), "The other scene's history is untouched.");
        HeadlessCapture.SaveIfRequested(floated.Window, nameof(FloatingToolWindowShortcutsTests), "floating-scene-selected");

        floated.Press(MainGesture(MainViewExtension.CloseTabCommandId));
        Assert.Multiple(() =>
        {
            Assert.That(floated.Editor.DockHost.Factory.EnumerateTools(), Does.Not.Contain(tool));
            Assert.That(other.DockHost.Factory.EnumerateTools().Count(), Is.EqualTo(otherTools));
        });
    }

    [AvaloniaTest]
    public async Task Closing_an_unselected_scene_closes_its_floating_window_without_selecting_it()
    {
        await using Floated floated = await Floated.CreateAsync("floating-scene-close");
        int closed = 0;
        floated.Floating.Closed += (_, _) => closed++;
        EditViewModel other = OpenAnotherScene(floated);
        floated.Window.Activate();
        Assert.That(floated.Floating.IsVisible, Is.False);
        Assert.That(closed, Is.Zero);

        await TestShell.Editor.CloseTabItem(floated.Editor.Scene);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(floated.Floating.IsVisible, Is.False);
            Assert.That(closed, Is.EqualTo(1), "Closing a hidden scene still closes its host window.");
            Assert.That(TestShell.Editor.SelectedTabItem.Value!.Context.Value, Is.SameAs(other));
            Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task A_closed_floating_window_is_not_restored_by_later_scene_switches()
    {
        await using Floated floated = await Floated.CreateAsync("floating-scene-closed-window");
        int closed = 0;
        floated.Floating.Closed += (_, _) => closed++;
        floated.Floating.Close();
        HeadlessTestHelpers.Settle();

        _ = OpenAnotherScene(floated);
        TestShell.Editor.ActivateTabItem(floated.Editor.Scene);
        HeadlessTestHelpers.Render(2);

        Assert.That(floated.Floating.IsVisible, Is.False);
        Assert.That(closed, Is.EqualTo(1));
    }

    private static EditViewModel OpenAnotherScene(Floated floated)
    {
        var scene = new Scene(640, 480, "Other scene")
        {
            Uri = new Uri(Path.Combine(TestShell.ProjectRoot(floated.Editor), "other.scene"))
        };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Render(2);
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static (Window Window, Control Inside) FloatTool(EditViewModel editor)
    {
        BeutlDockFactory factory = editor.DockHost.Factory;
        BeutlToolDockable tool = factory.EnumerateTools().First();
        factory.SplitToWindow((IDock)tool.Owner!, tool, 20, 20, 500, 400, null);
        HeadlessTestHelpers.Render(2);
        var window = (Window)factory.FindRoot(tool, _ => true)!.Window!.Host!;
        var inside = (Control)window.GetVisualDescendants().OfType<ToolTabContent>()
            .Single(content => ReferenceEquals(content.DataContext, tool)).Content!;
        return (window, inside);
    }

    // Exit stands for closing the main window, and Alt+F4 closes the window it is pressed in.
    [AvaloniaTest]
    public async Task The_exit_key_stays_with_a_floating_window()
    {
        await using Floated floated = await Floated.CreateAsync("floating-shortcuts-exit");
        KeyGesture exit = MainGesture(MainViewExtension.ExitCommandId);
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = exit.Key, KeyModifiers = exit.KeyModifiers };

        floated.Inside.RaiseEvent(args);

        Assert.That(args.Handled, Is.False);
    }

    // The palette takes the main window's keys, so that window gets the keyboard.
    [AvaloniaTest]
    public async Task The_command_palette_opens_in_the_main_window_which_gets_the_keyboard()
    {
        await using Floated floated = await Floated.CreateAsync("floating-shortcuts-palette");
        CommandPaletteViewModel palette = TestShell.MainViewModel.CommandPalette;
        int activations = 0;
        floated.Window.Activated += (_, _) => activations++;
        try
        {
            floated.Press(MainGesture(MainViewExtension.ShowCommandPaletteCommandId));
            Assert.That((palette.IsOpen.Value, activations), Is.EqualTo((true, 1)));

            floated.Inside.Focus();
            floated.Press(MainGesture("main.undo"));
            Assert.That(activations, Is.EqualTo(1), "A palette that was open already does not take the keyboard again.");
        }
        finally
        {
            palette.Close();
        }
    }

    [AvaloniaTest]
    [TestCase("scene")]
    [TestCase("project")]
    [TestCase("remove")]
    [TestCase("tutorials")]
    public async Task A_main_dialog_opens_in_the_main_window_which_gets_the_keyboard(string kind)
    {
        await using Floated floated = await Floated.CreateAsync($"floating-dialog-{kind}");
        MenuBarViewModel menu = TestShell.MainViewModel.MenuBar;
        int activations = 0;
        floated.Window.Activated += (_, _) => activations++;
        Task operation = Task.CompletedTask;
        try
        {
            switch (kind)
            {
                case "scene":
                    operation = menu.NewScene.ExecuteAsync();
                    break;
                case "project":
                    operation = menu.CreateNewProject.ExecuteAsync();
                    break;
                case "remove":
                    menu.RemoveFromProject.Execute(null);
                    break;
                case "tutorials":
                    menu.ShowTutorials.Execute();
                    break;
            }

            HeadlessTestHelpers.Render(3);
            if (operation.IsFaulted)
                await operation;
            FAContentDialog? dialog = floated.Window.GetVisualDescendants().OfType<FAContentDialog>().SingleOrDefault();
            Assert.Multiple(() =>
            {
                Assert.That(dialog, Is.Not.Null, "The main window hosts the dialog.");
                Assert.That(floated.Floating.GetVisualDescendants().OfType<FAContentDialog>(), Is.Empty);
                Assert.That(activations, Is.EqualTo(1), "The dialog's window gets the keyboard.");
            });
            HeadlessCapture.SaveIfRequested(floated.Window, nameof(FloatingToolWindowShortcutsTests), $"main-dialog-{kind}");
        }
        finally
        {
            foreach (FAContentDialog open in floated.Window.GetVisualDescendants()
                         .Concat(floated.Floating.GetVisualDescendants()).OfType<FAContentDialog>().ToArray())
                open.Hide();
            HeadlessTestHelpers.Settle();
            await operation;
        }
    }

    // As in the main window, where neither a dialog nor a suspended editor's view passes keys on to these commands.
    [AvaloniaTest]
    public async Task A_dialog_in_a_floating_window_and_a_suspended_editor_keep_the_keys()
    {
        await using Floated floated = await Floated.CreateAsync("floating-shortcuts-held");
        KeyGesture marker = EditorGesture("editor.toggleMarker");

        var button = new Button { Content = "OK" };
        var dialog = new FAContentDialog { Content = button, CloseButtonText = Strings.Close };
        _ = dialog.ShowAsync(floated.Floating);
        HeadlessTestHelpers.Render(2);
        button.Focus();
        floated.Press(marker);
        dialog.Hide();
        HeadlessTestHelpers.Settle();
        Assert.That(floated.Editor.Scene.Markers, Is.Empty, "A dialog");

        floated.Editor.IsEnabled.Value = false;
        try
        {
            floated.Inside.Focus();
            floated.Press(marker);
            Assert.That(floated.Editor.Scene.Markers, Is.Empty, "A suspended editor");
        }
        finally
        {
            floated.Editor.IsEnabled.Value = true;
        }
    }

    // AppKit shows only the application menu for a window without a menu of its own. The window's Edit entries hand their
    // keys to its own focus, here a text box, as the main window's do to the main window's.
    [AvaloniaTest]
    public async Task A_floating_window_on_macOS_has_the_menu_bar_whose_edit_entries_act_on_its_focus()
    {
        await using Floated floated = await Floated.CreateAsync("floating-shortcuts-menu");
        NativeMenu? menu = NativeMenu.GetMenu(floated.Floating);
        if (!OperatingSystem.IsMacOS())
        {
            Assert.That(menu, Is.Null);
            return;
        }

        Assert.That(menu!.Items.OfType<NativeMenuItem>().Select(item => item.Header),
            Is.EqualTo(TestShell.MainViewModel.MenuItems.Where(item => !item.IsInWindowOnly).Select(item => item.Header)));
        var box = new TextBox();
        ((ContentControl)floated.Inside.Parent!).Content = box;
        HeadlessTestHelpers.Render();
        box.Focus();
        floated.Floating.KeyTextInput("hello");
        floated.Editor.ToggleMarkerAtCurrentTime();
        NativeMenuItem undo = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Edit)
            .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == Strings.Undo);

        undo.Command!.Execute(null);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(box.Text ?? "", Is.Empty);
            Assert.That(floated.Editor.Scene.Markers, Has.Count.EqualTo(1));
        });
        floated.Floating.Close();
        HeadlessTestHelpers.Settle();
        Assert.That(menu.Items, Is.Empty, "Closing the window ends its menus.");
    }

    private static KeyGesture MainGesture(string id)
        => GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), id)!;

    private static KeyGesture EditorGesture(string id)
        => GetCommandGestureExtension.GetKeyGesture(typeof(SceneEditorExtension), id)!;

    // The main window with an editor, and the first of its tools floated into a window with the focus in the tool.
    private sealed class Floated : IAsyncDisposable
    {
        private readonly MainView _view;

        private Floated(Window window, MainView view, EditViewModel editor, Window floating, Control inside)
        {
            Window = window;
            _view = view;
            Editor = editor;
            Floating = floating;
            Inside = inside;
        }

        public Window Window { get; }

        public EditViewModel Editor { get; }

        public Window Floating { get; }

        public Control Inside { get; }

        public static async Task<Floated> CreateAsync(string name)
        {
            await TestReset.ResetShellAsync();
            EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(name);
            var view = new MainView { DataContext = TestShell.MainViewModel };
            // As MacWindow and MainWindow are, and the main window that the app's lifetime gives.
            var window = new Window { Content = view, DataContext = TestShell.MainViewModel, Width = 1000, Height = 720 };
            AppHelper.OverrideTopLevel = window;
            window.Show();
            HeadlessTestHelpers.Render(2);
            (Window floating, Control inside) = FloatTool(editor);
            floating.Activate();
            inside.Focus();
            HeadlessTestHelpers.Settle();
            return new Floated(window, view, editor, floating, inside);
        }

        public void Press(KeyGesture gesture)
        {
            Floating.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, PhysicalKey.None, null);
            HeadlessTestHelpers.Settle();
        }

        public async ValueTask DisposeAsync()
        {
            AppHelper.OverrideTopLevel = null;
            if (Floating.IsVisible)
                Floating.Close();
            _view.DataContext = null;
            Window.Close();
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }
}
