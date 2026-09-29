using System.Text.Json.Nodes;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
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
using FluentIcons.Avalonia.Fluent;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NodeGraphFallbackTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task FailedNode_ShowsReasonAndOriginalType_InBothThemes(bool deserializationFails, bool light)
    {
        var graph = new GraphModel();
        graph.Nodes.Add(new OutputNode { Position = (24, 24) });
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject nodeJson = json[nameof(GraphModel.Nodes)]![0]!.AsObject();
        nodeJson["$type"] = "[Missing.Plugin]Missing.Nodes:UnavailableNode";
        if (deserializationFails) nodeJson.WriteDiscriminator(typeof(UnreadableEditorGraphNode));
        graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var fallback = (IFallback)graph.Nodes.Single();
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        using var vm = new NodeGraphViewModel(graph, editor.Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 320,
            Height = 480,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GraphNodeView node = view.GetVisualDescendants().OfType<GraphNodeView>().Single();
            StackPanel message = node.FindControl<StackPanel>("fallbackMessage")!;
            FluentIcon warning = node.GetVisualDescendants().OfType<FluentIcon>()
                .Single(icon => Equals(ToolTip.GetTip(icon), FallbackHelper.GetFallbackMessage(fallback)));
            Assert.Multiple(() =>
            {
                Assert.That(vm.Nodes.Single().IsFallback, Is.True);
                Assert.That(message.IsEffectivelyVisible, Is.True);
                Assert.That(message.Children.OfType<TextBlock>().First().Text,
                    Is.EqualTo(FallbackHelper.GetFallbackMessage(fallback)));
                Assert.That(message.Children.OfType<SelectableTextBlock>().Single().Text,
                    Is.EqualTo(FallbackHelper.GetTypeName(fallback)));
                Assert.That(warning.IsEffectivelyVisible, Is.True);
                Assert.That(message.Bounds.Width, Is.LessThan(node.Bounds.Width));
                Assert.That(node.Bounds.Width, Is.EqualTo(215));
            });
            await Capture(window, $"fallback-{deserializationFails}-{light}");

            node.FindControl<ToggleButton>("expandToggle")!.IsChecked = false;
            HeadlessTestHelpers.Render(3);
            Assert.That(message.IsEffectivelyVisible, Is.False);
            Assert.That(warning.IsEffectivelyVisible, Is.True);
            Border header = node.FindControl<Border>("handle")!;
            Assert.That(header.Focus(), Is.True);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(header);
            Assert.That(peer.GetName(), Is.EqualTo(vm.Nodes.Single().NodeName.Value));
            Assert.That(peer.GetHelpText(), Is.EqualTo(FallbackHelper.GetFallbackMessage(fallback)));
            await Capture(window, $"fallback-collapsed-{deserializationFails}-{light}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task RetainedConnections_AreVisibleAndFollowCollapsedNode(bool light)
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode { Position = (24, 24) };
        var failed = new RandomSingleNode { Position = (280, 24), Name = "Unavailable node" };
        var output = new OutputNode { Position = (536, 24) };
        graph.Nodes.AddRange([source, failed, output]);
        graph.Connect(failed.Minimum, source.Value);
        graph.Connect(output.InputPort, failed.Value);
        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        json["Nodes"]![1]!["$type"] = "[Missing.Plugin]Missing.Nodes:UnavailableNode";
        graph = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(graph, sequence);
        using var observer = new CoreObjectOperationObserver(null, graph, sequence);
        using var subscription = history.Subscribe(observer);
        var mutation = new NodeGraphMutationService(history);
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        editor.Setup(x => x.GetService(typeof(INodeGraphMutationService))).Returns(mutation);
        using var vm = new NodeGraphViewModel(graph, editor.Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 800,
            Height = 440,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(vm.AllConnections, Has.Count.EqualTo(2));
            foreach (ConnectionViewModel connection in vm.AllConnections)
            {
                Assert.That(connection.InputPortVM.Value, Is.Not.Null);
                Assert.That(connection.OutputPortVM.Value, Is.Not.Null);
                Assert.That(connection.InputPortPosition.Value.X, Is.GreaterThan(connection.OutputPortPosition.Value.X));
            }
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>().Count(), Is.EqualTo(2));
            await Capture(window, $"fallback-wires-{light}");

            vm.Nodes[1].IsExpanded.Value = false;
            HeadlessTestHelpers.Render(3);
            Assert.That(vm.AllConnections[0].InputPortPosition.Value.Y,
                Is.EqualTo(vm.AllConnections[1].OutputPortPosition.Value.Y));
            await Capture(window, $"fallback-wires-collapsed-{light}");

            vm.Nodes[1].Delete.Execute();
            HeadlessTestHelpers.Render(3);
            Assert.That(vm.AllConnections, Is.Empty);
            Assert.That(view.GetVisualDescendants().OfType<ConnectionLine>(), Is.Empty);
            Assert.That(history.Undo(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(vm.AllConnections, Has.Count.EqualTo(2));
            Assert.That(vm.AllConnections.All(connection => connection.InputPortVM.Value != null
                && connection.OutputPortVM.Value != null), Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void AvailableNode_DoesNotShowFallbackMessage()
    {
        var graph = new GraphModel();
        graph.Nodes.Add(new OutputNode());
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        using var vm = new NodeGraphViewModel(graph, editor.Object);
        var node = new GraphNodeView { DataContext = vm.Nodes.Single() };
        var window = new Window { Content = node, Width = 320, Height = 240 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(vm.Nodes.Single().IsFallback, Is.False);
            Assert.That(node.FindControl<StackPanel>("fallbackMessage")!.IsEffectivelyVisible, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task Capture(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_GRAPH_FALLBACK_CAPTURE") is not { Length: > 0 } directory) return;
        // Let the node's 250 ms expand/collapse transition finish before capturing pixels.
        await Task.Delay(300);
        HeadlessTestHelpers.Render(3);
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        Assert.That(image, Is.Not.Null);
        image!.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}

public partial class UnreadableEditorGraphNode : GraphNode
{
    public override void Deserialize(ICoreSerializationContext context)
        => throw new InvalidOperationException("Node payload could not be read.");
}
