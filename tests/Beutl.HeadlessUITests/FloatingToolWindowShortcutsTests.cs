using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Language;
using Beutl.ProjectSystem;
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
            BeutlDockFactory factory = editor.DockHost.Factory;
            BeutlToolDockable tool = factory.EnumerateTools().First();
            factory.SplitToWindow((IDock)tool.Owner!, tool, 20, 20, 500, 400, null);
            HeadlessTestHelpers.Render(2);
            var floating = (Window)factory.FindRoot(tool, _ => true)!.Window!.Host!;
            var inside = (Control)floating.GetVisualDescendants().OfType<ToolTabContent>()
                .Single(content => ReferenceEquals(content.DataContext, tool)).Content!;
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
