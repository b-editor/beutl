using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Guards #2857: every New Scene entry point runs MenuBar.NewScene, and only while a project is open. The dialog itself
// cannot open in the headless host, because FAContentDialog.ShowAsync() needs an application lifetime to find its window.
[TestFixture]
public class NewSceneEntryPointTests
{
    [AvaloniaTest]
    public async Task Ctrl_N_and_the_command_palette_run_the_new_scene_command()
    {
        await TestReset.ResetShellAsync();
        await TestShell.CreateProjectAsync("new-scene-gesture");
        MenuBarViewModel menuBar = TestShell.MainViewModel.MenuBar;
        int runs = 0;
        using IDisposable counter = menuBar.NewScene.Subscribe(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        try
        {
            // Ctrl+N (Cmd+N on macOS) and the palette's "Create new scene" entry run this context command
            await MainViewExtension.Instance.RunAsync(TestShell.MainViewModel, new ContextCommandExecution("main.createNewFile"));

            Assert.Multiple(() =>
            {
                Assert.That(runs, Is.EqualTo(1));
                // The context command already lists "Create new scene" with its shortcut
                Assert.That(
                    menuBar.EnumeratePaletteCommands().Select(command => command.Command),
                    Has.None.SameAs(menuBar.NewScene));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Without_a_project_no_entry_point_runs_the_new_scene_command()
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

            await MainViewExtension.Instance.RunAsync(mainViewModel, new ContextCommandExecution("main.createNewFile"));

            MenuItem fileNewScene = view.GetLogicalDescendants().OfType<MenuItem>()
                .Single(item => Equals(item.Header, Strings.CreateNewScene));
            NativeMenu macFileMenu = FindMenuItem(NativeMenu.GetMenu(macWindow)!, Strings.File).Menu!;
            NativeMenu macNewMenu = FindMenuItem(macFileMenu, Strings.CreateNew).Menu!;
            NativeMenuItem macNewScene = FindMenuItem(macNewMenu, Strings.CreateNewScene);
            TitleBreadcrumbBar breadcrumb = view.GetVisualDescendants().OfType<TitleBreadcrumbBar>().Single();
            var fileFlyout = (Flyout)breadcrumb.FindControl<Button>("FileButton")!.Flyout!;
            fileFlyout.ShowAt(breadcrumb);
            HeadlessTestHelpers.Render();
            Button breadcrumbNewScene = ((Control)fileFlyout.Content!).GetVisualDescendants().OfType<Button>()
                .Concat(((Control)fileFlyout.Content!).GetLogicalDescendants().OfType<Button>())
                .First(button => Equals(button.Tag, "NewScene"));

            Assert.Multiple(() =>
            {
                Assert.That(runs, Is.Zero, "Ctrl+N and the command palette");
                Assert.That(((ICommand)menuBar.NewScene).CanExecute(null), Is.False);
                Assert.That(fileNewScene.Command, Is.SameAs(menuBar.NewScene), "File > New > Scene follows the command");
                Assert.That(macNewScene.Command, Is.SameAs(menuBar.NewScene), "macOS File > New > Scene follows the command");
                Assert.That(breadcrumbNewScene.IsEnabled, Is.False, "Title bar");
            });
            fileFlyout.Hide();
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

    [AvaloniaTest]
    public async Task The_dialog_cannot_be_built_without_a_project()
    {
        await TestReset.ResetShellAsync();

        Assert.Throws<InvalidOperationException>(
            () => new CreateNewSceneViewModel(TestShell.Project, TestShell.Editor));
    }

    [AvaloniaTest]
    public async Task The_menus_run_the_new_scene_command()
    {
        await TestReset.ResetShellAsync();
        await TestShell.CreateProjectAsync("new-scene-menus");
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

            MenuItem fileNewScene = view.GetLogicalDescendants().OfType<MenuItem>()
                .Single(item => Equals(item.Header, Strings.CreateNewScene));
            NativeMenu macFileMenu = FindMenuItem(NativeMenu.GetMenu(macWindow)!, Strings.File).Menu!;
            NativeMenu macNewMenu = FindMenuItem(macFileMenu, Strings.CreateNew).Menu!;
            NativeMenuItem macNewScene = FindMenuItem(macNewMenu, Strings.CreateNewScene);

            Assert.Multiple(() =>
            {
                Assert.That(fileNewScene.Command, Is.SameAs(menuBar.NewScene), "File > New > Scene");
                Assert.That(macNewScene.Command, Is.SameAs(menuBar.NewScene), "macOS File > New > Scene");
                Assert.That(((ICommand)menuBar.NewScene).CanExecute(null), Is.True, "A project is open.");
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

    private static NativeMenuItem FindMenuItem(NativeMenu menu, string header)
        => menu.Items.OfType<NativeMenuItem>().First(item => item.Header == header);
}
