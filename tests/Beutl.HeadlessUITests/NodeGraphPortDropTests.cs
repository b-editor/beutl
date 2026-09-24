using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Editor;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NodeGraphPortDropTests
{
    [AvaloniaTest]
    [TestCase(1d)]
    [TestCase(1.5d)]
    public async Task OutputDroppedOnEmptyGraph_OffersCompatibleNodesAndConnectsSelectedNode(double scale)
    {
        var graph = new GraphModel();
        var source = new FilterEffectInputNode { Position = (20, 40) };
        graph.Nodes.Add(source);
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(graph, sequence);
        using var observer = new CoreObjectOperationObserver(null, graph, sequence);
        history.Subscribe(observer);
        var service = new NodeGraphMutationService(history);
        using var vm = CreateViewModel(graph, service);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 550 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var zoom = view.FindControl<ZoomBorder>("zoomBorder")!;
            zoom.EnableAnimations = false;
            zoom.SetMatrix(new Matrix(scale, 0, 0, scale, 0, 0), true);
            HeadlessTestHelpers.Render(3);
            var socket = view.GetVisualDescendants().OfType<NodePortPoint>()
                .Single(p => p.DataContext is OutputPortViewModel output && output.Model == source.Output);
            Point from = socket.TranslatePoint(new Point(5, 5), window)!.Value;
            Point to = new(440, 210);
            var canvas = view.FindControl<Canvas>("canvas")!;
            Point graphPoint = window.TranslatePoint(to, canvas)!.Value;

            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            HeadlessTestHelpers.Render(3);

            ContextMenu menu = view.PortDropMenu!;
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(graph.Nodes, Has.Count.EqualTo(1));
            MenuItem utilities = menu.ItemsSource!.Cast<MenuItem>()
                .Single(item => Equals(item.Header, NodeGraphStrings.ToolsAndValues));
            MenuItem selector = utilities.ItemsSource!.Cast<MenuItem>()
                .Single(item => Equals(item.Header, NodeGraphStrings.ConditionalSwitch));
            Assert.That(selector.ItemsSource!.Cast<MenuItem>().Select(item => item.Header),
                Is.EquivalentTo(new[] { "True", "False" }));
            MenuItem outputItem = menu.ItemsSource!.Cast<MenuItem>()
                .Single(item => Equals(item.Header, GraphNodeRegistry.FindItem(typeof(OutputNode))!.DisplayName));
            TopLevel popup = TopLevel.GetTopLevel(menu)!;
            Capture(popup, $"compatible-menu-{scale}");
            Point action = outputItem.TranslatePoint(
                new Point(outputItem.Bounds.Width / 2, outputItem.Bounds.Height / 2), popup)!.Value;
            popup.MouseMove(action);
            popup.MouseDown(action, MouseButton.Left);
            popup.MouseUp(action, MouseButton.Left);
            HeadlessTestHelpers.Render(3);

            OutputNode added = graph.Nodes.OfType<OutputNode>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(added.Position.X, Is.EqualTo(graphPoint.X - 215 / 2d).Within(1));
                Assert.That(added.Position.Y, Is.EqualTo(graphPoint.Y).Within(1));
                Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
                Assert.That(graph.AllConnections[0].Output.Value, Is.SameAs(source.Output));
                Assert.That(graph.AllConnections[0].Input.Value, Is.SameAs(added.InputPort));
                Assert.That(history.UndoCount, Is.EqualTo(1));
            });
            await Task.Delay(300);
            HeadlessTestHelpers.Render(3);
            Capture(window, $"connected-node-{scale}");

            history.Undo();
            Assert.That(graph.Nodes, Has.Count.EqualTo(1));
            Assert.That(graph.AllConnections, Is.Empty);

            GraphNodeView sourceView = view.GetVisualDescendants().OfType<GraphNodeView>().Single();
            Point body = sourceView.TranslatePoint(new Point(100, 55), window)!.Value;
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(body, RawInputModifiers.LeftMouseButton);
            window.MouseUp(body, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
            Assert.That(view.PortDropMenu?.IsOpen, Is.Not.True,
                "Dropping on a node body should cancel the wire without opening suggestions.");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task InputDroppedOnEmptyGraph_OffersDynamicSourceAndConnectsIt()
    {
        var graph = new GraphModel();
        var target = new RandomSingleNode { Position = (20, 40) };
        graph.Nodes.Add(target);
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(graph, sequence);
        using var observer = new CoreObjectOperationObserver(null, graph, sequence);
        history.Subscribe(observer);
        var service = new NodeGraphMutationService(history);
        using var vm = CreateViewModel(graph, service);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 550 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var socket = view.GetVisualDescendants().OfType<NodePortPoint>()
                .Single(p => p.DataContext is InputPortViewModel input && input.Model == target.Maximum);
            Point from = socket.TranslatePoint(new Point(5, 5), window)!.Value;
            Point to = new(440, 210);
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            HeadlessTestHelpers.Render(3);

            ContextMenu menu = view.PortDropMenu!;
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(menu.ItemsSource!.Cast<MenuItem>().Any(item =>
                Equals(item.Header, GraphNodeRegistry.FindItem(typeof(OutputNode))!.DisplayName)), Is.False);
            MenuItem inputItem = menu.ItemsSource!.Cast<MenuItem>()
                .Single(item => Equals(item.Header, GraphNodeRegistry.FindItem(typeof(LayerInputNode))!.DisplayName));
            TopLevel popup = TopLevel.GetTopLevel(menu)!;
            Capture(popup, "compatible-input-menu");
            Point action = inputItem.TranslatePoint(
                new Point(inputItem.Bounds.Width / 2, inputItem.Bounds.Height / 2), popup)!.Value;
            popup.MouseMove(action);
            popup.MouseDown(action, MouseButton.Left);
            popup.MouseUp(action, MouseButton.Left);
            HeadlessTestHelpers.Render(3);

            LayerInputNode added = graph.Nodes.OfType<LayerInputNode>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(added.Items, Has.Count.EqualTo(1));
                Assert.That(graph.AllConnections, Has.Count.EqualTo(1));
                Assert.That(graph.AllConnections[0].Input.Value, Is.SameAs(target.Maximum));
                Assert.That(graph.AllConnections[0].Output.Value, Is.SameAs(added.Items[0]));
                Assert.That(history.UndoCount, Is.EqualTo(1));
            });
            await Task.Delay(300);
            HeadlessTestHelpers.Render(3);
            Capture(window, "connected-input-node");

            history.Undo();
            Assert.That(graph.Nodes, Has.Count.EqualTo(1));
            Assert.That(graph.AllConnections, Is.Empty);
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    private static NodeGraphViewModel CreateViewModel(GraphModel graph, INodeGraphMutationService mutation)
    {
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        editor.Setup(x => x.GetService(typeof(INodeGraphMutationService))).Returns(mutation);
        return new NodeGraphViewModel(graph, editor.Object);
    }

    private static void Capture(TopLevel topLevel, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_PORT_DROP_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = topLevel.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
