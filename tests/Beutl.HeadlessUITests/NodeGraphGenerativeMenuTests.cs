using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Testing.Headless;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NodeGraphGenerativeMenuTests
{
    [AvaloniaTest]
    public void RunAiNodesIsInTheCanvasMenuAndFollowsWhetherTheGraphHasAiNodes()
    {
        var graph = new GraphModel();
        graph.Nodes.Add(new TimeNode());
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(x => x.IsPlaying).Returns(new ReactivePropertySlim<bool>(false));
        editor.Setup(x => x.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
        using var vm = new NodeGraphViewModel(graph, editor.Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 550 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var zoom = view.FindControl<ZoomBorder>("zoomBorder")!;
            ContextMenu menu = zoom.ContextMenu!;
            menu.Open(zoom);
            HeadlessTestHelpers.Render(3);
            MenuItem run = FindItem(menu, NodeGraphStrings.Generative_RunQueue);
            MenuItem stop = FindItem(menu, NodeGraphStrings.Generative_StopQueue);
            Assert.That(run.IsEnabled, Is.False, "No AI node, nothing to run.");
            Assert.That(stop.IsVisible, Is.False);

            var node = new AiImageGenerationNode();
            graph.Nodes.Add(node);
            HeadlessTestHelpers.Render(3);
            Assert.That(run.IsEnabled, Is.True);

            graph.Nodes.Remove(node);
            HeadlessTestHelpers.Render(3);
            Assert.That(run.IsEnabled, Is.False);
            menu.Close();
        }
        finally
        {
            window.Close();
        }
    }

    private static MenuItem FindItem(ContextMenu menu, string header)
        => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));
}
