using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ColorScopesTab;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Beutl.Views.Dock;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockTabAddButtonTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task Add_button_is_available_on_every_dock_and_only_visible_while_its_header_is_hovered()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-hover");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ToolControl[] controls = view.GetVisualDescendants()
                .OfType<ToolControl>()
                .ToArray();
            Assert.That(controls, Is.Not.Empty);
            Assert.That(
                controls.All(control => FindAddButton(control) is not null),
                Is.True,
                "Every rendered dock tab strip should expose an add button.");

            IToolDock playerDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Player)!;
            ToolControl playerControl = controls.Single(control => ReferenceEquals(control.DataContext, playerDock));
            ToolTabAddButton button = FindAddButton(playerControl)!;
            Grid header = playerControl.GetVisualDescendants()
                .OfType<Grid>()
                .Single(control => control.Name == "PART_TabHeader");

            Assert.Multiple(() =>
            {
                Assert.That(button.Opacity, Is.EqualTo(0));
                Assert.That(button.IsHitTestVisible, Is.False);
            });

            window.MouseMove(Center(header, window));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(button.Opacity, Is.EqualTo(1));
                Assert.That(button.IsHitTestVisible, Is.True);
            });

            window.MouseMove(new Point(1, window.Bounds.Height - 1));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(button.Opacity, Is.EqualTo(0));
                Assert.That(button.IsHitTestVisible, Is.False);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Add_menu_disables_open_singletons_and_opens_the_selected_tool_in_its_dock()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-add-menu");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock target = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            ToolControl targetControl = view.GetVisualDescendants()
                .OfType<ToolControl>()
                .Single(control => ReferenceEquals(control.DataContext, target));
            ToolTabAddButton button = FindAddButton(targetControl)!;
            ContextMenu menu = button.CreateContextMenu()!;
            MenuItem[] items = menu.ItemsSource!.Cast<MenuItem>().ToArray();

            MenuItem timelineItem = items.Single(
                item => ReferenceEquals(item.DataContext, TimelineTabExtension.Instance));
            MenuItem colorScopesItem = items.Single(
                item => ReferenceEquals(item.DataContext, ColorScopesTabExtension.Instance));

            Assert.Multiple(() =>
            {
                Assert.That(timelineItem.IsEnabled, Is.False);
                Assert.That(colorScopesItem.IsEnabled, Is.True);
            });

            colorScopesItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            HeadlessTestHelpers.Settle();

            BeutlToolDockable? added = target.VisibleDockables?
                .OfType<BeutlToolDockable>()
                .SingleOrDefault(dockable => dockable.ToolContext.Extension == ColorScopesTabExtension.Instance);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.Not.Null);
                Assert.That(target.ActiveDockable, Is.SameAs(added));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static ToolTabAddButton? FindAddButton(Visual root)
    {
        return root.GetVisualDescendants().OfType<ToolTabAddButton>().SingleOrDefault();
    }

    private static Point Center(Control control, Visual relativeTo)
    {
        Point? point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);
        Assert.That(point, Is.Not.Null);
        return point!.Value;
    }
}
