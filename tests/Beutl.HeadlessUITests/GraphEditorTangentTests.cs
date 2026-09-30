using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Testing.Headless;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorTangentTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Ease_buttons_produce_distinct_curves_from_the_same_linear_state(bool speed)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Model.IsSpeedGraph.Value = speed;
        HeadlessTestHelpers.Render(3);
        var midpointValues = new Dictionary<string, float>();
        foreach (string mode in new[] { "Ease", "EaseIn", "EaseOut" })
        {
            ChoosePreset(graph, "Linear", false);
            ChoosePreset(graph, mode, false);
            midpointValues[mode] = graph.Second.Easing.Ease(0.5f);
            graph.View.Focus();
            graph.Window.KeyPress(Key.F, RawInputModifiers.None, PhysicalKey.None, "f");
            graph.Window.KeyRelease(Key.F, RawInputModifiers.None, PhysicalKey.None, "f");
            HeadlessTestHelpers.Render(3);
            graph.Capture($"fresh-linear-{mode}-{speed}");
        }
        Assert.That(midpointValues["Ease"], Is.EqualTo(0.5f).Within(0.001));
        Assert.That(midpointValues["EaseIn"], Is.EqualTo(0.625f).Within(0.001));
        Assert.That(midpointValues["EaseOut"], Is.EqualTo(0.375f).Within(0.001));
    }

    [AvaloniaTest]
    [TestCase("Symmetry", false)]
    [TestCase("Asymmetry", false)]
    [TestCase("Separately", false)]
    [TestCase("Symmetry", true)]
    [TestCase("Asymmetry", true)]
    [TestCase("Separately", true)]
    public async Task Expanding_Kf2_Kf3_right_edge_respects_Kf2_ControlPoint2_mode(string mode, bool speed)
    {
        using var graph = await CreateChain(speed);
        graph.Animation.KeyFrames.RemoveAt(3);
        graph.Model.HistoryManager.Commit();
        var channel = graph.Model.SelectedView.Value!;
        var kf3 = graph.Animation.KeyFrames[2];
        SetMode(graph, mode);
        channel.SetSelection([graph.Second, kf3]);
        HeadlessTestHelpers.Render(3);
        var easing = (SplineEasing)graph.Second.Easing;
        float x1 = easing.X1, y1 = easing.Y1, x2 = easing.X2, y2 = easing.Y2;
        Point originalCp2 = Incoming(graph, graph.Second);
        Point kf2Position = Position(graph, graph.Second), kf3Position = Position(graph, kf3);
        Point rightEdge = new(kf3Position.X, (kf2Position.Y + kf3Position.Y) / 2);
        int undo = graph.Model.HistoryManager.UndoCount;
        graph.HitTest(rightEdge);
        graph.Window.MouseDown(rightEdge, MouseButton.Left);
        foreach (double distance in new[] { 10d, 20d, 30d })
        {
            graph.Window.MouseMove(rightEdge + new Vector(distance, 0), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(graph.Second.Value, Is.EqualTo(300));
            AssertCoupling(mode, speed, Outgoing(graph, graph.Second), originalCp2, Incoming(graph, graph.Second));
            Assert.That(easing.X1, Is.EqualTo(x1), "Kf1's outgoing handle must stay unchanged.");
            Assert.That(easing.Y1, Is.EqualTo(y1));
            if (mode == "Separately")
            {
                Assert.That(easing.X2, Is.EqualTo(x2), "Kf2.ControlPoint2.X must not move when expanding Kf2-Kf3.");
                Assert.That(easing.Y2, Is.EqualTo(y2), "Kf2.ControlPoint2.Y must not move when expanding Kf2-Kf3.");
            }
        }
        graph.Window.MouseUp(rightEdge + new Vector(30, 0), MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        Assert.That(kf3.KeyTime.TotalSeconds, Is.EqualTo(2.8333333).Within(0.00001));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Capture($"three-key-cp2-{mode}-{speed}");
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        Assert.That(kf3.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
        Assert.That(((SplineEasing)graph.Second.Easing).X2, Is.EqualTo(x2));
        Assert.That(((SplineEasing)graph.Second.Easing).Y2, Is.EqualTo(y2));
    }

    [AvaloniaTest]
    [TestCase("Symmetry", false)]
    [TestCase("Asymmetry", false)]
    [TestCase("Separately", false)]
    [TestCase("Symmetry", true)]
    [TestCase("Asymmetry", true)]
    [TestCase("Separately", true)]
    public async Task Transform_box_respects_link_mode_at_selection_boundaries(string mode, bool speed)
    {
        using var graph = await CreateChain(speed);
        var channel = graph.Model.SelectedView.Value!;
        IKeyFrame middle = graph.Second, next = graph.Animation.KeyFrames[2], last = graph.Animation.KeyFrames[3];
        SetMode(graph, mode);
        channel.SetSelection([middle, next]);
        HeadlessTestHelpers.Render();
        Point firstFar = Outgoing(graph, graph.First), lastFar = Incoming(graph, last);
        Point leftOutside = Incoming(graph, middle), rightOutside = Outgoing(graph, next);
        int undo = graph.Model.HistoryManager.UndoCount;
        Point a = Position(graph, middle), b = Position(graph, next);
        Point from = new(b.X, (a.Y + b.Y) / 2);
        Drag(graph, from, from + new Vector(30, 0));

        AssertCoupling(mode, speed, Outgoing(graph, middle), leftOutside, Incoming(graph, middle));
        AssertCoupling(mode, speed, Incoming(graph, next), rightOutside, Outgoing(graph, next));
        AssertPoint(Outgoing(graph, graph.First), firstFar, "The unselected starting key's handle must not move.");
        AssertPoint(Incoming(graph, last), lastFar, "The unselected ending key's handle must not move.");
        Assert.That(middle.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        Assert.That(next.KeyTime.TotalSeconds, Is.EqualTo(2.8333333).Within(0.00001));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Capture($"box-tangents-{mode}-{speed}");
        graph.Model.HistoryManager.Undo();
        AssertPoint(Incoming(graph, middle), leftOutside, "Undo must restore the incoming handle.");
        AssertPoint(Outgoing(graph, next), rightOutside, "Undo must restore the outgoing handle.");
    }

    [AvaloniaTest]
    [TestCase("Symmetry", false, false)]
    [TestCase("Asymmetry", false, false)]
    [TestCase("Separately", false, false)]
    [TestCase("Symmetry", true, false)]
    [TestCase("Asymmetry", true, false)]
    [TestCase("Separately", true, false)]
    [TestCase("Symmetry", false, true)]
    [TestCase("Symmetry", true, true)]
    public async Task Handle_drag_respects_link_mode_and_alt_override(string mode, bool speed, bool alt)
    {
        using var graph = await CreateChain(speed);
        SetMode(graph, mode);
        var channel = graph.Model.SelectedView.Value!;
        channel.SetSelection([graph.Second]);
        HeadlessTestHelpers.Render();
        Point opposite = Outgoing(graph, graph.Second);
        var item = channel.KeyFrames.Single(x => x.Model == graph.Second);
        Point from = speed
            ? graph.View.FindControl<Panel>("graphPanel")!.TranslatePoint(GraphEditorView.GetSpeedHandlePoint(item, true), graph.Window)!.Value
            : graph.Handle("ControlPoint2").TranslatePoint(default, graph.Window)!.Value;
        Drag(graph, from, from + new Vector(-8, -5), alt ? RawInputModifiers.Alt : RawInputModifiers.None);
        AssertCoupling(alt ? "Separately" : mode, speed, Incoming(graph, graph.Second), opposite, Outgoing(graph, graph.Second));
        graph.Capture($"handle-tangents-{mode}-{speed}-{alt}");
    }

    [AvaloniaTest]
    [TestCase("Symmetry", false, true, false)]
    [TestCase("Asymmetry", false, true, false)]
    [TestCase("Separately", false, true, false)]
    [TestCase("Symmetry", false, false, false)]
    [TestCase("Asymmetry", false, false, false)]
    [TestCase("Separately", false, false, false)]
    [TestCase("Symmetry", true, true, false)]
    [TestCase("Asymmetry", true, true, false)]
    [TestCase("Separately", true, true, false)]
    [TestCase("Symmetry", true, false, false)]
    [TestCase("Asymmetry", true, false, false)]
    [TestCase("Separately", true, false, false)]
    [TestCase("Symmetry", false, true, true)]
    [TestCase("Symmetry", true, true, true)]
    public async Task Handle_drag_crosses_its_key_and_returns_without_releasing(string mode, bool speed, bool incoming, bool alt)
    {
        using var graph = await CreateChain(speed);
        SetMode(graph, mode);
        var channel = graph.Model.SelectedView.Value!;
        var left = channel.KeyFrames.Single(x => x.Model == graph.Second);
        var right = channel.KeyFrames[channel.KeyFrames.IndexOf(left) + 1];
        if (!speed)
        {
            // Reproduce the vertical handles in the report, with unequal lengths to test Asymmetry.
            ((SplineEasing)left.Model.Easing).X2 = 1;
            ((SplineEasing)left.Model.Easing).Y2 = 1.5f;
            ((SplineEasing)right.Model.Easing).X1 = 0;
            ((SplineEasing)right.Model.Easing).Y1 = -0.4f;
        }
        channel.SetSelection([graph.Second]);
        graph.Model.HistoryManager.Commit();
        HeadlessTestHelpers.Render(3);
        if (speed)
        {
            graph.View.Focus();
            graph.Window.KeyPress(Key.F, RawInputModifiers.None, PhysicalKey.None, "f");
            graph.Window.KeyRelease(Key.F, RawInputModifiers.None, PhysicalKey.None, "f");
            HeadlessTestHelpers.Render(3);
        }
        var before = SplineCoordinates(graph);
        int undo = graph.Model.HistoryManager.UndoCount;
        Point originalOpposite = incoming ? Outgoing(graph, graph.Second) : Incoming(graph, graph.Second);
        Point farLeft = Outgoing(graph, graph.First), farRight = Incoming(graph, right.Model);
        Point from = default;
        IInputElement? hit = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            from = HandlePosition(graph, incoming ? left : right, incoming, speed);
            hit = graph.HitTest(from);
            if (speed ? hit is GraphEditorSpeedGraph : hit is AvaloniaPath) break;
            HeadlessTestHelpers.Render();
        }
        double initialVelocity = GraphEditorView.GetKeyVelocity(left, !incoming);
        double initialScaleY = graph.Model.ScaleY.Value;
        double keyX = Position(graph, graph.Second).X;
        int direction = incoming ? 1 : -1;
        var modifiers = alt ? RawInputModifiers.Alt : RawInputModifiers.None;
        Assert.That(hit, speed ? Is.TypeOf<GraphEditorSpeedGraph>() : Is.TypeOf<AvaloniaPath>(),
            "The starting handle must be visible and receive the press.");
        graph.Capture($"crossing-before-{mode}-{speed}-{incoming}-{alt}");
        graph.Window.MouseMove(from, modifiers);
        graph.Window.MouseDown(from, MouseButton.Left, modifiers);
        Point? frozen = null;
        Point to = default;
        foreach (int distance in new[] { 8, 16, 24 })
        {
            to = new Point(keyX + direction * distance, from.Y - 5);
            graph.Window.MouseMove(to, modifiers | RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            AssertDraggedHandle(!incoming, to);
            Point driver = incoming ? Outgoing(graph, graph.Second) : Incoming(graph, graph.Second);
            Point opposite = incoming ? Incoming(graph, graph.Second) : Outgoing(graph, graph.Second);
            frozen ??= opposite;
            AssertCoupling(alt ? "Separately" : mode, speed, driver,
                mode == "Separately" || alt ? frozen.Value : originalOpposite, opposite);
        }
        graph.Capture($"crossing-after-{mode}-{speed}-{incoming}-{alt}");

        to = new Point(keyX - direction * 18, from.Y - 5);
        graph.Window.MouseMove(to, modifiers | RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        AssertDraggedHandle(incoming, to);
        AssertPoint(Outgoing(graph, graph.First), farLeft, "The previous key's handle must not change.");
        AssertPoint(Incoming(graph, right.Model), farRight, "The next key's handle must not change.");
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        Assert.That(graph.Second.Value, Is.EqualTo(300));
        graph.Window.MouseUp(to, MouseButton.Left, modifiers);
        var after = SplineCoordinates(graph);
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Model.HistoryManager.Undo();
        Assert.That(SplineCoordinates(graph), Is.EqualTo(before));
        graph.Model.HistoryManager.Redo();
        Assert.That(SplineCoordinates(graph), Is.EqualTo(after));
        graph.Window.MouseMove(from);
        Assert.That(SplineCoordinates(graph), Is.EqualTo(after), "Release must end the drag.");

        void AssertDraggedHandle(bool activeIncoming, Point pointer)
        {
            Point actual = HandlePosition(graph, activeIncoming ? left : right, activeIncoming, speed);
            const string message = "The handle on the pointer's side of the key must follow the same drag.";
            Assert.That(actual.X, Is.EqualTo(pointer.X).Within(0.003), message);
            // A changing speed range can round the ScrollViewer offset to a physical pixel.
            Assert.That(actual.Y, Is.EqualTo(pointer.Y).Within(speed ? 1 : 0.003), message);
            if (speed)
                Assert.That(GraphEditorView.GetKeyVelocity(left, !activeIncoming),
                    Is.EqualTo(initialVelocity + (from.Y - pointer.Y) / initialScaleY).Within(0.001),
                    "Switching sides must preserve the velocity under the pointer.");
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Cancelling_a_handle_drag_after_crossing_restores_both_sides(bool speed)
    {
        using var graph = await CreateChain(speed);
        SetMode(graph, "Symmetry");
        graph.Model.SelectedView.Value!.SetSelection([graph.Second]);
        HeadlessTestHelpers.Render(3);
        var item = graph.Model.SelectedView.Value.KeyFrames.Single(x => x.Model == graph.Second);
        var before = SplineCoordinates(graph);
        int undo = graph.Model.HistoryManager.UndoCount;
        Point from = HandlePosition(graph, item, true, speed);
        graph.HitTest(from);
        graph.Window.MouseMove(from);
        graph.Window.MouseDown(from, MouseButton.Left);
        graph.Window.MouseMove(new Point(Position(graph, graph.Second).X + 20, from.Y - 5), RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        Assert.That(SplineCoordinates(graph), Is.Not.EqualTo(before));
        graph.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        graph.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        graph.Window.MouseMove(from + new Vector(30, 10), RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(from, MouseButton.Left);
        Assert.That(SplineCoordinates(graph), Is.EqualTo(before));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
    }

    [AvaloniaTest]
    public async Task Linear_preset_resets_custom_tangent_influence_and_time_mapping()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Second.Easing = new SplineEasing(0.05f, -0.3f, 0.4f, 1.7f);
        ChoosePreset(graph, "Linear", false);
        var spline = (SplineEasing)graph.Second.Easing;
        Assert.That(spline.X1, Is.EqualTo(1f / 3));
        Assert.That(spline.X2, Is.EqualTo(2f / 3));
        for (int i = 0; i <= 20; i++)
            Assert.That(spline.Ease(i / 20f), Is.EqualTo(i / 20f).Within(0.001));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Handle_drag_at_an_endpoint_stays_within_its_segment(bool speed, bool incoming)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Model.IsSpeedGraph.Value = speed;
        SetMode(graph, "Separately");
        graph.Model.SelectedView.Value!.SetSelection([incoming ? graph.Second : graph.First]);
        HeadlessTestHelpers.Render(3);
        var item = graph.Model.SelectedView.Value.KeyFrames.Single(x => x.Model == graph.Second);
        Point from = HandlePosition(graph, item, incoming, speed);
        Point key = Position(graph, incoming ? graph.Second : graph.First);
        Drag(graph, from, new Point(key.X + (incoming ? 20 : -20), from.Y));
        var spline = (SplineEasing)graph.Second.Easing;
        Assert.That(incoming ? spline.X2 : spline.X1,
            Is.EqualTo(incoming ? (speed ? 0.999 : 1) : (speed ? 0.001 : 0)).Within(0.00001));
        Assert.That(graph.First.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
    }

    private static Point HandlePosition(GraphScope graph, GraphEditorKeyFrameViewModel item, bool incoming, bool speed)
    {
        if (speed)
            return graph.View.FindControl<Panel>("graphPanel")!
                .TranslatePoint(GraphEditorView.GetSpeedHandlePoint(item, incoming), graph.Window)!.Value;
        return graph.View.GetVisualDescendants().OfType<AvaloniaPath>()
            .Single(x => x.DataContext == item && Equals(x.Tag, incoming ? "ControlPoint2" : "ControlPoint1"))
            .TranslatePoint(default, graph.Window)!.Value;
    }

    private static (float, float, float, float)[] SplineCoordinates(GraphScope graph) => graph.Animation.KeyFrames
        .Select(x => x.Easing).OfType<SplineEasing>().Select(x => (x.X1, x.Y1, x.X2, x.Y2)).ToArray();

    [AvaloniaTest]
    public async Task Transform_box_keeps_the_opposite_corner_fixed_across_layout_updates()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        SetMode(graph, "Separately");
        HeadlessTestHelpers.Render();
        Point first = Position(graph, graph.First), second = Position(graph, graph.Second);
        Point from = new((first.X + second.X) / 2, second.Y);
        graph.HitTest(from);
        graph.Window.MouseDown(from, MouseButton.Left);
        graph.Window.MouseMove(from + new Vector(0, -20), RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.First.Value, Is.EqualTo(100).Within(0.001));
        graph.Window.MouseMove(from + new Vector(0, -40), RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(from + new Vector(0, -40), MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.That(graph.First.Value, Is.EqualTo(100).Within(0.001));
        Assert.That(graph.Second.Value, Is.EqualTo(580).Within(0.001));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Switching_ease_presets_replaces_previous_direction(bool speed, bool keyboard)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Model.IsSpeedGraph.Value = speed;
        HeadlessTestHelpers.Render(3);
        foreach (string mode in new[] { "Ease", "EaseIn", "EaseOut", "Linear", "EaseOut", "EaseIn", "Ease" })
        {
            float before = graph.Second.Easing.Ease(0.25f);
            int undo = graph.Model.HistoryManager.UndoCount;
            ChoosePreset(graph, mode, keyboard);
            double start = GraphEditorCurveMath.Velocity(graph.Second.Easing, 0, 1, 1);
            double end = GraphEditorCurveMath.Velocity(graph.Second.Easing, 1, 1, 1);
            double expectedStart = mode is "Ease" or "EaseOut" ? 0 : 1;
            double expectedEnd = mode is "Ease" or "EaseIn" ? 0 : 1;
            Assert.That(start, Is.EqualTo(expectedStart).Within(0.001), mode);
            Assert.That(end, Is.EqualTo(expectedEnd).Within(0.001), mode);
            if (mode == "EaseIn") Assert.That(graph.Second.Easing.Ease(0.25f), Is.GreaterThan(0.25f));
            if (mode == "EaseOut") Assert.That(graph.Second.Easing.Ease(0.25f), Is.LessThan(0.25f));
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
            graph.Model.HistoryManager.Undo();
            Assert.That(graph.Second.Easing.Ease(0.25f), Is.EqualTo(before).Within(0.0001));
            graph.Model.HistoryManager.Redo();
            HeadlessTestHelpers.Render(3);
            if (!keyboard && mode is "EaseIn" or "EaseOut")
            {
                graph.View.Focus();
                graph.Window.KeyPress(Key.F, RawInputModifiers.None, PhysicalKey.None, "f");
                graph.Window.KeyRelease(Key.F, RawInputModifiers.None, PhysicalKey.None, "f");
                HeadlessTestHelpers.Render(3);
                graph.Capture($"preset-{mode}-{speed}");
            }
        }
    }

    [AvaloniaTest]
    public async Task Directional_presets_leave_unselected_neighbors_handles_untouched()
    {
        using var graph = await CreateChain(false);
        var next = graph.Animation.KeyFrames[2];
        graph.Model.SelectedView.Value!.SetSelection([graph.Second]);
        var before = ((SplineEasing)graph.Second.Easing).X1;
        var after = ((SplineEasing)next.Easing).X2;
        foreach (string mode in new[] { "EaseIn", "EaseOut" })
        {
            ChoosePreset(graph, mode, false);
            Assert.That(((SplineEasing)graph.Second.Easing).X1, Is.EqualTo(before));
            Assert.That(((SplineEasing)graph.Second.Easing).Y1, Is.EqualTo(0.25f));
            Assert.That(((SplineEasing)next.Easing).X2, Is.EqualTo(after));
            Assert.That(((SplineEasing)next.Easing).Y2, Is.EqualTo(0.8f));
        }
    }

    private static void ChoosePreset(GraphScope graph, string mode, bool keyboard)
    {
        if (keyboard && mode != "Linear")
        {
            graph.View.Focus();
            var command = Beutl.Editor.Components.Helpers.KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
                ? RawInputModifiers.Meta : RawInputModifiers.Control;
            var modifiers = mode == "EaseIn" ? RawInputModifiers.Shift : mode == "EaseOut" ? command : RawInputModifiers.None;
            graph.Window.KeyPress(Key.F9, modifiers, PhysicalKey.None, null);
            graph.Window.KeyRelease(Key.F9, modifiers, PhysicalKey.None, null);
        }
        else
        {
            var button = graph.View.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Tag, mode));
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), graph.Window)!.Value;
            graph.HitTest(point);
            graph.Window.MouseMove(point);
            graph.Window.MouseDown(point, MouseButton.Left);
            graph.Window.MouseUp(point, MouseButton.Left);
        }
        HeadlessTestHelpers.Render(3);
    }

    private static async Task<GraphScope> CreateChain(bool speed)
    {
        var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        graph.Second.Value = 300;
        graph.Animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(2.5),
            Value = 450,
            Easing = new SplineEasing(0.2f, 0.1f, 0.7f, 0.8f)
        });
        graph.Animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(4.5),
            Value = 750,
            Easing = new SplineEasing(0.2f, 0.2f, 0.8f, 0.9f)
        });
        graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 0.6f };
        graph.Model.ScaleY.Value = 0.3;
        graph.Model.IsSpeedGraph.Value = speed;
        graph.Model.HistoryManager.Commit();
        HeadlessTestHelpers.Render(3);
        if (!speed) graph.Model.ScrollOffset.Value = default;
        HeadlessTestHelpers.Render();
        return graph;
    }

    private static void SetMode(GraphScope graph, string mode)
    {
        graph.Model.Symmetry.Value = mode == "Symmetry";
        graph.Model.Asymmetry.Value = mode == "Asymmetry";
        graph.Model.Separately.Value = mode == "Separately";
    }

    // Pixel vectors in the value graph; the speed graph exposes their horizontal influence.
    private static Point Incoming(GraphScope graph, IKeyFrame key)
    {
        var item = graph.Model.SelectedView.Value!.KeyFrames.Single(x => x.Model == key);
        return item.ControlPoint2.Value - item.RightTop.Value;
    }

    private static Point Outgoing(GraphScope graph, IKeyFrame key)
    {
        var channel = graph.Model.SelectedView.Value!;
        int index = channel.KeyFrames.IndexOf(channel.KeyFrames.Single(x => x.Model == key));
        var next = channel.KeyFrames[index + 1];
        return next.ControlPoint1.Value - next.LeftBottom.Value;
    }

    private static Point Position(GraphScope graph, IKeyFrame key)
    {
        var item = graph.Model.SelectedView.Value!.KeyFrames.Single(x => x.Model == key);
        return graph.View.FindControl<Panel>("graphPanel")!.TranslatePoint(graph.View.GetKeyPoint(item), graph.Window)!.Value;
    }

    private static void AssertCoupling(string mode, bool speed, Point driver, Point originalOpposite, Point actual)
    {
        double Length(Point point) => speed ? Math.Abs(point.X) : Math.Sqrt(point.X * point.X + point.Y * point.Y);
        Point expected = mode switch
        {
            "Symmetry" => driver * -1,
            "Asymmetry" => driver * (-Length(originalOpposite) / Length(driver)),
            _ => originalOpposite
        };
        AssertPoint(actual, expected, $"{mode} must control the opposite handle in the {(speed ? "speed" : "value")} graph.");
    }

    private static void AssertPoint(Point actual, Point expected, string message)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.003), message);
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.003), message);
    }

    private static void Drag(GraphScope graph, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        graph.HitTest(from);
        graph.Window.MouseMove(from, modifiers);
        graph.Window.MouseDown(from, MouseButton.Left, modifiers);
        graph.Window.MouseMove(to, modifiers | RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(to, MouseButton.Left, modifiers);
        HeadlessTestHelpers.Render(3);
    }
}
