using System.Text.Json.Nodes;
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
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
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
            await Capture(window, $"fallback-collapsed-{deserializationFails}-{light}");
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
