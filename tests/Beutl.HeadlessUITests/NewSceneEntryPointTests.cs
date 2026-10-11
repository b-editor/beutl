using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// File > New > Scene, Ctrl+N and the start screen's New > Scene used to run a command that nothing
// subscribed to, so they did nothing (#2857). Each must run MenuBar.NewScene, the command whose
// handler opens the new scene dialog (Scene > New uses it too). The dialog itself cannot open in
// the headless host: FAContentDialog.ShowAsync() needs an application lifetime to find its window.
[TestFixture]
public class NewSceneEntryPointTests
{
    [AvaloniaTest]
    public async Task Ctrl_N_and_the_command_palette_run_the_new_scene_command()
    {
        MenuBarViewModel menuBar = TestShell.MainViewModel.MenuBar;
        int runs = 0;
        using IDisposable counter = menuBar.NewScene.Subscribe(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        // Ctrl+N (Cmd+N on macOS) and the palette's "Create new scene" entry run this context command
        await TestShell.MainViewModel.ExecuteAsync(new ContextCommandExecution("CreateNewFile"));

        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.EqualTo(1));
            Assert.That(menuBar.FindContextCommand("CreateNewFile"), Is.SameAs(menuBar.NewScene));
            // The context command already lists "Create new scene" with its shortcut
            Assert.That(
                menuBar.EnumeratePaletteCommands().Select(command => command.Command),
                Has.None.SameAs(menuBar.NewScene));
        });
    }

    [AvaloniaTest]
    public async Task Menus_and_the_start_screen_run_the_new_scene_command()
    {
        await TestReset.ResetShellAsync();
        MainViewModel mainViewModel = TestShell.MainViewModel;
        MenuBarViewModel menuBar = mainViewModel.MenuBar;
        var window = new Window { Width = 1280, Height = 720 };
        // Open the test host before attaching MainView, so it doesn't run the real App startup.
        window.Show();
        var view = new MainView { DataContext = mainViewModel };
        var macWindow = new MacWindow { DataContext = mainViewModel };
        try
        {
            window.Content = view;
            HeadlessTestHelpers.Render();
            int runs = 0;
            using IDisposable counter = menuBar.NewScene.Subscribe(() =>
            {
                runs++;
                return Task.CompletedTask;
            });

            MenuItem fileNewScene = view.GetLogicalDescendants().OfType<MenuItem>()
                .Single(item => Equals(item.Header, Strings.CreateNewScene));
            NativeMenu macFileMenu = MacWindow.FindMenuItem(NativeMenu.GetMenu(macWindow), Strings.File)!.Menu!;
            NativeMenu macNewMenu = MacWindow.FindMenuItem(macFileMenu, Strings.CreateNew)!.Menu!;
            NativeMenuItem macNewScene = MacWindow.FindMenuItem(macNewMenu, Strings.CreateNewScene)!;

            // The start screen catches the dialog's failure to open in the headless host
            EditorHostFallback startScreen = view.GetVisualDescendants().OfType<EditorHostFallback>().Single();
            FAMenuFlyoutItem startNewScene = ((FAMenuFlyout)startScreen
                    .FindControl<OptionsDisplayItem>("createNewButton")!.ContextFlyout!)
                .Items.OfType<FAMenuFlyoutItem>()
                .Single(item => item.Text == Strings.CreateNewScene);
            startNewScene.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(fileNewScene.Command, Is.SameAs(menuBar.NewScene), "File > New > Scene");
                Assert.That(macNewScene.Command, Is.SameAs(menuBar.NewScene), "macOS File > New > Scene");
                Assert.That(runs, Is.EqualTo(1), "Start screen New > Scene");
            });
        }
        finally
        {
            macWindow.DataContext = null;
            macWindow.Close();
            view.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }
}
