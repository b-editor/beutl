using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineRangeSelectionTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Range_crossing_ruler_selects_overlapping_clips(bool reverse, bool scrolled)
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        if (scrolled)
        {
            session.Scroll(session.Model.LayerHeaders[0].Height.Value / 2);
        }

        Point rulerPoint = session.Ruler.TranslatePoint(new Point(15, 10), session.Window)!.Value;
        Point contentPoint = session.Timeline.TranslatePoint(
            new Point(TimeSpan.FromSeconds(1.5).TimeToPixel(1), session.Model.CalculateLayerTop(3) - 10),
            session.Window)!.Value;

        session.Drag(reverse ? contentPoint : rulerPoint, reverse ? rulerPoint : contentPoint, [0, 2]);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Range_crossing_ruler_after_deep_scroll_selects_only_visible_clips(bool reverse)
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        session.Scroll(100);
        Point rulerPoint = session.Ruler.TranslatePoint(new Point(15, 5), session.Window)!.Value;
        Point contentPoint = session.ToWindow(new Point(225, session.Model.CalculateLayerTop(7) - 10));

        session.Drag(reverse ? contentPoint : rulerPoint, reverse ? rulerPoint : contentPoint, [6]);
    }

    [AvaloniaTest]
    public async Task Range_crossing_left_edge_selects_overlapping_clips()
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        session.Drag(session.ToWindow(new Point(225, session.Model.CalculateLayerTop(3) - 10)),
            session.ToWindow(new Point(-30, 5)), [0, 2]);
    }

    [AvaloniaTest]
    public async Task Range_crossing_bottom_edge_selects_clips_without_adding_layers()
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        int layerCount = session.Model.LayerHeaders.Count;
        session.Drag(session.ToWindow(new Point(15, 5)),
            session.ToWindow(new Point(225, session.Timeline.Bounds.Height + 40)), [0, 2, 3, 6]);
        Assert.That(session.Model.LayerHeaders, Has.Count.EqualTo(layerCount));
    }

    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(100)]
    public async Task Range_entirely_in_ruler_does_not_select_clips(double scrollY)
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        if (scrollY > 0) session.Scroll(scrollY);
        session.Model.SelectElement(session.Model.Elements.First());
        session.Drag(session.Ruler.TranslatePoint(new Point(15, 5), session.Window)!.Value,
            session.Ruler.TranslatePoint(new Point(225, 25), session.Window)!.Value, []);
    }

    private sealed class TimelineSession : IDisposable
    {
        private readonly TimelineTestHost _host;
        private readonly TimelineOverlay _overlay;

        private TimelineSession(TimelineTestHost host)
        {
            _host = host;
            _overlay = host.View.FindControl<TimelineOverlay>("overlay")!;
        }

        public TimelineTabViewModel Model => _host.Model;
        public Window Window => _host.Window;
        public ScrollViewer Content => _host.Content;
        public Panel Timeline => _host.Timeline;
        public TimelineScale Ruler => _host.Ruler;

        public static async Task<TimelineSession> CreateAsync()
        {
            TimelineTestHost host = await TimelineTestHost.CreateAsync(
                "timeline-selection",
                TimeSpan.FromSeconds(5),
                [Clip(0.2, 0), Clip(0.2, 2), Clip(0.2, 3), Clip(0.2, 6), Clip(2, 0)]);
            host.Model.Options.Value = host.Model.Options.Value with { MaxLayerCount = 20 };
            HeadlessTestHelpers.Render();
            return new TimelineSession(host);

            static ElementDescription Clip(double start, int layer) => new(TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(1.2), layer, new ElementSource.EngineObject(() => new RectShape()));
        }

        public Point ToWindow(Point point) => Timeline.TranslatePoint(point, Window)!.Value;

        public void Scroll(double y)
        {
            Model.Options.Value = Model.Options.Value with { Offset = new System.Numerics.Vector2(75, (float)y) };
            HeadlessTestHelpers.Render();
            Assert.That(Content.Offset, Is.EqualTo(new Vector(75, y)));
        }

        public void Drag(Point from, Point to, int[] expectedLayers)
        {
            RawInputModifiers modifier = TimelineTestHost.CommandModifier;
            Window.MouseMove(from, modifier);
            Window.MouseDown(from, MouseButton.Left, modifier);
            Assert.That(_host.View._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.RangeSelectionPressed));
            try
            {
                Window.MouseMove(to, modifier | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render();
                Assert.That(Model.SelectedElements.Select(e => e.Model.ZIndex), Is.EquivalentTo(expectedLayers));
                Assert.That(Model.SelectedElements.All(e => e.IsSelected.Value), Is.True);
            }
            finally
            {
                Window.MouseUp(to, MouseButton.Left, modifier);
                HeadlessTestHelpers.Render();
            }

            Assert.Multiple(() =>
            {
                Assert.That(_host.View._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.Free));
                Assert.That(_overlay.SelectionRange, Is.EqualTo(default(Rect)));
                Assert.That(Model.SelectedElements.Select(e => e.Model.ZIndex), Is.EquivalentTo(expectedLayers));
            });
        }

        public void Dispose() => _host.Dispose();
    }
}
