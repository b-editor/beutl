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
    [TestCase(false, 0.5d)]
    [TestCase(false, 1.5d)]
    [TestCase(true, 0.5d)]
    [TestCase(true, 1.5d)]
    public void Dropping_anywhere_on_a_row_connects_its_port(bool reverse, double scale)
    {
        var source = new RandomSingleNode { Position = { Value = (20, 40) } };
        var target = new RandomSingleNode { Position = { Value = (260, 40) } };
        using var scope = PortScope.Create(source, target);
        scope.Zoom(scale);
        using var feedback = new HapticFeedbackRecorder();
        INodePort from = reverse ? target.Maximum : source.Value;
        INodePort to = reverse ? source.Value : target.Maximum;
        Point start = scope.Socket(from).TranslatePoint(new Point(5, 5), scope.Window)!.Value;

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        foreach (double across in new[] { 0.5, 0.02, 0.98 })
        {
            scope.Window.MouseMove(scope.OnRow(to, across), RawInputModifiers.LeftMouseButton);
            Assert.That(feedback.Pulses, Has.Count.EqualTo(1), "The row is one target, entered once.");
            Assert.That(scope.Graph.AllConnections, Is.Empty, "Hover must not create a connection.");
        }
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(scope.Window, nameof(NodeGraphDragTests), $"row-hover-{reverse}");
        scope.Window.MouseUp(scope.OnRow(to, 0.98), MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(scope.Window, nameof(NodeGraphDragTests), $"row-connection-{reverse}");

        Assert.Multiple(() =>
        {
            Assert.That(scope.Graph.AllConnections.Single().Input.Value.Value, Is.SameAs(target.Maximum));
            Assert.That(scope.Graph.AllConnections.Single().Output.Value.Value, Is.SameAs(source.Value));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
            Assert.That(scope.View.PortDropMenu?.IsOpen, Is.Not.True);
        });
    }

    [AvaloniaTest]
    [TestCase(false, 0.5d)]
    [TestCase(false, 1.5d)]
    [TestCase(true, 0.5d)]
    [TestCase(true, 1.5d)]
    public void A_wire_snaps_to_a_port_it_can_connect_to_and_lands_where_it_snapped(bool reverse, double scale)
    {
        var source = new RandomSingleNode { Position = { Value = (20, 40) } };
        var target = new RandomSingleNode { Position = { Value = (260, 40) } };
        using var scope = PortScope.Create(source, target);
        scope.Zoom(scale);
        INodePort from = reverse ? target.Maximum : source.Value;
        INodePort to = reverse ? source.Value : target.Maximum;
        INodePort sameSide = reverse ? target.Minimum : target.Value;
        Point start = scope.Socket(from).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point over = scope.OnRow(to, 0.98);
        Point snapped = scope.PortCenter(to);

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(over, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(reverse, snapped, "Over the row");
        Point cannot = scope.OnRow(sameSide, 0.5);
        scope.Window.MouseMove(cannot, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(reverse, scope.ToCanvas(cannot), "Over a port it cannot connect to");
        Point blank = new(720, 460);
        scope.Window.MouseMove(blank, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(reverse, scope.ToCanvas(blank), "Off the node");
        scope.Window.MouseMove(over, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(reverse, snapped, "Back over the row");
        scope.Window.MouseUp(over, MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        ConnectionViewModel connection = scope.Model.AllConnections.Single();
        Point landed = reverse ? connection.OutputPortPosition.Value : connection.InputPortPosition.Value;
        Assert.That(Point.Distance(landed, snapped), Is.LessThan(0.01), "The wire must not jump when it connects.");
    }

    [AvaloniaTest]
    public void A_dragged_list_slot_snaps_only_to_a_port_it_can_move_to()
    {
        var first = new ExpressionNode { Position = { Value = (20, 20) } };
        var third = new ExpressionNode { Position = { Value = (20, 240) } };
        var target = new ExpressionNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(first, third, target);
        scope.Graph.Connect(target.InputPort, first.Output);
        scope.History.Commit();
        HeadlessTestHelpers.Render(3);
        NodePortPoint slot = scope.View.GetVisualDescendants().OfType<NodePortPoint>()
            .Single(p => p.Tag is ConnectionViewModel && p.DataContext is InputPortViewModel input && input.Model == target.InputPort);
        Point start = slot.TranslatePoint(new Point(5, 5), scope.Window)!.Value;

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(start + new Vector(-20, 0), RawInputModifiers.LeftMouseButton);
        void Hover(Point pointer, Point expected, string message)
        {
            scope.Window.MouseMove(pointer, RawInputModifiers.LeftMouseButton);
            scope.AssertWireEnd(fromInput: true, expected, message);
        }
        Point OnRow(INodePort port, out Point onCanvas)
        {
            Point point = scope.OnRow(port, 0.5);
            onCanvas = scope.ToCanvas(point);
            return point;
        }

        Hover(scope.OnRow(third.Output, 0.5), scope.PortCenter(third.Output), "Another output");
        Assert.That(Point.Distance(scope.Wire().StartPoint, scope.ToCanvas(start)), Is.LessThan(0.01),
            "An input's wire starts at the input, as a connection's does.");
        Hover(OnRow(first.Output, out Point feeding), feeding, "The output that already feeds the slot");
        Hover(OnRow(third.InputPort, out Point input), input, "An input");
        Hover(OnRow(target.Output, out Point cycle), cycle, "An output that would close a cycle");
        scope.Window.MouseUp(new Point(720, 460), MouseButton.Left);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Dropping_on_a_node_outside_its_rows_connects_nothing(bool header)
    {
        var source = new RandomSingleNode { Position = { Value = (20, 40) } };
        var target = new RandomSingleNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(source, target);
        using var feedback = new HapticFeedbackRecorder();
        GraphNodeView node = scope.NodeView(target);
        Point start = scope.Socket(source.Value).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point end = header
            ? HeadlessTestHelpers.Center(node.FindControl<Border>("handle")!, scope.Window)
            : node.TranslatePoint(new Point(node.Bounds.Width / 2, node.Bounds.Height - 2), scope.Window)!.Value;

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        scope.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Graph.AllConnections, Is.Empty);
            Assert.That(feedback.Pulses, Is.Empty);
            Assert.That(scope.View.PortDropMenu?.IsOpen, Is.Not.True, "A node is not the empty graph.");
        });
    }

    [AvaloniaTest]
    public void Dropping_on_a_list_port_row_adds_to_its_connections()
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
        Point start = scope.Socket(source.Output).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point end = scope.OnRow(target.InputPort, 0.5);
        Point placeholder = scope.PortCenter(target.InputPort);

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(fromInput: false, placeholder, "Over the row, the new connection takes the free place");
        scope.Window.MouseMove(slot.TranslatePoint(new Point(5, 5), scope.Window)!.Value, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(fromInput: false, placeholder, "Over a slot, the new connection still goes after it");
        scope.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        HeadlessTestHelpers.Render(3);

        Assert.That(target.InputPort.Connections.Select(c => c.Value!.Output.Value.Value),
            Is.EqualTo(new object[] { first.Output, source.Output }));
        Point landed = scope.Model.AllConnections.Single(c => c.Connection.Output.Value.Value == source.Output).InputPortPosition.Value;
        Assert.That(Point.Distance(landed, placeholder), Is.LessThan(0.01), "The wire must not jump when it connects.");
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_wire_snaps_to_the_row_of_an_undecided_port_and_adds_a_port(bool fromUndecided)
    {
        var target = new RandomSingleNode { Position = { Value = (20, 40) } };
        var dynamicNode = new LayerInputNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(target, dynamicNode);
        using var feedback = new HapticFeedbackRecorder();
        INodePort? from = fromUndecided ? null : target.Maximum;
        INodePort? to = fromUndecided ? target.Maximum : null;
        Point start = scope.Socket(from).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point end = scope.OnRow(to, 0.5);

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(fromInput: !fromUndecided, scope.PortCenter(to), "Over the row");
        Assert.That(feedback.Pulses, Has.Count.EqualTo(1));
        scope.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        Assert.That(dynamicNode.Items, Has.Count.EqualTo(1));
        Assert.That(scope.Graph.AllConnections.Single().Input.Value.Value, Is.SameAs(target.Maximum));
        Assert.That(scope.Graph.AllConnections.Single().Output.Value.Value, Is.SameAs(dynamicNode.Items[0]));
    }

    [AvaloniaTest]
    public void A_wire_from_the_side_an_undecided_port_offers_neither_snaps_nor_adds_a_port()
    {
        var source = new RandomSingleNode { Position = { Value = (20, 40) } };
        var dynamicNode = new LayerInputNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(source, dynamicNode);
        using var feedback = new HapticFeedbackRecorder();
        Point start = scope.Socket(source.Value).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point end = scope.OnRow(null, 0.5);

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        scope.AssertWireEnd(fromInput: false, scope.ToCanvas(end), "A new output takes no output.");
        scope.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        Assert.That(feedback.Pulses, Is.Empty);
        Assert.That(dynamicNode.Items, Is.Empty);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Dragging_over_an_occupied_input_does_not_pulse_or_change_its_connection(bool onRow)
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
        Point target = onRow
            ? scope.OnRow(output.InputPort, 0.5)
            : scope.Socket(output.InputPort).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Assert.That(feedback.Pulses, Is.Empty);
        scope.AssertWireEnd(fromInput: false, scope.ToCanvas(target), "An occupied input does not take the wire.");
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

    // macOS users add to a selection with Cmd, where Ctrl+click means a right click.
    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control)]
    [TestCase(RawInputModifiers.Meta)]
    public void Clicking_a_node_with_the_command_key_toggles_its_selection(RawInputModifiers modifier)
    {
        var node = new ExpressionNode { Position = { Value = (20, 40) } };
        using var scope = PortScope.Create(node);
        GraphNodeViewModel model = scope.Model.Nodes.Single();
        Border handle = scope.View.GetVisualDescendants().OfType<GraphNodeView>().Single().FindControl<Border>("handle")!;
        Point point = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), scope.Window)!.Value;

        scope.Window.MouseMove(point);
        scope.Window.MouseDown(point, MouseButton.Left, modifier);
        scope.Window.MouseUp(point, MouseButton.Left, modifier);
        HeadlessTestHelpers.Render(3);
        Assert.That(model.IsSelected.Value, Is.True);

        scope.Window.MouseDown(point, MouseButton.Left, modifier);
        scope.Window.MouseUp(point, MouseButton.Left, modifier);
        HeadlessTestHelpers.Render(3);
        Assert.That(model.IsSelected.Value, Is.False);
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

    public enum SlotDrop { OwnPlaceholder, OwnRow, AnotherInput, AnotherInputRow, AnotherOutput, AnotherOutputRow }

    [AvaloniaTest]
    [TestCase(SlotDrop.OwnPlaceholder)]
    [TestCase(SlotDrop.OwnRow)]
    [TestCase(SlotDrop.AnotherInput)]
    [TestCase(SlotDrop.AnotherInputRow)]
    [TestCase(SlotDrop.AnotherOutput)]
    [TestCase(SlotDrop.AnotherOutputRow)]
    public void Dragging_a_list_slot_sideways_rewires_it_in_place_or_keeps_it(SlotDrop drop)
    {
        var first = new ExpressionNode { Position = { Value = (20, 20) } };
        var second = new ExpressionNode { Position = { Value = (20, 220) } };
        var third = new ExpressionNode { Position = { Value = (20, 400) } };
        var target = new ExpressionNode { Position = { Value = (380, 40) } };
        using var scope = PortScope.Create(first, second, third, target);
        scope.Graph.Connect(target.InputPort, first.Output);
        scope.Graph.Connect(target.InputPort, second.Output);
        scope.History.Commit();
        HeadlessTestHelpers.Render(3);
        int undoCount = scope.History.UndoCount;
        NodePortPoint slot = scope.View.GetVisualDescendants().OfType<NodePortPoint>()
            .Where(p => p.Tag is ConnectionViewModel && p.DataContext is InputPortViewModel input && input.Model == target.InputPort)
            .MinBy(p => p.TranslatePoint(default, scope.Window)!.Value.Y)!;
        Point OnPoint(INodePort port) => scope.Socket(port).TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point start = slot.TranslatePoint(new Point(5, 5), scope.Window)!.Value;
        Point end = drop switch
        {
            SlotDrop.OwnPlaceholder => OnPoint(target.InputPort),
            SlotDrop.OwnRow => scope.OnRow(target.InputPort, 0.5),
            SlotDrop.AnotherInput => OnPoint(first.InputPort),
            SlotDrop.AnotherInputRow => scope.OnRow(first.InputPort, 0.5),
            SlotDrop.AnotherOutput => OnPoint(third.Output),
            _ => scope.OnRow(third.Output, 0.5),
        };

        scope.Window.MouseMove(start);
        scope.Window.MouseDown(start, MouseButton.Left);
        scope.Window.MouseMove(start + new Vector(-20, 0), RawInputModifiers.LeftMouseButton);
        scope.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.That(scope.History.IsEditing, Is.True, "The drag must hold an edit session.");
        scope.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render(3);

        bool rewired = drop is SlotDrop.AnotherOutput or SlotDrop.AnotherOutputRow;
        Assert.Multiple(() =>
        {
            Assert.That(SlotSources(), Is.EqualTo(rewired
                ? new object[] { third.Output, second.Output }
                : new object[] { first.Output, second.Output }));
            Assert.That(scope.History.UndoCount, Is.EqualTo(undoCount + (rewired ? 1 : 0)));
            Assert.That(scope.History.IsEditing, Is.False);
            Assert.That(scope.Lines(), Is.EqualTo(2), "The drag wire is gone and each slot keeps its wire.");
        });

        if (rewired)
        {
            scope.History.Undo();
            Assert.That(SlotSources(), Is.EqualTo(new object[] { first.Output, second.Output }), "One Undo restores the slot.");
        }

        object?[] SlotSources() => [.. target.InputPort.Connections.Select(c => c.Value!.Output.Value.Value)];
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

        // A null port is the point that a dynamic node offers for a new port.
        public NodePortPoint Socket(INodePort? port) => View.GetVisualDescendants().OfType<NodePortPoint>()
            .Single(p => p.DataContext is NodePortViewModel vm && vm.Model == port && p.Tag is null);

        public Point PortCenter(INodePort? port) => Socket(port).TranslatePoint(new Point(5, 5), Canvas)!.Value;

        public Point ToCanvas(Point windowPoint) => Window.TranslatePoint(windowPoint, Canvas)!.Value;

        // The wire being dragged, which unlike a connection's has no view model.
        public ConnectionLine Wire() => Canvas.Children.OfType<ConnectionLine>().Single(line => line.ConnectionViewModel == null);

        public void AssertWireEnd(bool fromInput, Point expected, string message)
        {
            ConnectionLine wire = Wire();
            Assert.That(Point.Distance(fromInput ? wire.EndPoint : wire.StartPoint, expected), Is.LessThan(0.01), message);
        }

        public GraphNodeView NodeView(GraphNode node) => View.GetVisualDescendants().OfType<GraphNodeView>()
            .Single(view => view.DataContext is GraphNodeViewModel vm && vm.GraphNode == node);

        // A point at half the height of a port's row, the given fraction across its width. A null port is the row
        // that a dynamic node offers for a new port.
        public Point OnRow(INodePort? port, double across)
        {
            NodePortView row = View.GetVisualDescendants().OfType<NodePortView>()
                .Single(view => view.DataContext is NodePortViewModel vm && vm.Model == port);
            return row.TranslatePoint(new Point(row.Bounds.Width * across, row.Bounds.Height / 2), Window)!.Value;
        }

        public void Zoom(double scale)
        {
            View.FindControl<ZoomBorder>("zoomBorder")!.SetMatrix(Matrix.CreateScale(scale, scale), true);
            HeadlessTestHelpers.Render(3);
        }

        public int Lines() => Canvas.Children.OfType<ConnectionLine>().Count();

        private Canvas Canvas => View.FindControl<Canvas>("canvas")!;

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
