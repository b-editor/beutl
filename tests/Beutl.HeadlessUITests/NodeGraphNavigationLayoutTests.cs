using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NodeGraphNavigationLayoutTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(320, true)]
    [TestCase(640, false)]
    [TestCase(640, true)]
    public async Task Navigation_does_not_overlap_the_graph(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"node-navigation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "node-navigation", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var graph = new GraphModel();
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = graph;
        var element = new Element { Name = "Composition" };
        element.AddObject(drawable);
        var group = new GroupNode { Position = (0, 0) };
        graph.Nodes.Add(group);
        using var model = new NodeGraphTabViewModel(editor);
        model.Model.Value = graph;
        var view = new NodeGraphTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 420,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            NodeGraphView graphView = view.GetVisualDescendants().OfType<NodeGraphView>().Single();
            FABreadcrumbBar breadcrumb = view.GetVisualDescendants().OfType<FABreadcrumbBar>().Single();
            GraphNodeView node = graphView.GetVisualDescendants().OfType<GraphNodeView>().Single();
            Assert.That(node.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == NodeGraphStrings.Group), Is.True);
            Assert.That(ToolTip.GetTip(node.FindControl<Border>("handle")!), Is.EqualTo(NodeGraphStrings.Group));
            Capture("origin");
            string longNodeName = string.Concat(Enumerable.Repeat("Long node name ", 8));
            group.Name = longNodeName;
            HeadlessTestHelpers.Render(3);
            Assert.That(ToolTip.GetTip(node.FindControl<Border>("handle")!), Is.EqualTo(longNodeName));
            Capture("long-node-name");
            group.Name = "";
            HeadlessTestHelpers.Render(3);
            double breadcrumbBottom = breadcrumb.TranslatePoint(new Point(0, breadcrumb.Bounds.Height), view)!.Value.Y;
            double graphTop = graphView.TranslatePoint(default, view)!.Value.Y;
            double nodeTop = node.TranslatePoint(default, view)!.Value.Y;
            TestContext.WriteLine($"Breadcrumb bottom={breadcrumbBottom}, graph top={graphTop}, node top={nodeTop}");
            Assert.That(graphTop, Is.GreaterThanOrEqualTo(breadcrumbBottom));
            Assert.That(nodeTop, Is.GreaterThanOrEqualTo(breadcrumbBottom));

            Border navigation = view.FindControl<Border>("NavigationBar")!;
            ZoomBorder zoom = graphView.FindControl<ZoomBorder>("zoomBorder")!;
            zoom.EnableAnimations = false;
            double navigationHeight = navigation.Bounds.Height;
            Assert.That(graphTop, Is.EqualTo(navigation.Bounds.Bottom));

            // Moving a node still uses canvas coordinates after shifting the viewport down.
            Border handle = node.FindControl<Border>("handle")!;
            Point handlePoint = handle.TranslatePoint(new Point(95, 16), window)!.Value;
            Drag(handlePoint, new Vector(30, 20));
            Assert.That(group.Position, Is.EqualTo((30d, 20d)));

            // Camera movement and zoom belong to the graph, not to the navigation band.
            Point emptyPoint = zoom.TranslatePoint(new Point(width - 40, 180), window)!.Value;
            Matrix beforePan = zoom.Matrix;
            Drag(emptyPoint, new Vector(-20, -10));
            Assert.That(zoom.Matrix, Is.Not.EqualTo(beforePan));
            Matrix beforeWheel = zoom.Matrix;
            window.MouseWheel(navigation.TranslatePoint(new Point(100, 10), window)!.Value, new Vector(0, 1));
            HeadlessTestHelpers.Render();
            Assert.That(zoom.Matrix, Is.EqualTo(beforeWheel));
            window.MouseWheel(emptyPoint, new Vector(0, 1));
            HeadlessTestHelpers.Render();
            Assert.That(zoom.Matrix.M11, Is.GreaterThan(beforeWheel.M11));
            zoom.Pan(0, -30, true);
            HeadlessTestHelpers.Render();
            Assert.That(navigation.Bounds.Height, Is.EqualTo(navigationHeight));
            Capture("panned");

            zoom.SetMatrix(Matrix.Identity, true);
            HeadlessTestHelpers.Render();
            Button open = node.GetVisualDescendants().OfType<Button>()
                .Single(x => x.Name != "expandToggle");
            Click(open);
            Assert.That(model.NodeGraph.Value!.NodeGraph, Is.SameAs(group.Group));
            Assert.That(model.Items, Has.Count.EqualTo(2));
            Capture("inside-group");
            FABreadcrumbBarItem parent = breadcrumb.GetVisualDescendants().OfType<FABreadcrumbBarItem>()
                .First(x => ReferenceEquals(x.Content, model.Items[0]));
            Click(parent);
            Assert.That(model.NodeGraph.Value!.NodeGraph, Is.SameAs(graph));
            Assert.That(model.Items, Has.Count.EqualTo(1));

            element.Name = string.Concat(Enumerable.Repeat("Long composition name ", 12));
            HeadlessTestHelpers.Render(3);
            Assert.That(navigation.Bounds.Height, Is.EqualTo(navigationHeight));
            TextBlock name = breadcrumb.GetVisualDescendants().OfType<TextBlock>().First(x => x.Text == element.Name);
            Assert.That(ToolTip.GetTip(name), Is.EqualTo(element.Name));
            Assert.That(name.Bounds.Width, Is.LessThanOrEqualTo(breadcrumb.Bounds.Width));
            Assert.That(breadcrumb.Bounds.Right, Is.LessThanOrEqualTo(navigation.Bounds.Width));
            Capture("long-name");

            model.Model.Value = null;
            HeadlessTestHelpers.Render(3);
            Assert.That(navigation.IsVisible, Is.False);
            Assert.That(graphView.TranslatePoint(default, view)!.Value.Y, Is.Zero);
            model.Model.Value = graph;
            window.Height = 180;
            HeadlessTestHelpers.Render(3);
            Assert.That(navigation.IsVisible, Is.True);
            Assert.That(graphView.Bounds.Height, Is.EqualTo(window.ClientSize.Height - navigation.Bounds.Height));

            void Click(Control control)
            {
                Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render(3);
            }

            void Drag(Point from, Vector delta)
            {
                Point to = from + delta;
                window.MouseMove(from);
                window.MouseDown(from, MouseButton.Left);
                window.MouseMove(to, RawInputModifiers.LeftMouseButton);
                window.MouseUp(to, MouseButton.Left);
                HeadlessTestHelpers.Render(3);
            }
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_NODE_NAV_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
