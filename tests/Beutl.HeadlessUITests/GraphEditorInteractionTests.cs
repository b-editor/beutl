using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Configuration;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Testing.Headless;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorInteractionTests
{
    [AvaloniaTest]
    [TestCase(KeyModifiers.Control, false)]
    [TestCase(KeyModifiers.Meta, false)]
    [TestCase(KeyModifiers.Control, true)]
    [TestCase(KeyModifiers.Meta, true)]
    public async Task Command_drag_selects_without_panning_or_seeking(KeyModifiers command, bool speedGraph)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        var hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        KeyModifiers previousCommand = hotkeys.CommandModifiers;
        hotkeys.CommandModifiers = command;
        try
        {
            graph.Model.IsSpeedGraph.Value = speedGraph;
            graph.Model.CurrentTime.Value = TimeSpan.FromSeconds(0.2);
            HeadlessTestHelpers.Render(3);
            Click(graph, Position(graph, graph.First));
            Assert.That(Selected(graph), Is.EqualTo(new[] { graph.First }));
            Click(graph, Position(graph, graph.Second), RawInputModifiers.Shift);
            Assert.That(Selected(graph), Is.EquivalentTo(new[] { graph.First, graph.Second }));
            Click(graph, Position(graph, graph.First), RawInputModifiers.Shift);
            Assert.That(Selected(graph), Is.EqualTo(new[] { graph.Second }));
            Click(graph, new Point(500, 220));
            Assert.That(Selected(graph), Is.Empty);

            var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
            Vector offset = scroll.Offset;
            TimeSpan currentTime = graph.Model.CurrentTime.Value;
            int undo = graph.Model.HistoryManager.UndoCount;
            Point first = Position(graph, graph.First), second = Position(graph, graph.Second);
            Drag(graph, new Point(first.X - 15, Math.Min(first.Y, second.Y) - 15),
                new Point(second.X + 15, Math.Max(first.Y, second.Y) + 15), CommandModifier);
            Assert.That(Selected(graph), Is.EquivalentTo(new[] { graph.First, graph.Second }));

            // Start inside the existing transform box, away from its handles.
            Drag(graph, second + new Vector(-12, 12), second + new Vector(12, -12), CommandModifier);
            Assert.That(Selected(graph), Is.EqualTo(new[] { graph.Second }));
            // Press above the lower key, inside the plot rather than its horizontal scrollbar.
            Drag(graph, first + new Vector(12, -12), first + new Vector(-12, 12), CommandModifier | RawInputModifiers.Shift);
            Assert.Multiple(() =>
            {
                Assert.That(Selected(graph), Is.EquivalentTo(new[] { graph.First, graph.Second }));
                Assert.That(scroll.Offset, Is.EqualTo(offset));
                Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(currentTime));
                Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
                Assert.That(graph.First.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
                Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            });
            graph.Capture($"command-marquee-{command}-{speedGraph}");
        }
        finally
        {
            hotkeys.CommandModifiers = previousCommand;
        }
    }

    [AvaloniaTest]
    [TestCase(false, 0, 1f)]
    [TestCase(true, 0, 1f)]
    [TestCase(false, 75, 1.5f)]
    [TestCase(true, 75, 1.5f)]
    public async Task Plain_background_drag_seeks_without_scrolling_or_editing_keys(bool speedGraph, double offsetX, float scale)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Second.Easing = new SplineEasing(0.25f, 0, 0.75f, 0.75f);
        graph.Model.Element!.Start = TimeSpan.FromSeconds(0.2);
        graph.Model.HistoryManager.Commit();
        graph.Model.IsSpeedGraph.Value = speedGraph;
        graph.Model.Options.Value = graph.Model.Options.Value with { Scale = scale };
        HeadlessTestHelpers.Render(3);
        graph.Model.ScrollOffset.Value = graph.Model.ScrollOffset.Value.WithX(offsetX);
        HeadlessTestHelpers.Render();
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        var panel = graph.View.FindControl<Panel>("graphPanel")!;
        Vector offset = scroll.Offset;
        int undo = graph.Model.HistoryManager.UndoCount;
        TimeSpan sceneStart = graph.Model.Scene.Start, duration = graph.Model.Scene.Duration;
        var channel = graph.Model.SelectedView.Value!;
        Point first = graph.View.GetKeyPoint(channel.KeyFrames.Single(x => x.Model == graph.First));
        Point second = graph.View.GetKeyPoint(channel.KeyFrames.Single(x => x.Model == graph.Second));
        Point start = panel.TranslatePoint(new Point(TimeSpan.FromSeconds(1).TimeToPixel(scale),
            (first.Y + 2 * second.Y) / 3), graph.Window)!.Value;
        Point end = start + new Vector(-TimeSpan.FromSeconds(0.2).TimeToPixel(scale), -20);
        graph.HitTest(start);
        graph.Window.MouseDown(start, MouseButton.Left);
        Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(1)),
            "Pressing the plot seeks to global time, including scroll and zoom, without subtracting the element start.");
        graph.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.8)));
        graph.Window.MouseUp(end, MouseButton.Left);
        graph.Window.MouseMove(end + new Vector(40, -10));
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(scroll.Offset, Is.EqualTo(offset));
            Assert.That(Selected(graph), Is.EquivalentTo(new[] { graph.First, graph.Second }));
            Assert.That(graph.First.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
            Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(graph.First.Value, Is.EqualTo(100));
            Assert.That(graph.Second.Value, Is.EqualTo(500));
            Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.8)), "Hovering after release must not continue seeking.");
            Assert.That(graph.Model.Scene.Start, Is.EqualTo(sceneStart));
            Assert.That(graph.Model.Scene.Duration, Is.EqualTo(duration));
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
            Assert.That(graph.View.FindControl<GraphEditorSelectionAdorner>("SelectionAdorner")!.Marquee, Is.Null);
        });
        graph.Capture($"plain-seek-{speedGraph}-{offsetX}");

        graph.Window.MouseDown(start, MouseButton.Left);
        Point beforeZero = panel.TranslatePoint(new Point(-10, first.Y), graph.Window)!.Value;
        graph.Window.MouseMove(beforeZero, RawInputModifiers.LeftMouseButton);
        Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(TimeSpan.Zero));
        PressKey(graph, Key.Escape);
        graph.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(end, MouseButton.Left);
        Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(TimeSpan.Zero), "Escape must end the seek gesture.");
    }

    [AvaloniaTest]
    public async Task Selected_keys_move_together_clamp_as_a_group_and_undo_once()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        Click(graph, Position(graph, graph.First));
        Click(graph, Position(graph, graph.Second), RawInputModifiers.Shift);
        int undo = graph.Model.HistoryManager.UndoCount;
        Point start = Position(graph, graph.First);
        Drag(graph, start, start + new Vector(30, -20));
        Assert.Multiple(() =>
        {
            Assert.That(graph.First.KeyTime.TotalSeconds, Is.EqualTo(0.7).Within(0.00001));
            Assert.That(graph.Second.KeyTime.TotalSeconds, Is.EqualTo(1.7).Within(0.00001));
            Assert.That(graph.First.Value, Is.EqualTo(140).Within(0.001));
            Assert.That(graph.Second.Value, Is.EqualTo(540).Within(0.001));
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        });
        Assert.That(graph.Model.HistoryManager.Undo(), Is.True);
        HeadlessTestHelpers.Render();
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        start = Position(graph, graph.First);
        Drag(graph, start, start + new Vector(-120, 0));
        Assert.That(graph.First.KeyTime, Is.EqualTo(TimeSpan.Zero));
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [AvaloniaTest]
    public async Task Crossing_keyframes_preserves_selection_and_escape_rolls_back_drag()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        Point start = Position(graph, graph.First);
        Drag(graph, start, start + new Vector(180, -20));
        Assert.That(graph.Animation.KeyFrames[1], Is.SameAs(graph.First));
        Assert.That(graph.First.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.7)));
        Assert.That(graph.First.Value, Is.EqualTo(140));
        Assert.That(Selected(graph), Is.EqualTo(new[] { graph.First }));
        int undo = graph.Model.HistoryManager.UndoCount;
        start = Position(graph, graph.First);
        graph.Window.MouseDown(start, MouseButton.Left);
        graph.Window.MouseMove(start + new Vector(30, -10), RawInputModifiers.LeftMouseButton);
        PressKey(graph, Key.Escape);
        graph.Window.MouseUp(start + new Vector(30, -10), MouseButton.Left);
        Assert.That(graph.First.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.7)));
        Assert.That(graph.First.Value, Is.EqualTo(140));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        Assert.That(graph.View.KeyTimeMoveState, Is.Null);
    }

    [AvaloniaTest]
    [TestCase(40, 8, 0.7666667, 100)]
    [TestCase(8, -40, 0.5, 180)]
    public async Task Shift_drag_constrains_the_selected_key_instead_of_moving_all(int dx, int dy, double seconds, double value)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        Point start = Position(graph, graph.First);
        Drag(graph, start, start + new Vector(dx, dy), RawInputModifiers.Shift);
        Assert.That(graph.First.KeyTime.TotalSeconds, Is.EqualTo(seconds).Within(0.00001));
        Assert.That(graph.First.Value, Is.EqualTo(value).Within(0.001));
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        Assert.That(graph.Second.Value, Is.EqualTo(500));
    }

    [AvaloniaTest]
    public async Task Easy_ease_in_out_changes_the_correct_side_and_delete_is_atomic()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        var third = new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(2.5),
            Value = 200,
            Easing = new SplineEasing(0.25f, 0.25f, 0.75f, 0.75f)
        };
        graph.Animation.KeyFrames.Add(third);
        graph.Model.HistoryManager.Commit();
        HeadlessTestHelpers.Render();
        Click(graph, Position(graph, graph.Second));
        PressKey(graph, Key.F9, RawInputModifiers.Shift);
        var incoming = (SplineEasing)graph.Second.Easing;
        var outgoing = (SplineEasing)third.Easing;
        Assert.That(incoming.X2, Is.EqualTo(2f / 3));
        Assert.That(incoming.Y2, Is.EqualTo(1));
        Assert.That(outgoing.Y1, Is.EqualTo(1f / 3));
        PressKey(graph, Key.F9, CommandModifier);
        Assert.That(outgoing.X1, Is.EqualTo(1f / 3));
        Assert.That(outgoing.Y1, Is.Zero);
        Assert.That(incoming.Y2, Is.EqualTo(2f / 3));
        PressKey(graph, Key.A, CommandModifier);
        Assert.That(Selected(graph), Has.Length.EqualTo(3));
        int undo = graph.Model.HistoryManager.UndoCount;
        PressKey(graph, Key.Delete);
        Assert.That(graph.Animation.KeyFrames, Is.Empty);
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.Animation.KeyFrames, Has.Count.EqualTo(3));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Ruler_wheel_scrolls_only_the_hovered_axis(bool horizontal, bool shift)
    {
        using var graph = await GraphScope.CreateAsync(selectAll: false);
        graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 2 };
        graph.Model.ScaleY.Value = 2;
        HeadlessTestHelpers.Render(3);
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        Control ruler = horizontal ? graph.View.FindControl<Border>("RulerBar")!
            : graph.View.FindControl<GraphEditorScale>("verticalScale")!;
        Point pointer = ruler.TranslatePoint(horizontal ? new Point(180, 16) : new Point(20, 160), graph.Window)!.Value;
        graph.HitTest(pointer);
        int undo = graph.Model.HistoryManager.UndoCount;
        TimeSpan playhead = graph.Model.CurrentTime.Value;
        foreach (Vector delta in new[] { new Vector(0, -1), new Vector(-1, 0), new Vector(-2, -1) })
        {
            graph.Model.ScrollOffset.Value = new Vector(75, 80);
            HeadlessTestHelpers.Render();
            graph.Window.MouseWheel(pointer, delta, shift ? RawInputModifiers.Shift : RawInputModifiers.None);
            HeadlessTestHelpers.Render();
            double amount = horizontal && delta.X != 0 ? delta.X : delta.Y != 0 ? delta.Y : delta.X;
            Assert.That(scroll.Offset.X, Is.EqualTo(horizontal ? 75 - amount * 50 : 75).Within(0.01));
            Assert.That(scroll.Offset.Y, Is.EqualTo(horizontal ? 80 : 80 - amount * 50).Within(0.01));
            Assert.That(graph.Model.Options.Value.Scale, Is.EqualTo(2));
            Assert.That(graph.Model.ScaleY.Value, Is.EqualTo(2));
        }
        Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(playhead));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        graph.Capture($"ruler-scroll-{horizontal}-{shift}");
    }

    [AvaloniaTest]
    [TestCase(false, KeyModifiers.Control, false)]
    [TestCase(false, KeyModifiers.Meta, false)]
    [TestCase(true, KeyModifiers.Control, false)]
    [TestCase(true, KeyModifiers.Meta, false)]
    [TestCase(true, KeyModifiers.Meta, true)]
    public async Task Ruler_zoom_uses_the_hovered_axis_and_keeps_the_pointer_anchor(bool horizontal, KeyModifiers command, bool autoHeight)
    {
        using var graph = await GraphScope.CreateAsync(light: command == KeyModifiers.Control, selectAll: false);
        var hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        KeyModifiers previousCommand = hotkeys.CommandModifiers;
        hotkeys.CommandModifiers = command;
        try
        {
            graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 1 };
            graph.Model.ScaleY.Value = 2;
            graph.Model.AutoZoomHeight.Value = autoHeight;
            HeadlessTestHelpers.Render(3);
            graph.Model.ScrollOffset.Value = new Vector(75, autoHeight ? graph.Model.ScrollOffset.Value.Y : 80);
            HeadlessTestHelpers.Render();
            var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
            Control ruler = horizontal ? graph.View.FindControl<Border>("RulerBar")!
                : graph.View.FindControl<GraphEditorScale>("verticalScale")!;
            Point local = horizontal ? new Point(180, 16) : new Point(20, 160);
            Point pointer = ruler.TranslatePoint(local, graph.Window)!.Value;
            Point plotPointer = ruler.TranslatePoint(local, scroll)!.Value;
            graph.HitTest(pointer);
            int undo = graph.Model.HistoryManager.UndoCount;
            TimeSpan playhead = graph.Model.CurrentTime.Value;
            foreach (RawInputModifiers modifier in new[] { RawInputModifiers.Alt, CommandModifier, CommandModifier | RawInputModifiers.Alt })
                foreach (Vector delta in new[] { new Vector(0, 1), new Vector(-1, 0) })
                {
                    float scaleX = graph.Model.Options.Value.Scale;
                    double scaleY = graph.Model.ScaleY.Value;
                    Vector offset = scroll.Offset;
                    double time = (offset.X + plotPointer.X) / scaleX;
                    double value = (graph.Model.Baseline.Value - offset.Y - plotPointer.Y) / scaleY;
                    graph.Window.MouseWheel(pointer, delta, modifier);
                    HeadlessTestHelpers.Render();
                    double factor = Math.Pow(1.2, delta.Y != 0 ? delta.Y : delta.X);
                    Assert.That(graph.Model.Options.Value.Scale, Is.EqualTo(horizontal ? scaleX * factor : scaleX).Within(0.001));
                    Assert.That(graph.Model.ScaleY.Value, Is.EqualTo(horizontal ? scaleY : scaleY * factor).Within(0.001));
                    if (horizontal)
                    {
                        Assert.That((scroll.Offset.X + plotPointer.X) / graph.Model.Options.Value.Scale, Is.EqualTo(time).Within(0.01));
                        Assert.That(scroll.Offset.Y, Is.EqualTo(offset.Y).Within(0.01));
                    }
                    else
                    {
                        Assert.That((graph.Model.Baseline.Value - scroll.Offset.Y - plotPointer.Y) / graph.Model.ScaleY.Value,
                            Is.EqualTo(value).Within(0.01));
                        Assert.That(scroll.Offset.X, Is.EqualTo(offset.X).Within(0.01));
                    }
                    if (modifier == CommandModifier && delta.Y > 0)
                        graph.Capture($"ruler-zoom-{horizontal}-{command}-{autoHeight}");
                }
            Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(playhead));
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        }
        finally
        {
            hotkeys.CommandModifiers = previousCommand;
        }
    }

    [AvaloniaTest]
    public async Task Ruler_zoom_at_the_time_scale_limit_does_not_pan()
    {
        using var graph = await GraphScope.CreateAsync(selectAll: false);
        graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 2 };
        graph.Model.ScaleY.Value = 2;
        HeadlessTestHelpers.Render(3);
        graph.Model.ScrollOffset.Value = new Vector(75, 80);
        HeadlessTestHelpers.Render();
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        var ruler = graph.View.FindControl<Border>("RulerBar")!;
        Point pointer = ruler.TranslatePoint(new Point(180, 16), graph.Window)!.Value;
        graph.HitTest(pointer);
        Vector before = scroll.Offset;
        graph.Window.MouseWheel(pointer, new Vector(0, 1), CommandModifier);
        HeadlessTestHelpers.Render();
        Assert.That(graph.Model.Options.Value.Scale, Is.EqualTo(2));
        Assert.That(scroll.Offset, Is.EqualTo(before));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Plot_wheel_respects_the_timeline_scroll_preference(bool swapped)
    {
        using var graph = await GraphScope.CreateAsync(selectAll: false);
        var config = GlobalConfiguration.Instance.EditorConfig;
        bool previous = config.SwapTimelineScrollDirection;
        try
        {
            config.SwapTimelineScrollDirection = swapped;
            graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 2 };
            graph.Model.ScaleY.Value = 2;
            HeadlessTestHelpers.Render(3);
            var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
            Point pointer = scroll.TranslatePoint(new Point(180, 160), graph.Window)!.Value;
            graph.HitTest(pointer);
            foreach (var delta in new[] { new Vector(0, -1), new Vector(-1, 0), new Vector(-2, -1) })
            {
                graph.Model.ScrollOffset.Value = new Vector(75, 80);
                HeadlessTestHelpers.Render();
                graph.Window.MouseWheel(pointer, delta);
                HeadlessTestHelpers.Render();
                Assert.That(scroll.Offset.X, Is.EqualTo(75 - (swapped ? delta.X : delta.Y) * 50).Within(0.01));
                Assert.That(scroll.Offset.Y, Is.EqualTo(80 - (swapped ? delta.Y : delta.X) * 50).Within(0.01));
            }
        }
        finally { config.SwapTimelineScrollDirection = previous; }
    }

    [AvaloniaTest]
    [TestCase(KeyModifiers.Control, false)]
    [TestCase(KeyModifiers.Meta, false)]
    [TestCase(KeyModifiers.Control, true)]
    [TestCase(KeyModifiers.Meta, true)]
    public async Task Plot_command_wheel_zooms_time_and_shift_zooms_value(KeyModifiers command, bool shift)
    {
        using var graph = await GraphScope.CreateAsync(selectAll: false);
        var hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        KeyModifiers previous = hotkeys.CommandModifiers;
        hotkeys.CommandModifiers = command;
        try
        {
            var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
            graph.Model.ScaleY.Value = 2;
            HeadlessTestHelpers.Render(3);
            graph.Model.ScrollOffset.Value = new Vector(75, 80);
            HeadlessTestHelpers.Render();
            Point pointer = scroll.TranslatePoint(new Point(180, 160), graph.Window)!.Value;
            graph.HitTest(pointer);
            double time = (scroll.Offset.X + 180) / graph.Model.Options.Value.Scale;
            double value = (graph.Model.Baseline.Value - scroll.Offset.Y - 160) / graph.Model.ScaleY.Value;
            graph.Window.MouseWheel(pointer, new Vector(0, 1), CommandModifier | (shift ? RawInputModifiers.Shift : RawInputModifiers.None));
            HeadlessTestHelpers.Render();
            Assert.That(graph.Model.Options.Value.Scale, Is.EqualTo(shift ? 1 : 1.2).Within(0.001));
            Assert.That(graph.Model.ScaleY.Value, Is.EqualTo(shift ? 2.4 : 2).Within(0.001));
            Assert.That((scroll.Offset.X + 180) / graph.Model.Options.Value.Scale, Is.EqualTo(time).Within(0.01));
            Assert.That((graph.Model.Baseline.Value - scroll.Offset.Y - 160) / graph.Model.ScaleY.Value, Is.EqualTo(value).Within(0.01));
        }
        finally { hotkeys.CommandModifiers = previous; }
    }

    [AvaloniaTest]
    public async Task Wheel_mapping_and_pointer_anchored_zoom_match_graph_navigation()
    {
        using var graph = await GraphScope.CreateAsync(selectAll: false);
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        var panel = graph.View.FindControl<Panel>("graphPanel")!;
        Point pointer = scroll.TranslatePoint(new Point(180, 160), graph.Window)!.Value;
        graph.HitTest(pointer);
        graph.Window.MouseWheel(pointer, new Vector(0, -1));
        HeadlessTestHelpers.Render();
        Assert.That(scroll.Offset.X, Is.GreaterThan(0));
        graph.Window.MouseWheel(pointer, new Vector(-1, 0));
        HeadlessTestHelpers.Render();
        Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
        double time = (scroll.Offset.X + 180) / graph.Model.Options.Value.Scale;
        graph.Window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.Alt);
        HeadlessTestHelpers.Render();
        Assert.That((scroll.Offset.X + 180) / graph.Model.Options.Value.Scale, Is.EqualTo(time).Within(0.01));
        double value = (graph.Model.Baseline.Value - scroll.Offset.Y - 160) / graph.Model.ScaleY.Value;
        graph.Window.MouseWheel(pointer, new Vector(0, 1), CommandModifier | RawInputModifiers.Shift);
        HeadlessTestHelpers.Render();
        Assert.That((graph.Model.Baseline.Value - scroll.Offset.Y - 160) / graph.Model.ScaleY.Value, Is.EqualTo(value).Within(0.01));
        var before = scroll.Offset;
        TimeSpan playheadBeforePan = graph.Model.CurrentTime.Value;
        graph.Window.MouseDown(pointer, MouseButton.Middle);
        graph.Window.MouseMove(pointer + new Vector(-20, -10), RawInputModifiers.MiddleMouseButton);
        graph.Window.MouseUp(pointer + new Vector(-20, -10), MouseButton.Middle);
        HeadlessTestHelpers.Render();
        Assert.That(scroll.Offset.X, Is.EqualTo(before.X + 20).Within(0.01));
        Assert.That(scroll.Offset.Y, Is.EqualTo(before.Y + 10).Within(0.01));
        Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(playheadBeforePan));

        before = scroll.Offset;
        graph.View.Focus();
        graph.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        graph.Window.MouseDown(pointer, MouseButton.Left);
        graph.Window.MouseMove(pointer + new Vector(-10, -10), RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(pointer + new Vector(-10, -10), MouseButton.Left);
        graph.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        HeadlessTestHelpers.Render();
        Assert.That(scroll.Offset.X, Is.EqualTo(before.X + 10).Within(0.01));
        Assert.That(scroll.Offset.Y, Is.EqualTo(before.Y + 10).Within(0.01));
        Assert.That(graph.Model.CurrentTime.Value, Is.EqualTo(playheadBeforePan));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Speed_graph_edits_influence_without_changing_key_values(bool light)
    {
        using var graph = await GraphScope.CreateAsync(light, separateHandles: true, selectAll: false);
        Click(graph, Position(graph, graph.First));
        Click(graph, Position(graph, graph.Second), RawInputModifiers.Shift);
        PressKey(graph, Key.F9);
        var graphType = graph.View.FindControl<ComboBox>("GraphTypePicker")!;
        graphType.SelectedIndex = 1;
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.Model.IsSpeedGraph.Value, Is.True);
        var speed = graph.View.FindControl<GraphEditorSpeedGraph>("SpeedGraph")!;
        var panel = graph.View.FindControl<Panel>("graphPanel")!;
        double scale = graph.Model.Options.Value.Scale;
        double left = graph.First.KeyTime.TimeToPixel((float)scale);
        double right = graph.Second.KeyTime.TimeToPixel((float)scale);
        var easing = (SplineEasing)graph.Second.Easing;
        var handle = panel.TranslatePoint(new Point(left + easing.X1 * (right - left), graph.Model.Baseline.Value), graph.Window)!.Value;
        graph.HitTest(handle);
        Drag(graph, handle, handle + new Vector(25, 0), RawInputModifiers.Alt);
        Assert.That(easing.X1, Is.GreaterThan(1f / 3));
        Assert.That(easing.Y1, Is.Zero.Within(0.001));
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Second.Value, Is.EqualTo(500));
        graph.Capture($"speed-{light}");
        graph.Model.IsSpeedGraph.Value = false;
        HeadlessTestHelpers.Render(3);
        Assert.That(graphType.SelectedIndex, Is.Zero);
        graph.Capture($"value-{light}");
    }

    [AvaloniaTest]
    public async Task Transform_box_scales_time_and_value_and_is_one_undo()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        Click(graph, Position(graph, graph.First));
        Click(graph, Position(graph, graph.Second), RawInputModifiers.Shift);
        int undo = graph.Model.HistoryManager.UndoCount;
        // Bottom-right corner is away from both keyframes and their direction handles.
        Point first = Position(graph, graph.First), second = Position(graph, graph.Second);
        Drag(graph, new Point(second.X, first.Y), new Point(second.X + 75, first.Y + 50));
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(graph.First.Value, Is.EqualTo(0).Within(0.001));
        Assert.That(graph.Second.Value, Is.EqualTo(500));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
    }

    private static RawInputModifiers CommandModifier => KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
        ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static IKeyFrame[] Selected(GraphScope graph) => graph.Model.SelectedView.Value!.KeyFrames
        .Where(x => x.IsSelected.Value).Select(x => x.Model).ToArray();

    private static Point Position(GraphScope graph, IKeyFrame key)
    {
        HeadlessTestHelpers.Render();
        if (!graph.Model.IsSpeedGraph.Value)
            return graph.KeyFrame(key).TranslatePoint(default, graph.Window)!.Value;
        var item = graph.Model.SelectedView.Value!.KeyFrames.Single(x => x.Model == key);
        return graph.View.FindControl<Panel>("graphPanel")!.TranslatePoint(graph.View.GetKeyPoint(item), graph.Window)!.Value;
    }

    private static void Click(GraphScope graph, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        graph.HitTest(point);
        graph.Window.MouseMove(point, modifiers);
        graph.Window.MouseDown(point, MouseButton.Left, modifiers);
        graph.Window.MouseUp(point, MouseButton.Left, modifiers);
        HeadlessTestHelpers.Render();
    }

    private static void Drag(GraphScope graph, Point start, Point end, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        graph.HitTest(start);
        graph.Window.MouseMove(start, modifiers);
        graph.Window.MouseDown(start, MouseButton.Left, modifiers);
        graph.Window.MouseMove(end, modifiers | RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(end, MouseButton.Left, modifiers);
        HeadlessTestHelpers.Render();
    }

    private static void PressKey(GraphScope graph, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        graph.Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        graph.Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        HeadlessTestHelpers.Render();
    }
}
