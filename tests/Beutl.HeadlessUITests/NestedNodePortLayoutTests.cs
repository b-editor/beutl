using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NestedNodePortLayoutTests
{
    [AvaloniaTest]
    [TestCase(100)]
    [TestCase(800)]
    public void LargeRealizedListKeepsConnectedPortPositionsDuringMovement(int itemCount)
    {
        var graph = new GraphModel();
        var node = new FactoryNode<TransformGroup>();
        node.Object.Children.AddRange(Enumerable.Range(0, itemCount).Select(i => (Transform)new TranslateTransform(i, 0)));
        graph.Nodes.Add(node);
        var port = node.NestedInputPorts.Last();
        graph.Connect(port, AddSource(graph, 42f));
        using var vm = CreateViewModel(graph);
        GraphNodeViewModel nodeVm = vm.Nodes.Single(n => n.GraphNode == node);
        var view = new GraphNodeView { DataContext = nodeVm };
        var panel = view.FindControl<StackPanel>("stackPanel")!;
        var rows = nodeVm.NestedItems.Select(member => CreateRow(member)).ToArray();
        foreach (var row in rows) panel.Children.Add(row);
        var window = new Window { Content = new ScrollViewer { Content = view }, Width = 320, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(view.GetVisualDescendants().OfType<NodePortView>().Count(), Is.GreaterThanOrEqualTo(itemCount * 2));
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) view.UpdateNodePortPosition();
            watch.Stop();
            TestContext.WriteLine($"PORT_LAYOUT items={itemCount} ports={rows.Length} meanMs={watch.Elapsed.TotalMilliseconds / 10:F2}");

            var connection = vm.AllConnections.Single();
            Point position = rows[^1].GetPortPosition()!.Value + nodeVm.Position.Value;
            Assert.That(connection.InputPortPosition.Value, Is.EqualTo(position));
            nodeVm.Position.Value += new Point(13, 17);
            Assert.That(connection.InputPortPosition.Value, Is.EqualTo(position + new Point(13, 17)));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            foreach (var row in rows) row.DataContext = null;
        }
    }

    [AvaloniaTest]
    public void DeferredPortsUseTheirDeepestVisibleAncestorAndKeepSeparateRootAnchors()
    {
        var graph = new GraphModel();
        var node = new GeometryShapeNode { Position = (20, 30) };
        var pen = new Pen();
        var brush = new SolidColorBrush();
        var fill = new SolidColorBrush();
        pen.Brush.CurrentValue = brush;
        node.Pen.Property!.SetValue(pen);
        node.Fill.Property!.SetValue(fill);
        graph.Nodes.Add(node);
        var color = AddSource(graph, Colors.Red);
        var penColor = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Color));
        var fillColor = node.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), fill.Color));
        var penConnection = graph.Connect(penColor, color);
        var fillConnection = graph.Connect(fillColor, color);
        using var vm = CreateViewModel(graph);
        GraphNodeViewModel nodeVm = vm.Nodes.Single(n => n.GraphNode == node);
        var view = new GraphNodeView { DataContext = nodeVm };
        var panel = view.FindControl<StackPanel>("stackPanel")!;
        // Only these parent/sibling rows are realized; the connected color editors are deferred.
        var brushRow = CreateRow(nodeVm.NestedItems.Single(p => ReferenceEquals(p.Model!.Property!.GetEngineProperty(), pen.Brush)));
        var siblingRow = CreateRow(nodeVm.NestedItems.Single(p => ReferenceEquals(p.Model!.Property!.GetEngineProperty(), pen.Thickness)));
        var otherRootRow = CreateRow(nodeVm.NestedItems.Single(p => ReferenceEquals(p.Model!.Property!.GetEngineProperty(), fill.Opacity)));
        panel.Children.Add(brushRow);
        panel.Children.Add(siblingRow);
        panel.Children.Add(otherRootRow);
        var window = new Window { Content = view, Width = 320, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var rootRows = view.GetVisualDescendants().OfType<NodePortView>().ToArray();
            NodePortView penRow = rootRows.Single(row => row.DataContext is NodeMemberViewModel m && m.Model == node.Pen);
            NodePortView fillRow = rootRows.Single(row => row.DataContext is NodeMemberViewModel m && m.Model == node.Fill);
            var penLink = vm.AllConnections.Single(c => c.Connection == penConnection);
            var fillLink = vm.AllConnections.Single(c => c.Connection == fillConnection);
            Assert.That(penLink.InputPortPosition.Value, Is.EqualTo(brushRow.GetPortPosition()!.Value + nodeVm.Position.Value));
            Assert.That(fillLink.InputPortPosition.Value, Is.EqualTo(fillRow.GetPortPosition()!.Value + nodeVm.Position.Value));

            brushRow.IsVisible = false;
            HeadlessTestHelpers.Render(3);
            Assert.That(penLink.InputPortPosition.Value, Is.EqualTo(penRow.GetPortPosition()!.Value + nodeVm.Position.Value));
            Assert.That(fillLink.InputPortPosition.Value, Is.EqualTo(fillRow.GetPortPosition()!.Value + nodeVm.Position.Value));

            brushRow.IsVisible = true;
            nodeVm.Position.Value += new Point(15, 25);
            HeadlessTestHelpers.Render(3);
            Assert.That(penLink.InputPortPosition.Value, Is.EqualTo(brushRow.GetPortPosition()!.Value + nodeVm.Position.Value));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            brushRow.DataContext = null;
            siblingRow.DataContext = null;
            otherRootRow.DataContext = null;
        }
    }

    private static NodePortView CreateRow(InputPortViewModel member)
        => new() { ProvidedEditor = new Border { Height = 18 }, DataContext = member };

    private static NodeGraphViewModel CreateViewModel(GraphModel graph)
    {
        var services = new Mock<IEditorContext>();
        services.Setup(s => s.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        return new NodeGraphViewModel(graph, services.Object);
    }

    private static IOutputPort AddSource<T>(GraphModel graph, T value)
    {
        var source = new LayerInputNode();
        var output = new LayerInputNode.LayerInputPort<T>();
        output.SetupProperty("Value");
        output.Property!.SetValue(value);
        source.Items.Add(output);
        graph.Nodes.Add(source);
        return output;
    }
}
