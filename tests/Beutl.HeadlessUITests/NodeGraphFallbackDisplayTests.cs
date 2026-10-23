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
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

public class NodeGraphFallbackDisplayTests
{
    private const string MissingType = "[Missing.Plugin]Missing:Node";

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
        var window = new Window { Content = view, Width = 320, Height = 340, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GraphNodeView node = view.GetVisualDescendants().OfType<GraphNodeView>().Single(v => v.DataContext == vm.Nodes[0]);
            Assert.That(node.FindControl<StackPanel>("fallbackMessage")!.IsEffectivelyVisible, Is.True);
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
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Single().IsVisible, Is.False);
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

    private static GraphModel RestoreGraph(bool failed)
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode { Name = { Value = "Unavailable node" }, Position = { Value = (24, 24) } };
        var output = new OutputNode { Position = { Value = (360, 24) } };
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject node = json["Nodes"]![0]!.AsObject();
        if (failed)
            node.WriteDiscriminator(typeof(UnreadableNode));
        else
            node["$type"] = MissingType;
        return (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
    }
}

public partial class UnreadableNode : GraphNode
{
    public override void Deserialize(ICoreSerializationContext context) => throw new InvalidOperationException("Invalid node payload.");
}
