using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Model.Controls;
using Dock.Model.Core;

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
                .Single(item => Equals(item.Header, Strings.CreateSceneMenu));
            NativeMenu macFileMenu = FindMenuItem(NativeMenu.GetMenu(macWindow)!, Strings.File).Menu!;
            NativeMenu macNewMenu = FindMenuItem(macFileMenu, Strings.CreateNew).Menu!;
            NativeMenuItem macNewScene = FindMenuItem(macNewMenu, Strings.CreateSceneMenu);
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
                .Single(item => Equals(item.Header, Strings.CreateSceneMenu));
            NativeMenu macFileMenu = FindMenuItem(NativeMenu.GetMenu(macWindow)!, Strings.File).Menu!;
            NativeMenu macNewMenu = FindMenuItem(macFileMenu, Strings.CreateNew).Menu!;
            NativeMenuItem macNewScene = FindMenuItem(macNewMenu, Strings.CreateSceneMenu);

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

    [AvaloniaTest]
    public async Task New_scene_dialog_persists_frame_rate_sample_rate_and_channel_layout()
    {
        await TestReset.ResetShellAsync();
        try
        {
            Project project = await TestShell.CreateProjectAsync("new-scene-rates");
            var vm = new CreateNewSceneViewModel(TestShell.Project, TestShell.Editor);
            vm.Name.Value = "CustomRates";
            vm.FrameRate.Value = "29.97";
            vm.SampleRate.Value = 48000;
            vm.ChannelLayout.Value = SpeakerLayout.Surround51;
            HeadlessTestHelpers.Settle();
            Assert.That(vm.CanCreate.Value, Is.True);

            await vm.Create.ExecuteAsync();
            HeadlessTestHelpers.Settle();

            Scene created = project.Items.OfType<Scene>().Single(s => s.Name.Value == "CustomRates");
            System.Text.Json.Nodes.JsonNode file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(created.Uri!.LocalPath))!;
            Assert.Multiple(() =>
            {
                Assert.That(created.FrameRate.Value, Is.EqualTo(new Rational(30000, 1001)));
                Assert.That((string?)file["FrameRate"], Is.EqualTo("30000/1001"));
                Assert.That(created.SampleRate.Value, Is.EqualTo(48000));
                Assert.That((int?)file["SampleRate"], Is.EqualTo(48000));
                Assert.That(created.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
                Assert.That((int?)file["ChannelLayout"], Is.EqualTo((int)SpeakerLayout.Surround51));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(640, 360, 800, 600, true, false)]
    [TestCase(360, 640, 480, 720, true, false)]
    [TestCase(640, 360, 360, 640, false, false)]
    [TestCase(360, 640, 640, 360, false, false)]
    [TestCase(640, 360, 480, 480, true, false)]
    [TestCase(640, 360, 800, 450, true, true)]
    public async Task New_scene_inherits_the_current_dock_layout_for_the_same_orientation(
        int width, int height, int newWidth, int newHeight, bool inheritsLayout, bool previewOnly)
    {
        (_, _, EditViewModel source) = await TestShell.OpenNewSceneAsync("new-scene-layout", width, height);
        try
        {
            var tabsToClose = source.DockHost.Factory.EnumerateTools()
                .Where(tool => previewOnly || tool.ToolContext.Extension is LibraryTabExtension)
                .ToArray();
            foreach (var tool in tabsToClose)
            {
                source.DockHost.CloseToolTab(tool.ToolContext);
            }
            var sourceLeft = source.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            sourceLeft.Proportion = sourceLeft.CollapsedProportion = 0.35;
            string captureName = $"{width}x{height}-to-{newWidth}x{newHeight}";
            CaptureDockLayout(source, $"{captureName}-source");
            // Rendering normalizes the proportions within each split.
            double sourceProportion = sourceLeft.Proportion;

            var dialog = new CreateNewSceneViewModel(TestShell.Project, TestShell.Editor);
            dialog.Name.Value = "AddedScene";
            dialog.Size.Value = new PixelSize(newWidth, newHeight);
            await dialog.Create.ExecuteAsync();
            HeadlessTestHelpers.Settle();

            var added = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.That(added.Scene.Name.Value, Is.EqualTo("AddedScene"));
            var addedLeft = added.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            var rootSplit = BeutlDockFactory.Traverse(added.DockHost.Layout.Value).OfType<IProportionalDock>()
                .Single(dock => dock.Id == DockIds.RootSplit);
            var player = BeutlDockFactory.Traverse(added.DockHost.Layout.Value).OfType<PlayerToolDockable>().Single();
            var addedTools = added.DockHost.Factory.EnumerateTools().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(addedTools.Any(tool => tool.ToolContext.Extension is LibraryTabExtension), Is.EqualTo(!inheritsLayout));
                if (previewOnly)
                {
                    Assert.That(addedTools, Is.Empty);
                }
                else
                {
                    Assert.That(addedLeft.Proportion, Is.EqualTo(inheritsLayout ? sourceProportion : 0.25));
                }
                Assert.That(rootSplit.Orientation, Is.EqualTo(newHeight > newWidth ? Orientation.Horizontal : Orientation.Vertical));
                Assert.That(addedLeft, Is.Not.SameAs(sourceLeft));
                Assert.That(player.Player, Is.SameAs(added.Player));
            });
            CaptureDockLayout(added, $"{captureName}-added");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static void CaptureDockLayout(EditViewModel editor, string name)
    {
        if (!HeadlessCapture.IsRequested) return;

        var view = new EditorHostView { DataContext = editor };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            HeadlessCapture.SaveIfRequested(window, nameof(NewSceneEntryPointTests), name);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static NativeMenuItem FindMenuItem(NativeMenu menu, string header)
        => menu.Items.OfType<NativeMenuItem>().First(item => item.Header == header);
}
