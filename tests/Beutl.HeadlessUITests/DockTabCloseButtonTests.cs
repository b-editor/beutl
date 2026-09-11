using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockTabCloseButtonTests
{
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

    private static ToolTabStripItem TabFor(Visual root, IDockable dockable)
    {
        return root.GetVisualDescendants()
            .OfType<ToolTabStripItem>()
            .Single(item => ReferenceEquals(item.DataContext, dockable));
    }

    private static Button CloseButtonOf(ToolTabStripItem tab)
    {
        return tab.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TabCloseButton");
    }

    private static Border TitleOf(ToolTabStripItem tab)
    {
        return tab.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_TabTitleHost");
    }

    private static Point Center(Control control, Visual relativeTo)
    {
        Point? point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            relativeTo);
        Assert.That(point, Is.Not.Null);
        return point!.Value;
    }

    [AvaloniaTest]
    public async Task Close_button_appears_on_hover_without_changing_the_tab_width_and_fades_the_title()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-close-hover");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock leftDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            IDockable first = leftDock.VisibleDockables![0];
            ToolTabStripItem tab = TabFor(view, first);
            Button close = CloseButtonOf(tab);
            Border title = TitleOf(tab);
            double widthBefore = tab.Bounds.Width;

            Assert.Multiple(() =>
            {
                Assert.That(first.CanClose, Is.True);
                Assert.That(close.IsVisible, Is.True);
                Assert.That(close.Opacity, Is.EqualTo(0));
                Assert.That(close.IsHitTestVisible, Is.False);
                Assert.That(title.OpacityMask, Is.Null);
            });

            window.MouseMove(Center(tab, window));
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();

            Point? closeRight = close.TranslatePoint(new Point(close.Bounds.Width, 0), tab);
            Point? titleRight = title.TranslatePoint(new Point(title.Bounds.Width, 0), tab);
            Assert.Multiple(() =>
            {
                Assert.That(close.Opacity, Is.EqualTo(1));
                Assert.That(close.IsHitTestVisible, Is.True);
                Assert.That(close.Bounds.Width, Is.EqualTo(20));
                Assert.That(tab.Bounds.Width, Is.EqualTo(widthBefore));
                Assert.That(closeRight!.Value.X, Is.EqualTo(titleRight!.Value.X).Within(0.5));
                Assert.That(title.OpacityMask, Is.InstanceOf<LinearGradientBrush>());
            });

            window.MouseMove(new Point(1, window.Bounds.Height - 1));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(close.Opacity, Is.EqualTo(0));
                Assert.That(close.IsHitTestVisible, Is.False);
                Assert.That(title.OpacityMask, Is.Null);
            });

            Assert.That(close.Focus(NavigationMethod.Tab), Is.True);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(close.Opacity, Is.EqualTo(1), "Keyboard focus reveals the close button.");
                Assert.That(close.IsHitTestVisible, Is.True);
                Assert.That(title.OpacityMask, Is.InstanceOf<LinearGradientBrush>());
            });

            window.MouseMove(new Point(1, window.Bounds.Height - 1));
            window.MouseDown(new Point(1, window.Bounds.Height - 1), MouseButton.Left);
            window.MouseUp(new Point(1, window.Bounds.Height - 1), MouseButton.Left);
            HeadlessTestHelpers.Settle();

            IToolDock playerDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Player)!;
            IDockable player = playerDock.VisibleDockables!.Single(d => !d.CanClose);
            ToolTabStripItem playerTab = TabFor(view, player);
            window.MouseMove(Center(playerTab, window));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(CloseButtonOf(playerTab).IsVisible, Is.False);
                Assert.That(TitleOf(playerTab).OpacityMask, Is.Null);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Clicking_the_close_button_closes_the_tab()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("dock-tab-close-click");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            IToolDock leftDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
            int countBefore = leftDock.VisibleDockables!.Count;
            Assert.That(countBefore, Is.GreaterThan(1));
            IDockable target = leftDock.VisibleDockables[0];
            ToolTabStripItem tab = TabFor(view, target);
            Button close = CloseButtonOf(tab);

            window.MouseMove(Center(tab, window));
            HeadlessTestHelpers.Settle();
            Point closeCenter = Center(close, window);
            window.MouseMove(closeCenter);
            HeadlessTestHelpers.Settle();
            window.MouseDown(closeCenter, MouseButton.Left);
            window.MouseUp(closeCenter, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(leftDock.VisibleDockables, Has.Count.EqualTo(countBefore - 1));
                Assert.That(leftDock.VisibleDockables, Does.Not.Contain(target));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
