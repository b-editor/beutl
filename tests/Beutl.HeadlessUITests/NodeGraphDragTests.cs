using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Testing.Headless;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class NodeGraphDragTests
{
    public enum DragEnd { CaptureLoss, Playback, Escape, Undo }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Drag_pulses_when_entering_a_connectable_port_without_connecting_until_release(bool valid, bool reverse)
    {
        var source = new ExpressionNode { Position = { Value = (20, 40) } };
        var output = new OutputNode { Position = { Value = (380, 40) } };
        var other = new ExpressionNode { Position = { Value = (380, 240) } };
        using var scope = PortScope.Create(source, output, other);
        using var feedback = new HapticFeedbackRecorder();
        Point start = scope.Socket(source.Output).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point target = scope.Socket(valid ? output.InputPort : other.Output).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        if (reverse) (start, target) = (target, start);
        scope.Window.MouseMove(target);
        Assert.That(feedback.Pulses, Is.Empty, "Hover without dragging stays silent.");
        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Assert.That(feedback.Pulses, Has.Count.EqualTo(valid ? 1 : 0));
        Assert.That(scope.Graph.AllConnections, Is.Empty, "Hover must not create a connection.");
        Assert.That(scope.History.UndoCount, Is.Zero);
        scope.Window.MouseMove(target + new Vector(1, 0), RawInputModifiers.LeftMouseButton);
        Assert.That(feedback.Pulses, Has.Count.EqualTo(valid ? 1 : 0), "Staying on the same port must not repeat.");
        scope.Window.MouseMove(new Point(720, 460), RawInputModifiers.LeftMouseButton);
        scope.Window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Assert.That(feedback.Pulses, Has.Count.EqualTo(valid ? 2 : 0), "Re-entering the port should pulse again.");
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(scope.Window, nameof(NodeGraphDragTests), $"hover-{valid}-{reverse}");
        scope.Window.MouseUp(target, MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Graph.AllConnections, Has.Count.EqualTo(valid ? 1 : 0));
        Assert.That(feedback.Pulses, Is.EqualTo(Enumerable.Repeat(HapticFeedbackKind.Alignment, valid ? 2 : 0)));
        HeadlessCapture.SaveIfRequested(scope.Window, nameof(NodeGraphDragTests), $"connection-{valid}");
        if (valid)
        {
            scope.History.Undo();
            scope.History.Redo();
            Assert.That(feedback.Pulses, Has.Count.EqualTo(2));
        }
    }

    [AvaloniaTest]
    public void List_port_feedback_tracks_the_model_across_its_placeholder_and_connection_points()
    {
        var first = new ExpressionNode { Position = { Value = (20, 20) } };
        var source = new ExpressionNode { Position = { Value = (20, 220) } };
        var target = new ExpressionNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(first, source, target);
        scope.Graph.Connect(target.InputPort, first.Output);
        scope.History.Commit();
        HeadlessTestHelpers.Render(3);
        NodePortPoint slot = scope.View.GetVisualDescendants().OfType<NodePortPoint>()
            .Single(p => p.Tag is ConnectionViewModel && p.DataContext is NodePortViewModel vm && vm.Model == target.InputPort);
        NodePortPoint placeholder = scope.Socket(target.InputPort);
        using var feedback = new HapticFeedbackRecorder();
        Point start = scope.Socket(source.Output).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        Hover(placeholder, 1);
        Hover(slot, 1);
        Hover(scope.Socket(first.InputPort), 2); // A different logical port is a new target.
        Hover(placeholder, 3);
        scope.Window.MouseMove(new Point(720, 460), RawInputModifiers.LeftMouseButton);
        Hover(slot, 4); // Leaving and re-entering the same logical port should pulse.
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(scope.Window, nameof(NodeGraphDragTests), "list-port-hover");
        scope.Window.MouseUp(slot.TranslatePoint(new Point(5, 5), scope.Window)!.Value, MouseButton.Left);
        Assert.That(scope.Graph.AllConnections, Has.Count.EqualTo(2));
        Assert.That(feedback.Pulses, Has.Count.EqualTo(4));

        void Hover(NodePortPoint point, int pulses)
        {
            scope.Window.MouseMove(point.TranslatePoint(new Point(5, 5), scope.Window)!.Value, RawInputModifiers.LeftMouseButton);
            Assert.That(feedback.Pulses, Has.Count.EqualTo(pulses));
            Assert.That(scope.Graph.AllConnections, Has.Count.EqualTo(1), "Hover must not change the graph.");
        }
    }

    [AvaloniaTest]
    public void Dragging_over_an_occupied_input_does_not_pulse_or_change_its_connection()
    {
        var first = new ExpressionNode { Position = { Value = (20, 40) } };
        var second = new ExpressionNode { Position = { Value = (20, 240) } };
        var output = new OutputNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(first, second, output);
        var connection = scope.Graph.Connect(output.InputPort, first.Output);
        scope.History.Commit();
        HeadlessTestHelpers.Render(3);
        using var feedback = new HapticFeedbackRecorder();
        Point start = scope.Socket(second.Output).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point target = scope.Socket(output.InputPort).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Assert.That(feedback.Pulses, Is.Empty);
        scope.Window.MouseUp(target, MouseButton.Left);
        Assert.That(feedback.Pulses, Is.Empty);
        Assert.That(scope.Graph.AllConnections.Single(), Is.SameAs(connection));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public void Capture_loss_and_playback_connect_at_the_last_port_and_Escape_and_Undo_drop_the_wire(DragEnd end)
    {
        using var feedback = new HapticFeedbackRecorder();
        var source = new ExpressionNode { Position = { Value = (20, 40) } };
        var output = new OutputNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(source, output);
        NodePortPoint from = scope.Socket(source.Output);
        NodePortPoint to = scope.Socket(output.InputPort);
        IPointer? pointer = null;
        from.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        Point start = from.TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point over = to.TranslatePoint(new Point(5, 5), scope.Window)!.Value;

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(over, RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.History.IsEditing, Is.True, "The drag must hold an edit session.");
        Assert.That(scope.Lines(), Is.EqualTo(1), "The drag draws a wire.");
        Assert.That(feedback.Pulses, Has.Count.EqualTo(1));

        scope.End(end, pointer!);
        // The pointer no longer holds the wire, so releasing over the empty canvas changes nothing.
        Point blank = new(400, 450);
        scope.Window.MouseMove(blank, RawInputModifiers.LeftMouseButton);
        scope.Window.MouseUp(blank, MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        bool connected = end is DragEnd.CaptureLoss or DragEnd.Playback;
        Assert.That(feedback.Pulses, Has.Count.EqualTo(1), "Ending the drag must not emit additional feedback.");
        Assert.Multiple(() =>
        {
            Assert.That(scope.Graph.AllConnections, Has.Count.EqualTo(connected ? 1 : 0));
            Assert.That(scope.Lines(), Is.EqualTo(connected ? 1 : 0), "Only a made connection keeps a wire.");
            Assert.That(scope.History.UndoCount, Is.EqualTo(connected ? 1 : 0));
            Assert.That(scope.History.IsEditing, Is.False);
            Assert.That(scope.View.PortDropMenu?.IsOpen, Is.Not.True, "Only a real release offers nodes.");
        });
    }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public void Capture_loss_and_playback_keep_a_reordered_list_slot_and_Escape_and_Undo_put_it_back(DragEnd end)
    {
        var first = new ExpressionNode { Position = { Value = (20, 20) } };
        var second = new ExpressionNode { Position = { Value = (20, 220) } };
        var target = new ExpressionNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(first, second, target);
        scope.Graph.Connect(target.InputPort, first.Output);
        scope.Graph.Connect(target.InputPort, second.Output);
        scope.History.Commit();
        HeadlessTestHelpers.Render(3);
        int undoCount = scope.History.UndoCount;
        NodePortPoint[] slots = scope.View.GetVisualDescendants().OfType<NodePortPoint>()
            .Where(p => p.Tag is ConnectionViewModel && p.DataContext is InputPortViewModel input && input.Model == target.InputPort)
            .OrderBy(p => p.TranslatePoint(default, scope.Window)!.Value.Y)
            .ToArray();
        Assert.That(slots, Has.Length.EqualTo(2));
        IPointer? pointer = null;
        slots[0].AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        Point start = slots[0].TranslatePoint(new Point(5, 5), scope.Window)!.Value;

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(start + new Vector(0, 10), RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.History.IsEditing, Is.True, "The drag must hold an edit session.");

        scope.End(end, pointer!);
        scope.Window.MouseMove(start + new Vector(0, 30), RawInputModifiers.LeftMouseButton);
        scope.Window.MouseUp(start + new Vector(0, 30), MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
        Assert.Multiple(() =>
        {
            Assert.That(target.InputPort.Connections.Select(c => c.Value!.Output.Value.Value),
                Is.EqualTo(kept ? new object[] { second.Output, first.Output } : new object[] { first.Output, second.Output }));
            Assert.That(scope.History.UndoCount, Is.EqualTo(undoCount + (kept ? 1 : 0)));
            Assert.That(scope.History.IsEditing, Is.False);
            Assert.That(slots.All(slot => slot.RenderTransform is null || !slot.IsAttachedToVisualTree()), Is.True,
                "No slot stays shifted.");
        });
    }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public void Capture_loss_and_playback_keep_moved_nodes_and_Escape_and_Undo_put_them_back(DragEnd end)
    {
        var first = new ExpressionNode { Position = { Value = (20, 40) } };
        var second = new ExpressionNode { Position = { Value = (20, 260) } };
        using var scope = PortScope.Create(first, second);
        foreach (GraphNodeViewModel node in scope.Model.Nodes)
            node.IsSelected.Value = true;
        GraphNodeView firstView = scope.View.GetVisualDescendants().OfType<GraphNodeView>()
            .Single(v => v.DataContext is GraphNodeViewModel vm && vm.GraphNode == first);
        Border handle = firstView.FindControl<Border>("handle")!;
        IPointer? pointer = null;
        handle.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        Point start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), scope.Window)!.Value;

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(start + new Vector(60, 30), RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.History.IsEditing, Is.True, "The drag must hold an edit session.");

        scope.End(end, pointer!);
        scope.Window.MouseMove(start + new Vector(120, 60), RawInputModifiers.LeftMouseButton);
        scope.Window.MouseUp(start + new Vector(120, 60), MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
        double dx = kept ? 60 : 0;
        double dy = kept ? 30 : 0;
        Assert.Multiple(() =>
        {
            Assert.That(first.Position.Value, Is.EqualTo((20 + dx, 40 + dy)));
            Assert.That(second.Position.Value, Is.EqualTo((20 + dx, 260 + dy)));
            foreach (GraphNodeViewModel node in scope.Model.Nodes)
                Assert.That(node.Position.Value, Is.EqualTo(new Point(node.GraphNode.Position.Value.X, node.GraphNode.Position.Value.Y)));
            Assert.That(scope.History.UndoCount, Is.EqualTo(kept ? 1 : 0));
            Assert.That(scope.History.IsEditing, Is.False);
        });
    }

    private sealed class PortScope : IDisposable
    {
        private readonly CoreObjectOperationObserver _observer;
        private readonly ReactivePropertySlim<bool> _playing = new(false);

        private PortScope(GraphModel graph)
        {
            Graph = graph;
            var sequence = new OperationSequenceGenerator();
            History = new HistoryManager(sequence);
            _observer = new CoreObjectOperationObserver(null, graph, sequence);
            History.Subscribe(_observer);
            Mock<IEditorContext> editor = NodeGraphTestContext.Create(new NodeGraphMutationService(History));
            editor.Setup(x => x.GetService(typeof(HistoryManager))).Returns(History);
            var player = new Mock<IPreviewPlayer>();
            player.SetupGet(x => x.IsPlaying).Returns(_playing);
            editor.Setup(x => x.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
            Model = new NodeGraphViewModel(graph, editor.Object);
            View = new NodeGraphView { DataContext = Model };
            Window = new Window { Content = View, Width = 800, Height = 550 };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            ZoomBorder zoom = View.FindControl<ZoomBorder>("zoomBorder")!;
            zoom.EnableAnimations = false;
            zoom.SetMatrix(Matrix.Identity, true);
            HeadlessTestHelpers.Render(3);
        }

        public GraphModel Graph { get; }

        public HistoryManager History { get; }

        public NodeGraphViewModel Model { get; }

        public NodeGraphView View { get; }

        public Window Window { get; }

        public static PortScope Create(params GraphNode[] nodes)
        {
            var graph = new GraphModel();
            graph.Nodes.AddRange(nodes);
            return new PortScope(graph);
        }

        public NodePortPoint Socket(INodePort port) => View.GetVisualDescendants().OfType<NodePortPoint>()
            .Single(p => p.DataContext is NodePortViewModel vm && vm.Model == port && p.Tag is null);

        public int Lines() => View.FindControl<Canvas>("canvas")!.Children.OfType<ConnectionLine>().Count();

        public void End(DragEnd end, IPointer pointer)
        {
            switch (end)
            {
                case DragEnd.CaptureLoss:
                    pointer.Capture(null);
                    break;
                case DragEnd.Playback:
                    _playing.Value = true;
                    _playing.Value = false;
                    break;
                case DragEnd.Escape:
                    Window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    Window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                    break;
                case DragEnd.Undo:
                    Assert.That(History.TryCancelGesture(), Is.True);
                    break;
            }

            HeadlessTestHelpers.Render(3);
            Assert.That(History.IsEditing, Is.False, "The end closes the session before any release.");
        }

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
