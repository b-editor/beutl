using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Inpc;
using Dock.Model.Inpc.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockSplitterRenderingTests
{
    [AvaloniaTest]
    [TestCase(Orientation.Horizontal)]
    [TestCase(Orientation.Vertical)]
    public void An_empty_nested_dock_does_not_draw_a_second_divider(Orientation orientation)
    {
        bool horizontal = orientation == Orientation.Horizontal;
        var factory = new Factory();
        var inspectorTool = new Tool { Title = "Inspector" };
        IToolDock inspector = Pane(inspectorTool, 0.25);
        IProportionalDockSplitter innerDivider = factory.CreateProportionalDockSplitter();
        IProportionalDockSplitter outerDivider = factory.CreateProportionalDockSplitter();
        IProportionalDock inner = Split(orientation, 0.5,
            Pane(new Tool { Title = "Library" }, 0.25), factory.CreateProportionalDockSplitter(),
            Pane(new Tool { Title = "Preview" }, 0.5), innerDivider, inspector);
        IProportionalDock nested = Split(horizontal ? Orientation.Vertical : Orientation.Horizontal, 0.7,
            inner, factory.CreateProportionalDockSplitter(), Pane(new Tool { Title = "Timeline" }, 0.5));
        IProportionalDock split = Split(orientation, 1, nested, outerDivider, Pane(new Tool { Title = "Agent" }, 0.3));
        IRootDock root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList<IDockable>(split);
        root.DefaultDockable = root.ActiveDockable = split;
        factory.InitLayout(root);

        var window = new Window
        {
            Width = horizontal ? 805 : 460,
            Height = horizontal ? 460 : 805,
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = new DockControl { Factory = factory, Layout = root, Margin = new Thickness(2, 0) },
        };
        try
        {
            window.Show();
            window.SetRenderScaling(2);
            HeadlessTestHelpers.Render(3);

            factory.RemoveDockable(inspectorTool, collapse: false);
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(DockSplitterRenderingTests), $"{orientation}-empty");

            ProportionalStackPanelSplitter innerControl = Divider(innerDivider);
            ProportionalStackPanelSplitter outerControl = Divider(outerDivider);
            Rect outerBounds = BoundsInWindow(outerControl);
            Point innerCenter = BoundsInWindow(innerControl).Center;
            // Sample just inside the nested pane, where the empty dock's splitter used to spill out.
            Point paneEdge = horizontal
                ? new Point(outerBounds.X - 0.25, innerCenter.Y)
                : new Point(innerCenter.X, outerBounds.Y - 0.25);

            Assert.That(outerControl.TryFindResource("DockSplitterIdleBrush", ThemeVariant.Dark, out object? brush), Is.True);
            Color color = ((ISolidColorBrush)brush!).Color;
            (int, int, int) idleColor = (color.R, color.G, color.B);
            using (var frame = window.CaptureRenderedFrame()!)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(HeadlessTestHelpers.ReadPixel(frame, PixelAt(outerBounds.Center)), Is.EqualTo(idleColor),
                        "The outer splitter must still paint its divider.");
                    Assert.That(HeadlessTestHelpers.ReadPixel(frame, PixelAt(paneEdge)), Is.Not.EqualTo(idleColor),
                        "The inner splitter must leave the edge of the nested pane clear.");
                });
            }

            window.MouseMove(paneEdge);
            HeadlessTestHelpers.Settle();
            Assert.That(innerControl.IsPointerOver, Is.False);

            Point start = outerBounds.Center;
            var delta = horizontal ? new Vector(28, 0) : new Vector(0, 28);
            window.MouseMove(start);
            HeadlessTestHelpers.Settle();
            Assert.That(outerControl.IsPointerOver, Is.True);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + delta);
            window.MouseUp(start + delta, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
            Point resized = BoundsInWindow(outerControl).Center;
            Assert.That(horizontal ? resized.X - start.X : resized.Y - start.Y, Is.EqualTo(28).Within(1));

            factory.AddDockable(inspector, inspectorTool);
            factory.SetActiveDockable(inspectorTool);
            HeadlessTestHelpers.Render(3);
            window.MouseMove(BoundsInWindow(innerControl).Center);
            HeadlessTestHelpers.Settle();
            Assert.That(innerControl.IsPointerOver, Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        IToolDock Pane(Tool tool, double proportion)
        {
            IToolDock pane = factory.CreateToolDock();
            pane.Proportion = proportion;
            pane.GripMode = GripMode.Hidden;
            pane.VisibleDockables = factory.CreateList<IDockable>(tool);
            pane.ActiveDockable = tool;
            return pane;
        }

        IProportionalDock Split(Orientation direction, double proportion, params IDockable[] children)
        {
            IProportionalDock dock = factory.CreateProportionalDock();
            dock.Orientation = direction;
            dock.Proportion = proportion;
            dock.VisibleDockables = factory.CreateList<IDockable>(children);
            return dock;
        }

        ProportionalStackPanelSplitter Divider(IDockable model) => window.GetVisualDescendants()
            .OfType<ProportionalStackPanelSplitter>().Single(control => ReferenceEquals(control.DataContext, model));
        Rect BoundsInWindow(Control control) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
        PixelPoint PixelAt(Point point) => new((int)(point.X * window.RenderScaling), (int)(point.Y * window.RenderScaling));
    }
}
