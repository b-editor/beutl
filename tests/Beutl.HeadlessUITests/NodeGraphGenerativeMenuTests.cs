using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Controls;
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
            FAMenuFlyout menu = (FAMenuFlyout)zoom.ContextFlyout!;
            menu.ShowAt(zoom);
            HeadlessTestHelpers.Render(3);
            FAMenuFlyoutItem run = FindItem(menu, NodeGraphStrings.Generative_RunQueue);
            FAMenuFlyoutItem stop = FindItem(menu, NodeGraphStrings.Generative_StopQueue);
            Assert.That(run.IsEnabled, Is.False, "No AI node, nothing to run.");
            Assert.That(stop.IsVisible, Is.False);

            var node = new AiImageGenerationNode();
            graph.Nodes.Add(node);
            HeadlessTestHelpers.Render(3);
            Assert.That(run.IsEnabled, Is.True);

            graph.Nodes.Remove(node);
            HeadlessTestHelpers.Render(3);
            Assert.That(run.IsEnabled, Is.False);
            menu.Hide();
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
            Assert.That(prompt.Prompt.Property!.GetValue(), Is.EqualTo("a cat"));
            Assert.That(prompt.Style.Property!.GetValue(), Is.EqualTo("watercolor"),
                "The saved sections go back into their own fields.");
            Assert.That(prompt.ComposePrompt(), Is.EqualTo("a cat\nStyle: watercolor"),
                "Applying a saved prompt sends the text that was saved.");

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

    [AvaloniaTest]
    public void PreviewShowsAProgressRingAndLoadingTextWhileBusy()
    {
        var graph = new GraphModel();
        var node = new AiImageGenerationNode();
        graph.Nodes.Add(node);
        using var vm = new NodeGraphViewModel(graph, CreateEditor().Object);
        var view = new NodeGraphView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var preview = (NodeMonitor<Beutl.Media.Source.Ref<Beutl.Media.Bitmap>?>)node.Items.OfType<INodeMonitor>()
                .Single(m => m.Name == "GenerationPreview");
            ProgressRing ring = view.GetVisualDescendants().OfType<ProgressRing>().Single();
            Assert.That(ring.IsEffectivelyVisible, Is.False);

            preview.SetBusy(true, NodeGraphStrings.Generative_Loading);
            HeadlessTestHelpers.Render(3);
            Assert.That(ring.IsEffectivelyVisible, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.IsEffectivelyVisible && t.Text == NodeGraphStrings.Generative_Loading), Is.True);

            preview.SetBusy(true);
            HeadlessTestHelpers.Render(3);
            Assert.That(ring.IsEffectivelyVisible, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.IsEffectivelyVisible && t.Text == NodeGraphStrings.Generative_Loading), Is.False);

            preview.SetBusy(false);
            HeadlessTestHelpers.Render(3);
            Assert.That(ring.IsEffectivelyVisible, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task CompareShowsEveryResultAndAdoptsTheChosenOneWithItsSeed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-compare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var graph = new GraphModel();
            var node = new AiImageGenerationNode();
            foreach (int seed in new[] { 7, 8, 9 })
            {
                string path = Path.Combine(directory, $"{seed}.png");
                using (var bitmap = new Beutl.Media.Bitmap(8, 8))
                using (var stream = File.Create(path))
                    bitmap.Save(stream, Beutl.Graphics.EncodedImageFormat.Png);
                var image = new Beutl.Media.Source.ImageSource();
                image.ReadFrom(new Uri(path));
                node.Generations.Add(new GenerationRecord { Image = image, Seed = seed, CreatedAt = DateTimeOffset.Now });
            }

            node.SelectGeneration(node.Generations[2].Id);
            graph.Nodes.Add(node);
            using var vm = new NodeGraphViewModel(graph, CreateEditor().Object);
            GraphNodeViewModel nodeVm = vm.Nodes.Single();
            int shown = 0;
            int thumbnails = 0;
            nodeVm.ShowDialogAsync = dialog =>
            {
                var list = (ListBox)dialog.Content!;
                shown = list.Items.Count;
                thumbnails = list.Items.OfType<ListBoxItem>()
                    .Count(item => ((StackPanel)item.Content!).Children.OfType<BitmapView>().Any());
                Assert.That(list.SelectedIndex, Is.EqualTo(2), "Starts on the active result.");
                list.SelectedIndex = 0;
                return Task.FromResult(FAContentDialogResult.Primary);
            };

            await nodeVm.CompareGenerationsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(shown, Is.EqualTo(3));
                Assert.That(thumbnails, Is.EqualTo(3));
                Assert.That(node.ActiveGeneration, Is.SameAs(node.Generations[0]));
                Assert.That(node.Seed.Property!.GetValue(), Is.EqualTo(7), "The inputs reproduce the adopted result.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaTest]
    public void AGroupSavedAsATemplateCanBeAddedFromTheCanvasMenuAndUndone()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-node-templates-{Guid.NewGuid():N}");
        try
        {
            var graph = new GraphModel();
            var group = new Beutl.NodeGraph.Nodes.Group.GroupNode();
            group.Group.Nodes.Add(new AiPromptNode());
            graph.Nodes.Add(group);
            var sequence = new Beutl.Editor.OperationSequenceGenerator();
            using var history = new Beutl.Editor.HistoryManager(graph, sequence);
            using var observer = new Beutl.Editor.Observers.CoreObjectOperationObserver(null, graph, sequence);
            history.Subscribe(observer);
            Mock<IEditorContext> editor = CreateEditor();
            editor.Setup(x => x.GetService(typeof(INodeGraphMutationService))).Returns(new NodeGraphMutationService(history));
            using var vm = new NodeGraphViewModel(graph, editor.Object)
            {
                Templates = new Beutl.NodeGraph.Nodes.Group.GroupNodeTemplates(directory),
            };
            var view = new NodeGraphView { DataContext = vm };
            var window = new Window { Content = view, Width = 800, Height = 550 };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render(3);
                GraphNodeViewModel groupVm = vm.Nodes.Single();
                Assert.That(groupVm.IsGroup, Is.True);
                Assert.That(groupVm.SaveAsTemplate("Prompt kit"), Is.Not.Null);

                ZoomBorder zoom = view.FindControl<ZoomBorder>("zoomBorder")!;
                FAMenuFlyout menu = (FAMenuFlyout)zoom.ContextFlyout!;
                menu.ShowAt(zoom);
                HeadlessTestHelpers.Render(3);
                FAMenuFlyoutSubItem templates = menu.Items.OfType<FAMenuFlyoutSubItem>()
                    .Single(item => Equals(item.Text, NodeGraphStrings.Template_Templates));
                FAMenuFlyoutItem entry = templates.Items.Cast<FAMenuFlyoutItem>().Single();
                Assert.That(entry.Text, Is.EqualTo("Prompt kit"));
                entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
                menu.Hide();

                Assert.That(graph.Nodes.OfType<Beutl.NodeGraph.Nodes.Group.GroupNode>().Count(), Is.EqualTo(2));
                GroupNodeCopyHasItsOwnPrompt(graph);

                history.Undo();
                Assert.That(graph.Nodes, Has.Count.EqualTo(1), "Adding a template is undoable.");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void GroupNodeCopyHasItsOwnPrompt(GraphModel graph)
    {
        var prompts = graph.Nodes.OfType<Beutl.NodeGraph.Nodes.Group.GroupNode>()
            .Select(g => g.Group.Nodes.OfType<AiPromptNode>().Single().Id)
            .ToArray();
        Assert.That(prompts.Distinct().Count(), Is.EqualTo(2));
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

    private static FAMenuFlyoutItem FindItem(FAMenuFlyout menu, string header)
        => menu.Items.OfType<FAMenuFlyoutItem>().Single(item => Equals(item.Text, header));
}
