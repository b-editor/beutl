using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
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
using Beutl.Serialization;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

public class NodeGraphFallbackDisplayTests
{
    private const string MissingType = "[Missing.Plugin]Missing:Node";

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DoubleClickOnPortDisconnectsTheIntendedConnectionAndCanBeUndone(bool list, bool healthy)
    {
        GraphModel graph = RestoreGraph(failed: false, list: list);
        Connection connection = healthy ? graph.AllConnections.Last() : graph.AllConnections[0];
        Connection[] original = [.. graph.AllConnections];
        Connection[] surviving = [.. original.Where(candidate => candidate != connection)];
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(sequence);
        using var observer = new CoreObjectOperationObserver(null, graph, sequence);
        history.Subscribe(observer);
        using var vm = new NodeGraphViewModel(graph,
            NodeGraphTestContext.Create(new NodeGraphMutationService(history), player: true).Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 320, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var points = view.GetVisualDescendants().OfType<NodePortPoint>();
            var port = list ? points.Single(point => point.Tag is ConnectionViewModel link && link.Connection == connection)
                : points.Single();
            Point position = HeadlessTestHelpers.Center(port, window);
            window.MouseMove(position);
            for (int click = 0; click < 2; click++)
            {
                window.MouseDown(position, MouseButton.Left);
                window.MouseUp(position, MouseButton.Left);
            }
            HeadlessTestHelpers.Render(3);

            Assert.That(graph.AllConnections, Is.EqualTo(surviving), "Double click must remove the intended model connection.");
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Count(), Is.EqualTo(surviving.Length));
            Assert.That(history.Undo(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.AllConnections, Is.EqualTo(original));
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().All(line => line.IsVisible), Is.True);
            Assert.That(history.Redo(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.AllConnections, Is.EqualTo(surviving));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(24)]
    [TestCase(360)]
    public void WarningLineContextMenuCutsAConnectionBetweenUnavailableNodes(int receivingNodeY)
    {
        GraphModel graph = RestoreGraph(failed: false, bothUnavailable: true);
        graph.Nodes[1].Position.Value = (360, receivingNodeY);
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(sequence);
        using var observer = new CoreObjectOperationObserver(null, graph, sequence);
        history.Subscribe(observer);
        using var vm = new NodeGraphViewModel(graph,
            NodeGraphTestContext.Create(new NodeGraphMutationService(history), player: true).Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 320, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ConnectionLine line = view.GetVisualDescendants().OfType<ConnectionLine>().Single();
            var geometry = line.RenderedGeometry;
            Assert.That(geometry, Is.Not.Null);
            double length = geometry!.ContourLength;
            Point? hit = null;
            for (double distance = 1; distance < length; distance++)
            {
                if (geometry.TryGetPointAtDistance(distance, out Point point)
                    && line.TranslatePoint(point, window) is { } position
                    && window.InputHitTest(position) == line)
                {
                    hit = point;
                    break;
                }
            }
            Assert.That(hit, Is.Not.Null, "The visible warning line must accept pointer input.");
            HeadlessTestHelpers.Click(line, hit, MouseButton.Right, move: true, ticks: 3);
            var menu = (FAMenuFlyout)line.ContextFlyout!;
            Assert.That(menu.IsOpen, Is.True);
            var cut = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => Equals(item.Text, Strings.Disconnect));
            HeadlessTestHelpers.Click(cut, move: true);
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.AllConnections, Is.Empty);
            Assert.That(history.Undo(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Single().IsVisible, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    // A missing plugin's node shows why it is unavailable, and is moved, renamed, collapsed and deleted like any node.
    [AvaloniaTest]
    public void UnavailableNodeShowsReasonAndIsEditedLikeAnyNode()
    {
        GraphModel graph = RestoreGraph(failed: false);
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(sequence);
        using var observer = new CoreObjectOperationObserver(null, graph, sequence);
        history.Subscribe(observer);
        Mock<IEditorContext> editor = NodeGraphTestContext.Create(new NodeGraphMutationService(history));
        editor.Setup(x => x.GetService(typeof(HistoryManager))).Returns(history);
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(x => x.IsPlaying).Returns(new ReactivePropertySlim<bool>(false));
        editor.Setup(x => x.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
        using var vm = new NodeGraphViewModel(graph, editor.Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 340, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GraphNodeView node = view.GetVisualDescendants().OfType<GraphNodeView>().Single(v => v.DataContext == vm.Nodes[0]);
            Assert.That(node.FindControl<StackPanel>("fallbackMessage")!.IsEffectivelyVisible, Is.True);
            ConnectionLine line = view.GetVisualDescendants().OfType<ConnectionLine>().Single();
            Assert.That(line.IsEffectivelyVisible, Is.True);
            Assert.That(line.StrokeDashArray, Is.Not.Empty);
            Point originalAnchor = line.EndPoint;
            window.MouseDown(new Point(130, 42), MouseButton.Left);
            window.MouseMove(new Point(180, 42), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(180, 42), MouseButton.Left);
            vm.Nodes[0].UpdateName("Changed");
            vm.Nodes[0].IsExpanded.Value = false;
            HeadlessTestHelpers.Render(3);

            JsonObject saved = CoreSerializer.SerializeToJsonObject(graph)["Nodes"]![0]!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(graph.Nodes[0].Position.Value, Is.EqualTo((74d, 24d)));
                Assert.That(saved["Position"]!.GetValue<string>(), Is.EqualTo("74,24"));
                Assert.That(saved[nameof(GraphNode.Name)]!.GetValue<string>(), Is.EqualTo("Changed"));
                Assert.That(saved[nameof(GraphNode.IsExpanded)]!.GetValue<bool>(), Is.False);
                Assert.That(saved["$type"]!.GetValue<string>(), Is.EqualTo(MissingType));
                Assert.That(((ICommand)vm.Nodes[0].Delete).CanExecute(null), Is.True);
                Assert.That(line.IsEffectivelyVisible, Is.True);
                Assert.That(line.EndPoint, Is.EqualTo(originalAnchor + new Vector(50, 0)));
            });

            vm.Nodes[0].Delete.Execute();
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.Nodes, Has.Count.EqualTo(1));
            Assert.That(graph.AllConnections, Is.Empty, "Deleting the node removes its connection too.");
        }
        finally
        {
            window.Close();
        }
    }

    // A node whose data failed to load saves the JSON it was read from as it is, so it stays read-only.
    [AvaloniaTest]
    public void NodeWhoseDataFailedToLoadShowsReasonAndIsReadOnly()
    {
        GraphModel graph = RestoreGraph(failed: true);
        var mutation = new Mock<INodeGraphMutationService>();
        using var vm = new NodeGraphViewModel(graph, NodeGraphTestContext.Create(mutation.Object).Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 340, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GraphNodeView node = view.GetVisualDescendants().OfType<GraphNodeView>().Single(v => v.DataContext == vm.Nodes[0]);
            Assert.That(node.FindControl<StackPanel>("fallbackMessage")!.IsEffectivelyVisible, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Single().IsVisible, Is.True);
            Assert.That(((ICommand)vm.Nodes[0].Delete).CanExecute(null), Is.False);
            vm.Nodes[0].Delete.Execute();
            vm.Nodes[0].UpdateName("Changed");
            vm.Nodes[0].UpdatePosition([]);
            vm.Nodes[0].IsSelected.Value = true;
            vm.Nodes[1].IsSelected.Value = true;
            window.MouseDown(new Point(130, 42), MouseButton.Left);
            window.MouseMove(new Point(180, 42), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(180, 42), MouseButton.Left);
            HeadlessTestHelpers.Render(3);
            Assert.That(vm.Nodes.All(item => item.IsSelected.Value), Is.True);
            Assert.That(vm.Nodes[1].Position.Value, Is.EqualTo(new Point(360, 24)));
            Assert.That(vm.Nodes[0].Position.Value, Is.EqualTo(new Point(24, 24)));
            Assert.That(graph.Nodes[0].Name.Value, Is.EqualTo("Unavailable node"));
            mutation.VerifyNoOtherCalls();
            vm.Nodes[0].IsExpanded.Value = false;
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.Nodes[0].IsExpanded.Value, Is.True, "Collapsing it would not be saved.");
            var peer = ControlAutomationPeer.CreatePeerForElement(node.FindControl<Border>("handle")!);
            Assert.That(peer.GetHelpText(), Is.EqualTo(vm.Nodes[0].FallbackMessage));
        }
        finally
        {
            window.Close();
        }
    }

    private static GraphModel RestoreGraph(bool failed, bool list = false, bool bothUnavailable = false)
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode { Name = { Value = "Unavailable node" }, Position = { Value = (24, 24) } };
        graph.Nodes.Add(source);
        if (list)
        {
            var output = new ExpressionNode { Position = { Value = (360, 24) } };
            var other = new RandomSingleNode { Position = { Value = (24, 190) } };
            graph.Nodes.AddRange([output, other]);
            graph.Connect(output.InputPort, source.Value);
            graph.Connect(output.InputPort, other.Value);
        }
        else
        {
            var output = new OutputNode { Position = { Value = (360, 24) } };
            graph.Nodes.Add(output);
            graph.Connect(output.InputPort, source.Value);
        }
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject node = json["Nodes"]![0]!.AsObject();
        if (failed)
            node.WriteDiscriminator(typeof(UnreadableNode));
        else
            node["$type"] = MissingType;
        if (bothUnavailable)
            json["Nodes"]![1]!["$type"] = MissingType;
        return (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
    }
}

public partial class UnreadableNode : GraphNode
{
    public override void Deserialize(ICoreSerializationContext context) => throw new InvalidOperationException("Invalid node payload.");
}
