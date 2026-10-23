using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// The time ports of VideoSourceNode and SceneNode are edited with MediaTimeEditor.
[TestFixture]
public sealed class NodeTimePortEditorTests
{
    [AvaloniaTest]
    [TestCase(typeof(VideoSourceNode))]
    [TestCase(typeof(SceneNode))]
    public async Task A_time_port_shows_the_time_editor_and_takes_a_typed_time(Type nodeType)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("node-time-port");
        var node = (GraphNode)Activator.CreateInstance(nodeType)!;
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        var time = (InputPort<MediaTime>)node.Items.Single(item => item is InputPort<MediaTime>);
        using var graphViewModel = new NodeGraphViewModel(graph, editor);
        var view = new GraphNodeView { DataContext = graphViewModel.Nodes.Single() };
        var next = new Button { Content = "Next" };
        var window = new Window { Content = new StackPanel { Children = { view, next } }, Width = 360, Height = 420 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            TextBox box = view.GetVisualDescendants().OfType<MediaTimeEditor>().Single()
                .GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "PART_InnerTextBox");
            box.Focus();
            box.SelectAll();
            window.KeyTextInput("00:00:02.5");
            next.Focus();
            HeadlessTestHelpers.Render(3);

            Assert.That(time.Property!.GetValue(), Is.EqualTo(MediaTime.FromSeconds(2.5)));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }
}
