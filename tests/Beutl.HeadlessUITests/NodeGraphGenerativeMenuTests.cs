using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
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

    [AvaloniaTest]
    public void NodeMenuListsGenerationsNewestFirstAndSwitchesBetweenThem()
    {
        var graph = new GraphModel();
        var node = new AiImageGenerationNode();
        var older = new GenerationRecord { Summary = "older", CreatedAt = DateTimeOffset.Now.AddMinutes(-5) };
        var newer = new GenerationRecord { Summary = "newer", CreatedAt = DateTimeOffset.Now, IsPinned = true };
        node.Generations.Add(older);
        node.Generations.Add(newer);
        node.SelectGeneration(newer.Id);
        graph.Nodes.Add(node);
        using var vm = new NodeGraphViewModel(graph, CreateEditor().Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 550 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var nodeView = view.GetVisualDescendants().OfType<GraphNodeView>().Single();
            var handle = nodeView.FindControl<Border>("handle")!;
            var flyout = (FAMenuFlyout)handle.ContextFlyout!;
            flyout.ShowAt(handle);
            HeadlessTestHelpers.Render(3);

            var history = nodeView.FindControl<FAMenuFlyoutSubItem>("GenerationHistoryMenu")!;
            var entries = history.Items.OfType<FAToggleMenuFlyoutItem>().ToArray();
            Assert.That(history.IsVisible, Is.True);
            Assert.That(entries.Select(e => e.Text), Has.Exactly(1).Contains("newer").And.Exactly(1).Contains("older"));
            Assert.That(entries[0].Text, Does.Contain("newer").And.Contain(NodeGraphStrings.Generative_Pinned));
            Assert.That(entries.Select(e => e.IsChecked), Is.EqualTo(new[] { true, false }));

            ((GraphNodeViewModel)nodeView.DataContext!).SelectGeneration(older.Id);
            Assert.That(node.ActiveGeneration, Is.SameAs(older));
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void PromptLibraryMenuAppliesASavedPromptToThePromptNode()
    {
        var graph = new GraphModel();
        var prompt = new AiPromptNode();
        prompt.Style.Property!.SetValue("stale style");
        var image = new AiImageGenerationNode();
        graph.Nodes.Add(prompt);
        graph.Nodes.Add(image);
        graph.Connect(image.Prompt, prompt.Output);
        var library = new StubPromptLibrary(
        [
            new GenerativePromptEntry("Cats", "a cat\nStyle: watercolor", true, true),
            new GenerativePromptEntry("a dog", "a dog", false, false),
        ]);
        Mock<IEditorContext> editor = CreateEditor();
        editor.Setup(x => x.GetService(typeof(IGenerativePromptLibrary))).Returns(library);
        using var vm = new NodeGraphViewModel(graph, editor.Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            GraphNodeView promptView = OpenMenu(view, prompt);
            var promptItems = promptView.FindControl<FAMenuFlyoutSubItem>("PromptLibraryMenu")!
                .Items.OfType<FAMenuFlyoutItem>().ToArray();
            Assert.That(promptItems.Select(i => i.Text), Does.Contain($"Cats  ({NodeGraphStrings.Generative_Pinned})")
                .And.Contain("a dog")
                .And.Contain(NodeGraphStrings.Generative_SaveTemplate));
            Assert.That(promptItems.Single(i => i.Text == "a dog").IsEnabled, Is.True);

            ((GraphNodeViewModel)promptView.DataContext!).ApplyPrompt("a cat\nStyle: watercolor");
            Assert.That(prompt.Prompt.Property!.GetValue(), Is.EqualTo("a cat\nStyle: watercolor"));
            Assert.That(prompt.Style.Property!.GetValue(), Is.Empty, "The saved text already holds its sections.");
            Assert.That(prompt.ComposePrompt(), Is.EqualTo("a cat Style: watercolor"),
                "Applied text is composed like typed text, whitespace included.");

            GraphNodeView imageView = OpenMenu(view, image);
            var imageItems = imageView.FindControl<FAMenuFlyoutSubItem>("PromptLibraryMenu")!
                .Items.OfType<FAMenuFlyoutItem>().ToArray();
            Assert.That(imageItems.Single(i => i.Text == "a dog").IsEnabled, Is.False,
                "A connected prompt cannot be replaced by a typed one.");

            ((GraphNodeViewModel)promptView.DataContext!).SavePromptTemplate(" Mine ");
            Assert.That(library.Saved, Is.EqualTo(new[] { ("Mine", prompt.ComposePrompt()) }));
        }
        finally
        {
            window.Close();
        }
    }

    private static GraphNodeView OpenMenu(NodeGraphView view, GraphNode node)
    {
        GraphNodeView nodeView = view.GetVisualDescendants().OfType<GraphNodeView>()
            .Single(v => ((GraphNodeViewModel)v.DataContext!).GraphNode == node);
        var handle = nodeView.FindControl<Border>("handle")!;
        var flyout = (FAMenuFlyout)handle.ContextFlyout!;
        flyout.ShowAt(handle);
        HeadlessTestHelpers.Render(3);
        flyout.Hide();
        return nodeView;
    }

    private sealed class StubPromptLibrary(IReadOnlyList<GenerativePromptEntry> entries) : IGenerativePromptLibrary
    {
        public List<(string Name, string Prompt)> Saved { get; } = [];

        public IReadOnlyList<GenerativePromptEntry> GetEntries(GenerativeOperation operation) => entries;

        public void Record(GenerativeOperation operation, string prompt)
        {
        }

        public string? SaveTemplate(GenerativeOperation operation, string name, string prompt)
        {
            Saved.Add((name, prompt));
            return null;
        }
    }

    private static Mock<IEditorContext> CreateEditor()
    {
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(x => x.IsPlaying).Returns(new ReactivePropertySlim<bool>(false));
        editor.Setup(x => x.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
        return editor;
    }

    private static MenuItem FindItem(ContextMenu menu, string header)
        => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));
}
