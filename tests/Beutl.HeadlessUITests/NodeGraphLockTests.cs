using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Moq;

namespace Beutl.HeadlessUITests;

// A locked clip, and one whose file recovery keeps, make the node graph tab read-only: its gestures, menus,
// commands and node editors leave the graph as it is.
[TestFixture]
public class NodeGraphLockTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Drags_keys_and_menus_leave_the_graph_of_a_read_only_clip_as_it_is(bool protect)
    {
        var source = new ExpressionNode { Position = { Value = (20, 40) } };
        var free = new ExpressionNode { Position = { Value = (20, 260) } };
        var target = new ExpressionNode { Position = { Value = (380, 260) } };
        var output = new OutputNode { Position = { Value = (380, 40) } };
        using var scope = new LockedGraphScope(protect, [source, free, target, output],
            graph => graph.Connect(output.InputPort, source.Output));
        string before = CoreSerializer.SerializeToJsonObject(scope.Graph).ToJsonString();
        GraphNodeView sourceView = scope.NodeView(source);
        Border handle = sourceView.FindControl<Border>("handle")!;

        Point start = HeadlessTestHelpers.Center(handle, scope.Window);
        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(start + new Vector(60, 30), RawInputModifiers.LeftMouseButton);
        scope.Window.MouseUp(start + new Vector(60, 30), MouseButton.Left);
        Point from = HeadlessTestHelpers.Center(scope.Socket(free.Output), scope.Window);
        Point to = HeadlessTestHelpers.Center(scope.Socket(target.InputPort), scope.Window);
        scope.Window.MouseMove(from);
        scope.Window.MouseDown(from, MouseButton.Left);
        scope.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        scope.Window.MouseUp(to, MouseButton.Left);
        HeadlessTestHelpers.Click(scope.NodeView(output).FindControl<ToggleButton>("expandToggle")!, move: true);
        handle.Focus();
        scope.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        scope.Window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        HeadlessTestHelpers.Render(3);

        ZoomBorder zoom = scope.View.FindControl<ZoomBorder>("zoomBorder")!;
        var canvasMenu = (FAMenuFlyout)zoom.ContextFlyout!;
        canvasMenu.ShowAt(zoom);
        HeadlessTestHelpers.Render(3);
        // Read while shown: the items lose the graph as their data context when the menu closes.
        (string? Text, bool IsEnabled)[] subMenus =
            [.. canvasMenu.Items.OfType<FAMenuFlyoutSubItem>().Select(item => (item.Text, item.IsEnabled))];
        canvasMenu.Hide();
        var nodeMenu = (FAMenuFlyout)handle.ContextFlyout!;
        nodeMenu.ShowAt(handle);
        HeadlessTestHelpers.Render(3);
        FAMenuFlyoutItem rename = nodeMenu.Items.OfType<FAMenuFlyoutItem>().Single(item => Equals(item.Text, Strings.Rename));
        bool renameEnabled = rename.IsEnabled;
        nodeMenu.Hide();
        Assert.Multiple(() =>
        {
            Assert.That(CoreSerializer.SerializeToJsonObject(scope.Graph).ToJsonString(), Is.EqualTo(before));
            Assert.That(scope.History.UndoCount, Is.Zero);
            Assert.That(scope.History.IsEditing, Is.False);
            Assert.That(scope.Model.Nodes[0].Position.Value, Is.EqualTo(new Point(20, 40)), "The node stays where the model has it.");
            Assert.That(scope.Model.Nodes[3].IsExpanded.Value, Is.False, "Collapsing still works in the view.");
            Assert.That(scope.Lines(), Is.EqualTo(1), "No wire is drawn or dropped.");
            Assert.That(scope.View.GetVisualDescendants().OfType<NodePortPoint>().Any(p => p.IsEffectivelyEnabled), Is.False,
                "A port takes no drag, double click or menu.");
            Assert.That(((ICommand)scope.Model.Nodes[0].Delete).CanExecute(null), Is.False);
            Assert.That(subMenus.Select(item => item.Text), Is.EquivalentTo(new[] { Strings.AddNode, NodeGraphStrings.Template_Templates }));
            Assert.That(subMenus.Select(item => item.IsEnabled), Is.All.False, "Nothing can be added.");
            Assert.That(renameEnabled, Is.False);
        });
    }

    // A drag that the clip's lock overtakes ends with the nodes where the model still has them.
    [AvaloniaTest]
    public void A_move_the_service_refuses_puts_the_dragged_nodes_back()
    {
        var first = new ExpressionNode { Position = { Value = (20, 40) } };
        var second = new ExpressionNode { Position = { Value = (20, 260) } };
        using var scope = new LockedGraphScope(protect: false, [first, second], lockClip: false);
        foreach (GraphNodeViewModel node in scope.Model.Nodes)
            node.IsSelected.Value = true;
        Point start = HeadlessTestHelpers.Center(scope.NodeView(first).FindControl<Border>("handle")!, scope.Window);

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(start + new Vector(60, 30), RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Model.Nodes[0].Position.Value, Is.EqualTo(new Point(80, 70)), "The drag moves the nodes.");
        scope.Element.IsLocked.Value = true;
        scope.Window.MouseUp(start + new Vector(60, 30), MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        Assert.Multiple(() =>
        {
            Assert.That(first.Position.Value, Is.EqualTo((20d, 40d)));
            Assert.That(second.Position.Value, Is.EqualTo((20d, 260d)));
            Assert.That(scope.Model.Nodes.Select(node => node.Position.Value),
                Is.EqualTo(new[] { new Point(20, 40), new Point(20, 260) }));
            Assert.That(scope.History.UndoCount, Is.Zero);
        });
    }

    // Node editors get the clip, as the property tab's do: they lock with it and seek keyframes from its start.
    [AvaloniaTest]
    public async Task A_node_editor_is_read_only_on_a_locked_clip_and_seeks_keyframes_from_the_clip_start()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("node-graph-lock");
        await TestShell.AddElementAsync(editor, () => new NodeGraphDrawable(), MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        Element element = scene.Children.Single();
        GraphModel graph = ((NodeGraphDrawable)element.Objects.Single()).Model.Value!;
        var node = new RectGeometryNode();
        node.Object.Value.Width.Value = 100;
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 200 } });
        node.Object.Value.Height.SetKeyFrameAnimation(animation);
        graph.Nodes.Add(node);
        editor.HistoryManager.Commit("test setup");
        element.IsLocked.Value = true;
        int undoCount = editor.HistoryManager.UndoCount;
        using var viewModel = new NodeGraphViewModel(graph, editor);
        NumberEditorViewModel<float>[] editors = [.. viewModel.Nodes.Single().Items
            .Select(item => item.PropertyEditorContext).OfType<NumberEditorViewModel<float>>()];
        NumberEditorViewModel<float> width = editors.Single(e => ReferenceEquals(e.PropertyAdapter.GetEngineProperty(), node.Object.Value.Width));
        NumberEditorViewModel<float> height = editors.Single(e => ReferenceEquals(e.PropertyAdapter.GetEngineProperty(), node.Object.Value.Height));

        width.SetValue(100, 250);
        height.KeyFrameIndex.Value = 1;

        Assert.Multiple(() =>
        {
            Assert.That(width.CanEdit.Value, Is.False);
            Assert.That(node.Object.Value.Width.Value, Is.EqualTo(100));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            Assert.That(((IEditorClock)editor.GetService(typeof(IEditorClock))!).CurrentTime.Value,
                Is.EqualTo(MediaTime.FromSeconds(3)), "The keyframe at 1 s is 1 s after the clip start.");
        });
    }

    [AvaloniaTest]
    public async Task Generative_commands_leave_a_locked_clip_as_it_is()
    {
        var image = new AiImageGenerationNode { Position = { Value = (380, 40) } };
        var older = new GenerationRecord { Summary = { Value = "older" }, CreatedAt = { Value = DateTimeOffset.Now.AddMinutes(-5) } };
        var newer = new GenerationRecord { Summary = { Value = "newer" }, CreatedAt = { Value = DateTimeOffset.Now } };
        image.Generations.Add(older);
        image.Generations.Add(newer);
        image.SelectGeneration(newer.Id.Value);
        var prompt = new AiPromptNode { Position = { Value = (20, 40) } };
        using var scope = new LockedGraphScope(protect: false, [prompt, image]);
        string before = CoreSerializer.SerializeToJsonObject(scope.Graph).ToJsonString();
        GraphNodeViewModel imageViewModel = scope.Model.Nodes.Single(node => node.GraphNode == image);
        GraphNodeViewModel promptViewModel = scope.Model.Nodes.Single(node => node.GraphNode == prompt);
        GraphNodeView imageView = scope.NodeView(image);
        var nodeMenu = (FAMenuFlyout)imageView.FindControl<Border>("handle")!.ContextFlyout!;
        nodeMenu.ShowAt(imageView);
        HeadlessTestHelpers.Render(3);
        var history = imageView.FindControl<FAMenuFlyoutSubItem>("GenerationHistoryMenu")!;

        imageViewModel.SelectGeneration(older.Id.Value);
        imageViewModel.ToggleActivePinned();
        imageViewModel.PruneGenerations();
        promptViewModel.ApplyPrompt("a cat");
        await scope.Model.RunGenerativeAsync(null, force: true);

        Assert.Multiple(() =>
        {
            Assert.That(CoreSerializer.SerializeToJsonObject(scope.Graph).ToJsonString(), Is.EqualTo(before));
            Assert.That(scope.History.UndoCount, Is.Zero);
            Assert.That(scope.Model.CanRunGenerative.Value, Is.False);
            scope.Editor.Verify(x => x.GetService(typeof(IGenerativeNodeExecutor)), Times.Never(), "Nothing runs.");
            Assert.That(history.Items.OfType<FAToggleMenuFlyoutItem>().Count(), Is.EqualTo(2));
            Assert.That(history.Items.OfType<FAMenuFlyoutItem>().Where(item => item.IsEnabled), Is.Empty,
                "The history offers nothing to pick, pin or prune.");
        });
        nodeMenu.Hide();
    }

    // A result the server sends after the clip was locked is dropped, and the lock stops the run.
    [AvaloniaTest]
    [TestCase(1)]
    [TestCase(2)]
    public async Task A_generation_that_returns_after_the_clip_is_locked_leaves_it_as_it_is(int variations)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-locked-generation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var image = new AiImageGenerationNode { Position = { Value = (20, 40) } };
        image.Prompt.Property!.SetValue("a cat");
        using var scope = new LockedGraphScope(protect: false, [image], lockClip: false);
        var executor = new WaitingExecutor(directory);
        scope.Editor.Setup(x => x.GetService(typeof(IGenerativeNodeExecutor))).Returns(executor);
        scope.History.Commit("Setup");
        int undoCount = scope.History.UndoCount;
        try
        {
            Task run = scope.Model.RunGenerativeAsync([image], force: true, variations);
            await executor.Started.Task.WaitAsync(TimeSpan.FromMinutes(1));
            scope.Element.IsLocked.Value = true;
            executor.Release();
            await run.WaitAsync(TimeSpan.FromMinutes(1));

            Assert.Multiple(() =>
            {
                Assert.That(image.Generations, Is.Empty);
                Assert.That(image.ActiveGeneration, Is.Null);
                Assert.That(scope.History.UndoCount, Is.EqualTo(undoCount));
                Assert.That(image.Status, Is.Not.EqualTo(GenerativeNodeStatus.Running));
                Assert.That(scope.Model.IsGenerating.Value, Is.False);
                Assert.That(executor.Calls, Is.EqualTo(1), "No more requests after the lock.");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Answers once released, whatever its token says, as a server that has taken the job does.
    private sealed class WaitingExecutor(string directory) : IGenerativeNodeExecutor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public void Release() => _release.TrySetResult();

        public async Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request, IProgress<GenerativeProgress> progress, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await _release.Task;
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            using (var bitmap = new Bitmap(8, 8))
            using (FileStream stream = File.Create(path))
                bitmap.Save(stream, Beutl.Graphics.EncodedImageFormat.Png);
            return new GenerativeExecutionResult(new Uri(path), request.ModelId, (request as AiImageGenerationNodeRequest)?.Seed);
        }
    }

    // A graph in a clip on a scene, with the history and editor services a NodeGraphView asks for.
    private sealed class LockedGraphScope : IDisposable
    {
        private readonly CoreObjectOperationObserver _observer;

        public LockedGraphScope(bool protect, GraphNode[] nodes, Action<GraphModel>? connect = null, bool lockClip = true)
        {
            var scene = new Scene(64, 64, "Scene");
            Element = new Element
            {
                Length = { Value = MediaTime.FromSeconds(1) },
                Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
            };
            var drawable = new NodeGraphDrawable();
            Element.AddObject(drawable);
            scene.Children.Add(Element);
            Graph = drawable.Model.Value!;
            Graph.Nodes.AddRange(nodes);
            connect?.Invoke(Graph);
            if (protect)
            {
                // Something else in the clip could not be loaded, so removing a node repairs nothing.
                Element.AddObject(new FallbackEngineObject
                {
                    Json = new JsonObject { ["$type"] = ElementMigration.LegacyNodeTreeTypeName },
                    Reason = FallbackReason.TypeNotFound,
                });
                Element.SuppressedStorageSource = new SuppressedStorageSource([], Element.Uri);
            }
            else if (lockClip)
            {
                Element.IsLocked.Value = true;
            }

            var sequence = new OperationSequenceGenerator();
            History = new HistoryManager(sequence);
            _observer = new CoreObjectOperationObserver(null, Graph, sequence);
            History.Subscribe(_observer);
            Editor = NodeGraphTestContext.Create(new NodeGraphMutationService(History), player: true);
            Editor.Setup(x => x.GetService(typeof(HistoryManager))).Returns(History);
            Model = new NodeGraphViewModel(Graph, Editor.Object);
            View = new NodeGraphView { DataContext = Model };
            Window = new Window { Content = View, Width = 800, Height = 550 };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            ZoomBorder zoom = View.FindControl<ZoomBorder>("zoomBorder")!;
            zoom.EnableAnimations = false;
            zoom.SetMatrix(Matrix.Identity, true);
            HeadlessTestHelpers.Render(3);
        }

        public Element Element { get; }

        public GraphModel Graph { get; }

        public HistoryManager History { get; }

        public Mock<IEditorContext> Editor { get; }

        public NodeGraphViewModel Model { get; }

        public NodeGraphView View { get; }

        public Window Window { get; }

        public GraphNodeView NodeView(GraphNode node) => View.GetVisualDescendants().OfType<GraphNodeView>()
            .Single(view => view.DataContext is GraphNodeViewModel viewModel && viewModel.GraphNode == node);

        public NodePortPoint Socket(INodePort port) => View.GetVisualDescendants().OfType<NodePortPoint>()
            .Single(point => point.DataContext is NodePortViewModel viewModel && viewModel.Model == port && point.Tag is null);

        public int Lines() => View.FindControl<Canvas>("canvas")!.Children.OfType<ConnectionLine>().Count();

        public void Dispose()
        {
            View.DataContext = null;
            Window.Close();
            Model.Dispose();
            _observer.Dispose();
            History.Dispose();
        }
    }
}
