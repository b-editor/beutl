using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Beutl.Audio;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Components.SilenceSplitTab.ViewModels;
using Beutl.Editor.Components.SilenceSplitTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using BtlMatrix = Beutl.Graphics.Matrix;
using Vector2 = System.Numerics.Vector2;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TwoFingerTouchGestureTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Timeline_two_finger_touch_pinch_and_pan(bool ruler)
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("touch-timeline", MediaTime.FromSeconds(30));
        Control target = ruler ? host.RulerBar : host.Content;
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 1, Offset = new Vector2(300, 200) };
        HeadlessTestHelpers.Render();
        Assert.That(host.Content.Offset, Is.EqualTo(new Vector(300, 200)));

        Point centerLocal = new(180, ruler ? 10 : 80);
        Point center = target.TranslatePoint(centerLocal, host.Window)!.Value;
        TimelineTestHost.WaitForHitTest(host.Window, target, center);

        // Two-finger pinch out (80px -> 160px) doubles scale while keeping time under center anchored.
        DispatchTwoFingerGesture(
            host.Window,
            new Point(center.X - 40, center.Y),
            new Point(center.X + 40, center.Y),
            new Point(center.X - 80, center.Y),
            new Point(center.X + 80, center.Y));
        Assert.Multiple(() =>
        {
            Assert.That(host.Model.Options.Value.Scale, Is.EqualTo(2f).Within(0.001f));
            Assert.That((host.Content.Offset.X + centerLocal.X) / host.Model.Options.Value.Scale,
                Is.EqualTo(300 + centerLocal.X).Within(0.1));
            Assert.That(host.Content.Offset.Y, Is.EqualTo(200).Within(0.01));
        });

        // Two-finger pan (-60, -40) with constant distance keeps scale unchanged and scrolls with fingers.
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 1, Offset = new Vector2(300, 200) };
        HeadlessTestHelpers.Render();
        DispatchTwoFingerGesture(
            host.Window,
            new Point(center.X - 40, center.Y),
            new Point(center.X + 40, center.Y),
            new Point(center.X - 100, center.Y - 40),
            new Point(center.X - 20, center.Y - 40));
        Assert.Multiple(() =>
        {
            Assert.That(host.Model.Options.Value.Scale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(host.Content.Offset.X, Is.EqualTo(360).Within(0.1));
            Assert.That(host.Content.Offset.Y, Is.EqualTo(ruler ? 200 : 240).Within(0.1));
        });
    }

    [AvaloniaTest]
    public async Task NodeGraph_two_finger_touch_pinch_and_pan()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("touch-node-graph");
        var expression = new ExpressionNode { Position = { Value = (24, 24) } };
        expression.Expression.Property!.SetValue("1");
        var output = new OutputNode { Position = { Value = (320, 96) } };
        var graph = new GraphModel();
        graph.Nodes.AddRange([expression, output]);
        graph.Connect(output.InputPort, expression.Output);
        using var model = new NodeGraphViewModel(graph, editor);
        var view = new NodeGraphView { DataContext = model };
        var window = new Window { Content = view, Width = 640, Height = 360, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ZoomBorder zoom = view.FindControl<ZoomBorder>("zoomBorder")!;
            Canvas canvas = view.FindControl<Canvas>("canvas")!;
            zoom.SetMatrix(new Matrix(1.5, 0, 0, 1.5, 45, 30), true);
            HeadlessTestHelpers.Render();

            Point center = new(300, 180);
            Point anchor = window.TranslatePoint(center, canvas)!.Value;

            DispatchTwoFingerGesture(
                window,
                new Point(center.X - 40, center.Y),
                new Point(center.X + 40, center.Y),
                new Point(center.X - 80, center.Y),
                new Point(center.X + 80, center.Y));
            Point afterZoom = canvas.TranslatePoint(anchor, window)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(zoom.ZoomX, Is.EqualTo(3.0).Within(0.001));
                Assert.That(afterZoom.X, Is.EqualTo(center.X).Within(0.1));
                Assert.That(afterZoom.Y, Is.EqualTo(center.Y).Within(0.1));
            });

            zoom.SetMatrix(new Matrix(1.5, 0, 0, 1.5, 45, 30), true);
            HeadlessTestHelpers.Render();
            DispatchTwoFingerGesture(
                window,
                new Point(center.X - 40, center.Y),
                new Point(center.X + 40, center.Y),
                new Point(center.X, center.Y - 30),
                new Point(center.X + 80, center.Y - 30));
            Assert.Multiple(() =>
            {
                Assert.That(zoom.ZoomX, Is.EqualTo(1.5).Within(0.001));
                Assert.That(zoom.OffsetX, Is.EqualTo(85).Within(0.1));
                Assert.That(zoom.OffsetY, Is.EqualTo(0).Within(0.1));
            });
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Preview_two_finger_touch_pinch_and_pan()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("touch-preview");
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Grid viewport = view.FindControl<Grid>("previewViewport")!;
            player.IsMoveMode.Value = true;
            player.FrameMatrix.Value = BtlMatrix.Identity;
            HeadlessTestHelpers.Render();

            Point localCenter = new(viewport.Bounds.Width * 0.5, viewport.Bounds.Height * 0.5);
            Point center = viewport.TranslatePoint(localCenter, window)!.Value;

            DispatchTwoFingerGesture(
                window,
                new Point(center.X - 40, center.Y),
                new Point(center.X + 40, center.Y),
                new Point(center.X - 80, center.Y),
                new Point(center.X + 80, center.Y));
            Assert.Multiple(() =>
            {
                Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(2f).Within(0.001f));
                Assert.That(player.FrameMatrix.Value.M31, Is.EqualTo((float)(localCenter.X - 2 * localCenter.X)).Within(0.5f));
                Assert.That(player.FrameMatrix.Value.M32, Is.EqualTo((float)(localCenter.Y - 2 * localCenter.Y)).Within(0.5f));
            });

            player.FrameMatrix.Value = BtlMatrix.Identity;
            HeadlessTestHelpers.Render();
            DispatchTwoFingerGesture(
                window,
                new Point(center.X - 40, center.Y),
                new Point(center.X + 40, center.Y),
                new Point(center.X + 10, center.Y - 35),
                new Point(center.X + 90, center.Y - 35));
            Assert.Multiple(() =>
            {
                Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(1f).Within(0.001f));
                Assert.That(player.FrameMatrix.Value.M31, Is.EqualTo(50f).Within(0.5f));
                Assert.That(player.FrameMatrix.Value.M32, Is.EqualTo(-35f).Within(0.5f));
            });
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase("plot")]
    [TestCase("horizontal-ruler")]
    [TestCase("vertical-ruler")]
    public async Task GraphEditor_two_finger_touch_pinch_and_pan(string route)
    {
        using var graph = await GraphEditorContextMenuTests.GraphScope.CreateAsync(selectAll: false);
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 1 };
        graph.Model.ScaleY.Value = 2;
        HeadlessTestHelpers.Render(3);
        graph.Model.ScrollOffset.Value = new Vector(75, 80);
        HeadlessTestHelpers.Render();

        Control target = route switch
        {
            "horizontal-ruler" => graph.View.FindControl<Border>("RulerBar")!,
            "vertical-ruler" => graph.View.FindControl<GraphEditorScale>("verticalScale")!,
            _ => scroll
        };
        Point local = route switch
        {
            "horizontal-ruler" => new Point(180, 16),
            "vertical-ruler" => new Point(20, 160),
            _ => new Point(180, 160)
        };
        Point center = target.TranslatePoint(local, graph.Window)!.Value;
        Point scrollCenter = target.TranslatePoint(local, scroll)!.Value;
        bool verticalRuler = route == "vertical-ruler";
        double time = (scroll.Offset.X + scrollCenter.X) / graph.Model.Options.Value.Scale;
        double value = (graph.Model.Baseline.Value - scroll.Offset.Y - scrollCenter.Y) / graph.Model.ScaleY.Value;

        Point p1Start = verticalRuler ? new Point(center.X, center.Y - 30) : new Point(center.X - 30, center.Y);
        Point p2Start = verticalRuler ? new Point(center.X, center.Y + 30) : new Point(center.X + 30, center.Y);
        Point p1End = verticalRuler ? new Point(center.X, center.Y - 60) : new Point(center.X - 60, center.Y);
        Point p2End = verticalRuler ? new Point(center.X, center.Y + 60) : new Point(center.X + 60, center.Y);

        DispatchTwoFingerGesture(graph.Window, p1Start, p2Start, p1End, p2End);
        Assert.Multiple(() =>
        {
            Assert.That(graph.Model.Options.Value.Scale, Is.EqualTo(verticalRuler ? 1f : 2f).Within(0.001f));
            Assert.That(graph.Model.ScaleY.Value, Is.EqualTo(verticalRuler ? 4.0 : 2.0).Within(0.001));
            if (verticalRuler)
            {
                Assert.That((graph.Model.Baseline.Value - scroll.Offset.Y - scrollCenter.Y) / graph.Model.ScaleY.Value,
                    Is.EqualTo(value).Within(0.05));
            }
            else
            {
                Assert.That((scroll.Offset.X + scrollCenter.X) / graph.Model.Options.Value.Scale,
                    Is.EqualTo(time).Within(0.05));
            }
        });

        // Two-finger pan keeps scale constant and moves the viewport in the matching axis.
        graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 1 };
        graph.Model.ScaleY.Value = 2;
        HeadlessTestHelpers.Render(3);
        graph.Model.ScrollOffset.Value = new Vector(75, 80);
        HeadlessTestHelpers.Render();
        DispatchTwoFingerGesture(
            graph.Window,
            p1Start,
            p2Start,
            new Point(p1Start.X - 30, p1Start.Y - 20),
            new Point(p2Start.X - 30, p2Start.Y - 20));
        Assert.Multiple(() =>
        {
            Assert.That(graph.Model.Options.Value.Scale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(graph.Model.ScaleY.Value, Is.EqualTo(2.0).Within(0.001));
            Assert.That(scroll.Offset.X, Is.EqualTo(verticalRuler ? 75 : 105).Within(0.5));
            Assert.That(scroll.Offset.Y, Is.EqualTo(route == "horizontal-ruler" ? 80 : 100).Within(0.5));
        });
    }

    [AvaloniaTest]
    public async Task SilenceSplit_two_finger_touch_pinch_and_pan()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("touch-silence", duration: MediaTime.FromSeconds(70));
        var element = new Element { Start = { Value = MediaTime.FromSeconds(10) }, Length = { Value = MediaTime.FromSeconds(60) } };
        element.Objects.Add(new SourceSound { Source = { Value = new SoundSource() } });
        scene.Children.Add(element);
        using var tab = new SilenceSplitTabViewModel(editor);
        tab.Show(element);
        var view = new SilenceSplitTabView { DataContext = tab };
        var window = new Window { Content = view, Width = 1200, Height = 360 };
        try
        {
            window.Show();
            await HeadlessTestHelpers.WaitUntilAsync(() => !tab.Analysis.Value!.IsBusy.Value);
            SilenceSplitViewModel model = tab.Analysis.Value!;
            model.SetWaveform(Enumerable.Range(0, 3000).Select(i => new WaveformChunk(i, 0, 0)).ToArray());
            HeadlessTestHelpers.Render();

            WaveformControl waveform = view.FindControl<WaveformControl>("Waveform")!;
            double width = waveform.Bounds.Width;
            Point center = waveform.TranslatePoint(new Point(width * 0.5, waveform.Bounds.Height * 0.5), window)!.Value;

            // Pinching in past full-clip clamp then returning to initial distance stays at 60s.
            IHeadlessTouchPointer clampP1 = window.TouchBegin(new Point(center.X - 60, center.Y));
            IHeadlessTouchPointer clampP2 = window.TouchBegin(new Point(center.X + 60, center.Y));
            window.TouchMove(clampP1, new Point(center.X - 20, center.Y));
            window.TouchMove(clampP2, new Point(center.X + 20, center.Y));
            HeadlessTestHelpers.Render();
            window.TouchMove(clampP1, new Point(center.X - 60, center.Y));
            window.TouchMove(clampP2, new Point(center.X + 60, center.Y));
            HeadlessTestHelpers.Render();
            window.TouchEnd(clampP1, new Point(center.X - 60, center.Y));
            window.TouchEnd(clampP2, new Point(center.X + 60, center.Y));
            HeadlessTestHelpers.Render();
            Assert.That(model.PreviewRange.Duration.ToSeconds(), Is.EqualTo(60).Within(0.5));

            // Combined pinch (2x) + pan (+100px) keeps the time originally at center anchored at center + 100px.
            double anchorTime = model.PreviewRange.Start.ToSeconds() + 0.5 * model.PreviewRange.Duration.ToSeconds();
            DispatchTwoFingerGesture(
                window,
                new Point(center.X - 40, center.Y),
                new Point(center.X + 40, center.Y),
                new Point(center.X + 100 - 80, center.Y),
                new Point(center.X + 100 + 80, center.Y),
                steps: 1);
            double newAnchorFraction = (width * 0.5 + 100) / width;
            double timeAtNewCenter = model.PreviewRange.Start.ToSeconds()
                + newAnchorFraction * model.PreviewRange.Duration.ToSeconds();
            Assert.Multiple(() =>
            {
                Assert.That(model.PreviewRange.Duration.ToSeconds(), Is.EqualTo(30).Within(1.0));
                Assert.That(timeAtNewCenter, Is.EqualTo(anchorTime).Within(0.5));
            });

            double startBeforePan = model.PreviewRange.Start.ToSeconds();
            DispatchTwoFingerGesture(
                window,
                new Point(center.X - 40, center.Y),
                new Point(center.X + 40, center.Y),
                new Point(center.X - 100, center.Y),
                new Point(center.X - 20, center.Y));
            Assert.That(model.PreviewRange.Start.ToSeconds(), Is.GreaterThan(startBeforePan));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Timeline_re_touching_second_finger_starts_new_pinch_without_lifting_first()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("touch-recontact", MediaTime.FromSeconds(30));
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 0.5f, Offset = new Vector2(300, 200) };
        HeadlessTestHelpers.Render();

        Point centerLocal = new(180, 80);
        Point center = host.Content.TranslatePoint(centerLocal, host.Window)!.Value;
        TimelineTestHost.WaitForHitTest(host.Window, host.Content, center);

        IHeadlessTouchPointer p1 = host.Window.TouchBegin(new Point(center.X - 40, center.Y));
        IHeadlessTouchPointer p2 = host.Window.TouchBegin(new Point(center.X + 40, center.Y));
        host.Window.TouchMove(p1, new Point(center.X - 80, center.Y));
        host.Window.TouchMove(p2, new Point(center.X + 80, center.Y));
        HeadlessTestHelpers.Render();
        Assert.That(host.Model.Options.Value.Scale, Is.EqualTo(1f).Within(0.01f));

        // Lift only the second finger while keeping the first finger captured by the recognizer.
        host.Window.TouchEnd(p2, new Point(center.X + 80, center.Y));
        HeadlessTestHelpers.Render();

        // Touch down a new second finger and spread again to double scale once more.
        IHeadlessTouchPointer p3 = host.Window.TouchBegin(new Point(center.X, center.Y));
        host.Window.TouchMove(p1, new Point(center.X - 120, center.Y));
        host.Window.TouchMove(p3, new Point(center.X + 40, center.Y));
        HeadlessTestHelpers.Render();
        host.Window.TouchEnd(p1, new Point(center.X - 120, center.Y));
        host.Window.TouchEnd(p3, new Point(center.X + 40, center.Y));
        HeadlessTestHelpers.Render();

        Assert.That(host.Model.Options.Value.Scale, Is.EqualTo(2f).Within(0.01f));
    }

    private static void DispatchTwoFingerGesture(
        Window window,
        Point p1Start,
        Point p2Start,
        Point p1End,
        Point p2End,
        int steps = 4)
    {
        IHeadlessTouchPointer pointer1 = window.TouchBegin(p1Start);
        IHeadlessTouchPointer pointer2 = window.TouchBegin(p2Start);

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            Point cur1 = new(p1Start.X + (p1End.X - p1Start.X) * t, p1Start.Y + (p1End.Y - p1Start.Y) * t);
            Point cur2 = new(p2Start.X + (p2End.X - p2Start.X) * t, p2Start.Y + (p2End.Y - p2Start.Y) * t);
            window.TouchMove(pointer1, cur1);
            window.TouchMove(pointer2, cur2);
            HeadlessTestHelpers.Render();
        }

        window.TouchEnd(pointer1, p1End);
        window.TouchEnd(pointer2, p2End);
        HeadlessTestHelpers.Render();
    }
}
