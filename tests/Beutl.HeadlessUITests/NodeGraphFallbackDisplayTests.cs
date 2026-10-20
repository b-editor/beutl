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
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

public class NodeGraphFallbackDisplayTests
{
    [AvaloniaTest]
    public void UnavailableNodeShowsReasonAndIsReadOnly()
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode { Name = "Unavailable node", Position = (24, 24) };
        var output = new OutputNode { Position = (360, 24) };
        graph.Nodes.AddRange([source, output]);
        graph.Connect(output.InputPort, source.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        json["Nodes"]![0]!["$type"] = "[Missing.Plugin]Missing:Node";
        graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
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
            Assert.That(graph.Nodes[0].Name, Is.EqualTo("Unavailable node"));
            mutation.VerifyNoOtherCalls();
            vm.Nodes[0].IsExpanded.Value = false;
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.Nodes[0].IsExpanded, Is.True, "Expansion is editor state, not an edit to the retained payload.");
            var peer = ControlAutomationPeer.CreatePeerForElement(node.FindControl<Border>("handle")!);
            Assert.That(peer.GetHelpText(), Is.EqualTo(vm.Nodes[0].FallbackMessage));
        }
        finally
        {
            window.Close();
        }
    }
}
