using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Views;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Microsoft.Extensions.DependencyInjection;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorRulerBarTests
{
    [Test]
    [TestCase(1e-300)]
    [TestCase(1e-15)]
    [TestCase(1)]
    [TestCase(1e15)]
    [TestCase(1e300)]
    public void Grid_spacing_tracks_the_actual_scale(double scale)
    {
        double pixels = GraphEditorGridMetrics.Step(scale) * scale;
        Assert.That(pixels, Is.InRange(71.99, 180.01));
        var ticks = GraphEditorGridMetrics.MajorTicks(scale, 350, 0, 400).ToArray();
        Assert.That(ticks.Length, Is.InRange(2, 8));
        Assert.That(ticks.All(tick => double.IsFinite(tick.Y)), Is.True);
    }

    [Test]
    [TestCase(0, 350)]
    [TestCase(-1, 350)]
    [TestCase(double.NaN, 350)]
    [TestCase(double.PositiveInfinity, 350)]
    [TestCase(double.Epsilon, 350)]
    [TestCase(1, double.NaN)]
    [TestCase(1, double.PositiveInfinity)]
    [TestCase(1, 1e20)]
    public void Invalid_or_extreme_grid_inputs_stop_tick_traversal(double scale, double baseline)
    {
        var ticks = GraphEditorGridMetrics.MajorTicks(scale, baseline, 0, 400).Take(4097).ToArray();
        Assert.That(ticks.Length, Is.LessThanOrEqualTo(1));
    }

    [AvaloniaTest]
    [TestCase(0, 350)]
    [TestCase(double.NaN, 350)]
    [TestCase(1e-15, 350)]
    [TestCase(1, 1e20)]
    public void Extreme_grid_inputs_render_without_stalling(double scale, double baseline)
    {
        var background = new GraphEditorBackground { Scale = scale, Baseline = baseline, Viewport = new Size(600, 400), IsSpeedGraph = true };
        var ruler = new GraphEditorScale { Scale = scale, Baseline = baseline, Width = 60 };
        var window = new Window { Content = new Grid { Children = { background, ruler } }, Width = 640, Height = 420 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(3);
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false, 0.5, 0, 1)]
    [TestCase(true, 0.5, 0, 2)]
    [TestCase(false, 1, 200, 2)]
    [TestCase(true, 2, 200, 1)]
    public async Task Minor_ticks_and_grid_lines_reach_the_bottom_below_the_last_major_tick(bool light, double scaleY, double lowestMajor, double dpi)
    {
        using var graph = await GraphScope.CreateAsync(light, separateHandles: true);
        graph.Window.SetRenderScaling(dpi);
        graph.Model.ScaleY.Value = scaleY;
        HeadlessTestHelpers.Render(3);
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        var ruler = graph.View.FindControl<GraphEditorScale>("verticalScale")!;
        var background = graph.View.FindControl<GraphEditorBackground>("background")!;
        double step = GraphEditorGridMetrics.Step(scaleY);
        double majorY = scroll.Viewport.Height - step * scaleY * 0.7;
        graph.Model.ScrollOffset.Value = new Vector(0, graph.Model.Baseline.Value - lowestMajor * scaleY - majorY);
        HeadlessTestHelpers.Render(3);
        using var frame = graph.Window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        using var pixels = frame!.Lock();
        Assert.That(pixels.Format, Is.EqualTo(PixelFormat.Bgra8888).Or.EqualTo(PixelFormat.Rgba8888));
        foreach (double value in new[] { lowestMajor, lowestMajor - step / 4, lowestMajor - step / 2 })
        {
            double y = graph.Model.Baseline.Value - value * scaleY;
            var tick = ruler.TranslatePoint(new Point(ruler.Bounds.Width - 1.5, y - scroll.Offset.Y), graph.Window)!.Value;
            var gridLine = background.TranslatePoint(new Point(160, y), graph.Window)!.Value;
            Assert.That(tick.Y, Is.EqualTo(gridLine.Y).Within(1), "The ruler and grid must remain aligned.");
            Assert.That(y - scroll.Offset.Y, Is.InRange(0, scroll.Viewport.Height - 4));
            Assert.That(HasLine(tick), Is.True, $"The value ruler must draw the visible tick at {value}, including below its last major tick.");
            Assert.That(HasLine(gridLine), Is.True, $"The graph must draw the matching grid line at {value}.");
        }

        bool HasLine(Point point)
        {
            int x = (int)Math.Floor(point.X * graph.Window.RenderScaling);
            int y = (int)Math.Round(point.Y * graph.Window.RenderScaling);
            int blankY = (int)Math.Round((point.Y + 4) * graph.Window.RenderScaling);
            Assert.That(x, Is.InRange(0, pixels.Size.Width - 1));
            Assert.That(y - 1, Is.GreaterThanOrEqualTo(0));
            Assert.That(blankY, Is.LessThan(pixels.Size.Height));
            int blank = Marshal.ReadInt32(pixels.Address, blankY * pixels.RowBytes + x * 4);
            return Enumerable.Range(y - 1, 3).Any(row =>
                Marshal.ReadInt32(pixels.Address, row * pixels.RowBytes + x * 4) != blank);
        }
    }

    [AvaloniaTest]
    public async Task Scene_bars_never_drag_the_scene_below_one_frame()
    {
        using GraphScope graph = await GraphScope.CreateAsync();
        Scene scene = graph.Model.Scene;
        TimelineScale horizontal = graph.View.FindControl<TimelineScale>("scale")!;
        TimeSpan frame = TimeSpan.FromSeconds(1d / 30);
        scene.Start = TimeSpan.Zero;
        scene.Duration = TimeSpan.FromSeconds(2);
        HeadlessTestHelpers.Render();

        // The ending bar sits at 300 px (2 s at 150 px per second); drop it onto the start.
        graph.Drag(OnRuler(298), OnRuler(0));
        Assert.That(scene.Duration, Is.EqualTo(frame));

        scene.Duration = TimeSpan.FromSeconds(2);
        HeadlessTestHelpers.Render();
        // Drop the starting bar onto the end.
        graph.Drag(OnRuler(2), OnRuler(300));
        Assert.That(scene.Start, Is.EqualTo(TimeSpan.FromSeconds(2) - frame));
        Assert.That(scene.Duration, Is.EqualTo(frame));

        Point OnRuler(double x) => horizontal.TranslatePoint(new Point(x, 8), graph.Window)!.Value;
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Scene_bar_drag_keeps_its_value_on_capture_loss_and_reverts_on_Escape(bool escape)
    {
        using GraphScope graph = await GraphScope.CreateAsync();
        Scene scene = graph.Model.Scene;
        HistoryManager history = graph.Model.EditorContext.GetRequiredService<HistoryManager>();
        TimelineScale horizontal = graph.View.FindControl<TimelineScale>("scale")!;
        scene.Start = TimeSpan.Zero;
        scene.Duration = TimeSpan.FromSeconds(2);
        history.Commit();
        HeadlessTestHelpers.Render();
        int undoCount = history.UndoCount;
        IPointer? pointer = null;
        graph.View.FindControl<Border>("RulerBar")!.AddHandler(InputElement.PointerPressedEvent,
            (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);

        // The ending bar sits at 300 px (2 s at 150 px per second).
        graph.HitTest(OnRuler(298));
        graph.Window.MouseMove(OnRuler(298));
        graph.Window.MouseDown(OnRuler(298), MouseButton.Left);
        graph.Window.MouseMove(OnRuler(270), RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render();
        Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.8)));

        if (escape)
        {
            graph.Window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
            graph.Window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
        }
        else
        {
            pointer!.Capture(null);
        }

        HeadlessTestHelpers.Settle();
        graph.Window.MouseMove(OnRuler(240), RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(OnRuler(240), MouseButton.Left);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(escape ? 2 : 1.8)));
            Assert.That(history.UndoCount, Is.EqualTo(undoCount + (escape ? 0 : 1)));
            Assert.That(history.IsEditing, Is.False);
        });

        Point OnRuler(double x) => horizontal.TranslatePoint(new Point(x, 8), graph.Window)!.Value;
    }

    [AvaloniaTest]
    [TestCase(640, false)]
    [TestCase(960, false)]
    [TestCase(640, true)]
    [TestCase(960, true)]
    public async Task Both_rulers_stay_aligned_during_seek_pan_zoom_and_keyframe_drag(int width, bool light)
    {
        using var scope = await GraphScope.CreateAsync(light, elementStart: TimeSpan.FromSeconds(0.2));
        scope.View.DataContext = null;
        Scene scene = scope.Model.Scene;
        Element element = scope.Model.Element!;
        KeyFrameAnimation<float> animation = scope.Animation;
        KeyFrame<float> first = scope.First;
        using var model = new GraphEditorTabViewModel(scope.Model.EditorContext);
        var view = new GraphEditorTabView { DataContext = model };
        Window window = scope.Window;
        window.Content = view;
        window.Width = width;
        GraphEditorViewModel? graph = null;
        try
        {
            HeadlessTestHelpers.Render();
            GraphEditorView graphView = view.GetVisualDescendants().OfType<GraphEditorView>().Single();
            Assert.That(graphView.IsVisible, Is.False);
            model.Element.Value = element;
            model.Select(animation);
            HeadlessTestHelpers.Render();
            graph = model.SelectedAnimation.Value!;
            graph.Options.Value = graph.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
            graph.ScrollOffset.Value = new Vector(0, 60);
            scene.Duration = TimeSpan.FromSeconds(2);
            HeadlessTestHelpers.Render();
            Assert.That(graphView.IsEffectivelyVisible, Is.True);

            Border bar = graphView.FindControl<Border>("RulerBar")!;
            TimelineScale horizontal = graphView.FindControl<TimelineScale>("scale")!;
            GraphEditorScale vertical = graphView.FindControl<GraphEditorScale>("verticalScale")!;
            ScrollViewer scroll = graphView.FindControl<ScrollViewer>("scroll")!;
            Panel canvas = graphView.FindControl<Panel>("graphPanel")!;
            GraphEditorBackground background = graphView.FindControl<GraphEditorBackground>("background")!;
            CheckAlignment();

            scope.Click(horizontal.TranslatePoint(new Point(75, 24), window)!.Value);
            Assert.That(graph.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
            scope.Click(bar.TranslatePoint(new Point(90, 4), window)!.Value);
            Assert.That(graph.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.6)));

            scope.Drag(horizontal.TranslatePoint(new Point(298, 8), window)!.Value, horizontal.TranslatePoint(new Point(270, 8), window)!.Value);
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.8)));
            scope.Drag(horizontal.TranslatePoint(new Point(2, 8), window)!.Value, horizontal.TranslatePoint(new Point(30, 8), window)!.Value);
            Assert.That(scene.Start, Is.EqualTo(TimeSpan.FromSeconds(0.2)));
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.6)));

            // Grab the actual keyframe at the intersection of its time and value coordinates.
            Point keyframe = canvas.TranslatePoint(new Point(
                (first.KeyTime + element.Start).TimeToPixel(graph.Options.Value.Scale),
                graph.Baseline.Value - first.Value * graph.ScaleY.Value), window)!.Value;
            window.MouseMove(keyframe);
            window.MouseDown(keyframe, MouseButton.Left);
            Assert.That(graphView.KeyTimeMoveState, Is.Not.Null);
            window.MouseMove(keyframe + new Vector(30, -20), RawInputModifiers.LeftMouseButton);
            window.MouseUp(keyframe + new Vector(30, -20), MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.That(first.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(0.7)));
            Assert.That(first.Value, Is.EqualTo(140).Within(0.001));
            CheckAlignment();

            graph.ScrollOffset.Value = new Vector(75, 60);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset.X, Is.EqualTo(75));
            Assert.That(scroll.Offset.Y, Is.EqualTo(60));
            CheckAlignment();
            scope.Click(bar.TranslatePoint(new Point(75, 4), window)!.Value);
            Assert.That(graph.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(1)));

            Point verticalPoint = vertical.TranslatePoint(new Point(10, 80), window)!.Value;
            double previousY = scroll.Offset.Y;
            Vector pan = new(0, -1);
            window.MouseWheel(verticalPoint, pan);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset.Y, Is.GreaterThan(previousY));
            Assert.That(scroll.Offset.X, Is.EqualTo(75));
            CheckAlignment();

            window.MouseWheel(verticalPoint, new Vector(0, 1), GraphScope.CommandModifier);
            HeadlessTestHelpers.Render();
            Assert.That(graph.ScaleY.Value, Is.EqualTo(0.6).Within(0.001));
            CheckAlignment();
            window.MouseWheel(bar.TranslatePoint(new Point(100, 4), window)!.Value, new Vector(0, 1), RawInputModifiers.Alt);
            HeadlessTestHelpers.Render();
            Assert.That(graph.Options.Value.Scale, Is.EqualTo(1.2f).Within(0.001));
            CheckAlignment();

            graph.SelectedView.Value!.SetSelection(animation.KeyFrames);
            graphView.Focus();
            scope.PressKey(Key.F9);
            scope.FitAll();
            HeadlessTestHelpers.Render(3);
            CheckAlignment();
            graph.IsSpeedGraph.Value = true;
            HeadlessTestHelpers.Render(3);
            CheckAlignment();
            Assert.That(graphView.FindControl<GraphEditorSpeedGraph>("SpeedGraph")!.IsVisible, Is.True);
            graph.IsSpeedGraph.Value = false;
            HeadlessTestHelpers.Render(3);

            window.Height = 320;
            HeadlessTestHelpers.Render();
            CheckAlignment();
            model.Select(null);
            HeadlessTestHelpers.Render();
            Assert.That(graphView.IsVisible, Is.False);

            GraphEditorViewModel previousGraph = graph;
            model.Select(animation);
            graph = model.SelectedAnimation.Value!;
            HeadlessTestHelpers.Render();
            graph.ScrollOffset.Value = new Vector(25, 40);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset, Is.EqualTo(new Vector(25, 40)));
            previousGraph.ScrollOffset.Value = default;
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset, Is.EqualTo(new Vector(25, 40)));
            previousGraph.Dispose();
            CheckAlignment();

            void CheckAlignment()
            {
                Point plotOrigin = scroll.TranslatePoint(default, graphView)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(bar.Bounds.Height, Is.EqualTo(38));
                    Assert.That(horizontal.Bounds.Height, Is.EqualTo(32));
                    Assert.That(horizontal.TranslatePoint(default, bar)!.Value.Y, Is.EqualTo(6));
                    Assert.That(horizontal.TranslatePoint(default, graphView)!.Value.X, Is.EqualTo(plotOrigin.X));
                    Assert.That(horizontal.TranslatePoint(new Point(0, horizontal.Bounds.Height), graphView)!.Value.Y, Is.EqualTo(plotOrigin.Y));
                    Assert.That(vertical.TranslatePoint(default, graphView)!.Value.Y, Is.EqualTo(plotOrigin.Y));
                    Assert.That(vertical.Bounds.Width, Is.EqualTo(56));
                    Assert.That(vertical.Bounds.Height, Is.EqualTo(scroll.Viewport.Height));
                    Assert.That(vertical.Bounds.Height, Is.LessThanOrEqualTo(scroll.Bounds.Height));
                    Assert.That(vertical.TranslatePoint(new Point(vertical.Bounds.Width, 0), graphView)!.Value.X, Is.EqualTo(plotOrigin.X));
                    Assert.That(vertical.Baseline, Is.EqualTo(background.Baseline));
                    Assert.That(vertical.Scale, Is.EqualTo(background.Scale));
                    Assert.That(vertical.Offset.Y, Is.EqualTo(scroll.Offset.Y));
                    Assert.That(horizontal.Offset.X, Is.EqualTo(scroll.Offset.X));
                    Assert.That(horizontal.TranslatePoint(new Point(100, 0), graphView)!.Value.X,
                        Is.EqualTo(canvas.TranslatePoint(new Point(100 + scroll.Offset.X, 0), graphView)!.Value.X).Within(0.001));
                    Assert.That(vertical.TranslatePoint(new Point(0, graph.Baseline.Value - scroll.Offset.Y), graphView)!.Value.Y,
                        Is.EqualTo(background.TranslatePoint(new Point(0, graph.Baseline.Value), graphView)!.Value.Y).Within(0.001));
                });
            }
        }
        finally
        {
            view.DataContext = null;
            graph?.Dispose();
        }
    }
}
