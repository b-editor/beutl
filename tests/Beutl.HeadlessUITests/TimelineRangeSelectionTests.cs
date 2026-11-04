using System.Collections.Specialized;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Microsoft.Extensions.DependencyInjection;

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
            new Point(MediaTime.FromSeconds(1.5).TimeToPixel(1), session.Model.CalculateLayerTop(3) - 10),
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
        session.Model.SelectElement(session.Model.AllElements().First());
        session.Drag(session.Ruler.TranslatePoint(new Point(15, 5), session.Window)!.Value,
            session.Ruler.TranslatePoint(new Point(225, 25), session.Window)!.Value, []);
    }

    // After Effects keeps the playhead on its ruler, so a click on the clips' area only selects (#1379).
    [AvaloniaTest]
    public async Task With_seeking_on_click_off_a_plain_drag_selects_and_leaves_the_playhead()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            session.Model.Seek(MediaTime.FromSeconds(4));
            HeadlessTestHelpers.Render();
            MediaTime before = session.Model.CurrentTime.Value;

            session.Drag(session.ToWindow(new Point(15, 5)),
                session.ToWindow(new Point(225, session.Model.CalculateLayerTop(3) - 10)), [0, 2], RawInputModifiers.None);

            Assert.That(session.Model.CurrentTime.Value, Is.EqualTo(before));
        }
        finally
        {
            setting.Value = true;
        }
    }

    [AvaloniaTest]
    public async Task With_seeking_on_click_off_the_ruler_still_moves_the_playhead()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            session.Model.Seek(MediaTime.FromSeconds(4));
            HeadlessTestHelpers.Render();
            MediaTime before = session.Model.CurrentTime.Value;
            Point ruler = session.Ruler.TranslatePoint(new Point(100, 15), session.Window)!.Value;

            session.Window.MouseMove(ruler);
            session.Window.MouseDown(ruler, MouseButton.Left);
            session.Window.MouseUp(ruler, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.That(session.Model.CurrentTime.Value, Is.Not.EqualTo(before));
        }
        finally
        {
            setting.Value = true;
        }
    }

    // The ruler bar is taller than the scale drawn in it.
    [AvaloniaTest]
    public async Task With_seeking_on_click_off_the_strip_above_the_scale_moves_the_playhead_too()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            session.Model.Seek(MediaTime.FromSeconds(4));
            HeadlessTestHelpers.Render();
            MediaTime before = session.Model.CurrentTime.Value;
            Point strip = session.Ruler.TranslatePoint(new Point(100, -2), session.Window)!.Value;
            Assert.That(session.Ruler.GetVisualParent()!.Bounds.Height, Is.GreaterThan(session.Ruler.Bounds.Height));

            session.Window.MouseMove(strip);
            session.Window.MouseDown(strip, MouseButton.Left);
            session.Window.MouseUp(strip, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.That(session.Model.CurrentTime.Value, Is.Not.EqualTo(before));
        }
        finally
        {
            setting.Value = true;
        }
    }

    // A locked clip takes no drag, so its press carries on to the timeline after selecting it.
    [AvaloniaTest]
    public async Task With_seeking_on_click_off_a_click_on_a_locked_clip_selects_it_and_leaves_the_playhead()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            TimelineTabViewModel model = session.Model;
            Element clip = model.Scene.Children.Single(child => child.ZIndex.Value == 3);
            clip.IsLocked.Value = true;
            model.Seek(MediaTime.FromSeconds(4));
            HeadlessTestHelpers.Render();
            MediaTime before = model.CurrentTime.Value;
            Point point = session.ToWindow(new Point(100, model.CalculateLayerTop(3) + 5));

            session.Window.MouseMove(point);
            session.Window.MouseDown(point, MouseButton.Left);
            session.Window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(model.SelectedElements.Select(element => element.Model), Is.EqualTo(new[] { clip }));
                Assert.That(model.CurrentTime.Value, Is.EqualTo(before));
            });
        }
        finally
        {
            setting.Value = true;
        }
    }

    // The bar can be taken where it is drawn, without a trip to the ruler or losing what is selected.
    [AvaloniaTest]
    public async Task With_seeking_on_click_off_dragging_the_bar_moves_the_playhead_and_keeps_the_selection()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            TimelineTabViewModel model = session.Model;
            model.SelectElement(model.AllElements().First());
            MediaTime before = session.PutBarAtOneSecond();
            Point press = session.NearBar(3, model.CalculateLayerTop(1) + 5);
            Point to = session.ToWindow(new Point(MediaTime.FromSeconds(2).TimeToPixel(1), model.CalculateLayerTop(1) + 5));

            session.Window.MouseMove(press);
            session.Window.MouseDown(press, MouseButton.Left);
            Assert.That(model.CurrentTime.Value, Is.EqualTo(before));
            session.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render();
            Assert.That(model.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            HeadlessCapture.SaveIfRequested(session.Window, nameof(TimelineRangeSelectionTests), "bar-dragged");
            session.Window.MouseUp(to, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(model.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
                Assert.That(model.SelectedElements, Has.Count.EqualTo(1));
            });
        }
        finally
        {
            setting.Value = true;
        }
    }

    [AvaloniaTest]
    public async Task With_seeking_on_click_off_a_click_on_the_bar_leaves_the_playhead()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            MediaTime before = session.PutBarAtOneSecond();
            Point point = session.NearBar(3, session.Model.CalculateLayerTop(1) + 5);

            session.Window.MouseMove(point);
            session.Window.MouseDown(point, MouseButton.Left);
            session.Window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.That(session.Model.CurrentTime.Value, Is.EqualTo(before));
        }
        finally
        {
            setting.Value = true;
        }
    }

    // A locked clip leaves its press to the timeline, and the bar over it is still the clip's to click.
    [AvaloniaTest]
    public async Task With_seeking_on_click_off_a_clip_under_the_bar_keeps_the_press()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            TimelineTabViewModel model = session.Model;
            Element clip = model.Scene.Children.Single(child => child.ZIndex.Value == 3);
            clip.IsLocked.Value = true;
            MediaTime before = session.PutBarAtOneSecond();
            double row = model.CalculateLayerTop(3) + 5;
            Point press = session.NearBar(3, row);
            Point to = session.NearBar(100, row);

            session.Window.MouseMove(press);
            session.Window.MouseDown(press, MouseButton.Left);
            session.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            session.Window.MouseUp(to, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(model.SelectedElements.Select(element => element.Model), Is.EqualTo(new[] { clip }));
                Assert.That(model.CurrentTime.Value, Is.EqualTo(before));
            });
        }
        finally
        {
            setting.Value = true;
        }
    }

    [AvaloniaTest]
    public async Task With_seeking_on_click_off_the_cursor_shows_where_the_bar_can_be_dragged()
    {
        IProperty<bool> setting = GlobalConfiguration.Instance.EditorConfig.SeekOnTimelineClick;
        setting.Value = false;
        try
        {
            using TimelineSession session = await TimelineSession.CreateAsync();
            TimelineTabViewModel model = session.Model;
            session.PutBarAtOneSecond();
            double empty = model.CalculateLayerTop(1) + 5;

            Assert.Multiple(() =>
            {
                Assert.That(CursorAt(session.NearBar(4, empty)), Is.EqualTo(Cursors.SizeWestEast));
                Assert.That(CursorAt(session.NearBar(-4, empty)), Is.EqualTo(Cursors.SizeWestEast));
                Assert.That(CursorAt(session.NearBar(5, empty)), Is.EqualTo(Cursors.Arrow));
                Assert.That(CursorAt(session.NearBar(3, model.CalculateLayerTop(0) + 5)), Is.EqualTo(Cursors.Arrow));

                model.IsRazorMode.Value = true;
                HeadlessTestHelpers.Render();
                Assert.That(CursorAt(session.NearBar(3, empty)), Is.EqualTo(Cursors.Cross));
                model.IsRazorMode.Value = false;
                HeadlessTestHelpers.Render();

                setting.Value = true;
                Assert.That(CursorAt(session.NearBar(3, empty)), Is.EqualTo(Cursors.Arrow));
            });

            Cursor? CursorAt(Point point)
            {
                session.Window.MouseMove(point);
                return session.Timeline.Cursor;
            }
        }
        finally
        {
            setting.Value = true;
        }
    }

    [AvaloniaTest]
    public async Task By_default_a_plain_click_on_the_timeline_moves_the_playhead()
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        session.Model.Seek(MediaTime.FromSeconds(4));
        HeadlessTestHelpers.Render();
        MediaTime before = session.Model.CurrentTime.Value;
        Point point = session.ToWindow(new Point(100, session.Model.CalculateLayerTop(1) + 5));

        session.Window.MouseMove(point);
        session.Window.MouseDown(point, MouseButton.Left);
        session.Window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Render();

        Assert.That(session.Model.CurrentTime.Value, Is.Not.EqualTo(before));
    }

    [AvaloniaTest]
    public async Task Moving_the_range_only_touches_the_clips_entering_or_leaving_it()
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        TimelineTabViewModel model = session.Model;
        ICoreList<Element> selection = model.EditorContext.GetRequiredService<IEditorSelection>().SelectedElements;
        Point press = session.Ruler.TranslatePoint(new Point(15, 10), session.Window)!.Value;
        Point Row(int layer) => session.ToWindow(new Point(100, model.CalculateLayerTop(layer + 1) - 10));
        RawInputModifiers modifier = TimelineTestHost.CommandModifier;

        session.Window.MouseMove(press, modifier);
        session.Window.MouseDown(press, MouseButton.Left, modifier);
        try
        {
            session.Window.MouseMove(Row(2), modifier | RawInputModifiers.LeftMouseButton);
            Assert.That(selection.Select(e => e.ZIndex.Value), Is.EquivalentTo(new[] { 0, 2 }));

            ElementViewModel kept = model.SelectedElements.First();
            int keptNotifications = 0;
            using IDisposable subscription = kept.IsSelected.Skip(1).Subscribe(_ => keptNotifications++);
            var changes = new List<NotifyCollectionChangedEventArgs>();
            selection.CollectionChanged += OnChanged;
            void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => changes.Add(e);
            try
            {
                session.Window.MouseMove(Row(3), modifier | RawInputModifiers.LeftMouseButton);
                Assert.That(selection.Select(e => e.ZIndex.Value), Is.EquivalentTo(new[] { 0, 2, 3 }));
                Assert.That(changes.Select(e => (e.Action, e.NewItems?.Count)),
                    Is.EqualTo(new (NotifyCollectionChangedAction, int?)[] { (NotifyCollectionChangedAction.Add, 1) }));

                changes.Clear();
                session.Window.MouseMove(Row(2), modifier | RawInputModifiers.LeftMouseButton);
                Assert.That(selection.Select(e => e.ZIndex.Value), Is.EquivalentTo(new[] { 0, 2 }));
                Assert.That(changes.Select(e => (e.Action, e.OldItems?.Count)),
                    Is.EqualTo(new (NotifyCollectionChangedAction, int?)[] { (NotifyCollectionChangedAction.Remove, 1) }));
                Assert.That(keptNotifications, Is.Zero);
            }
            finally
            {
                selection.CollectionChanged -= OnChanged;
            }
        }
        finally
        {
            session.Window.MouseUp(Row(2), MouseButton.Left, modifier);
            HeadlessTestHelpers.Render();
        }
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
                MediaTime.FromSeconds(5),
                [Clip(0.2, 0), Clip(0.2, 2), Clip(0.2, 3), Clip(0.2, 6), Clip(2, 0)]);
            host.Model.Options.Value = host.Model.Options.Value with { MaxLayerCount = 20 };
            HeadlessTestHelpers.Render();
            return new TimelineSession(host);

            static ElementDescription Clip(double start, int layer) => new(MediaTime.FromSeconds(start),
                MediaTime.FromSeconds(1.2), layer, new ElementSource.EngineObject(() => new RectShape()));
        }

        public Point ToWindow(Point point) => Timeline.TranslatePoint(point, Window)!.Value;

        public MediaTime PutBarAtOneSecond()
        {
            Model.Seek(MediaTime.FromSeconds(1));
            HeadlessTestHelpers.Render();
            return Model.CurrentTime.Value;
        }

        public Point NearBar(double distance, double y)
            => ToWindow(new Point(Model.SeekBarMargin.Value.Left + distance, y));

        public void Scroll(double y)
        {
            Model.Options.Value = Model.Options.Value with { Offset = new System.Numerics.Vector2(75, (float)y) };
            HeadlessTestHelpers.Render();
            Assert.That(Content.Offset, Is.EqualTo(new Vector(75, y)));
        }

        public void Drag(Point from, Point to, int[] expectedLayers, RawInputModifiers? modifiers = null)
        {
            RawInputModifiers modifier = modifiers ?? TimelineTestHost.CommandModifier;
            Window.MouseMove(from, modifier);
            Window.MouseDown(from, MouseButton.Left, modifier);
            Assert.That(_host.View._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.RangeSelectionPressed));
            try
            {
                Window.MouseMove(to, modifier | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render();
                Assert.That(Model.SelectedElements.Select(e => e.Model.ZIndex.Value), Is.EquivalentTo(expectedLayers));
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
                Assert.That(Model.SelectedElements.Select(e => e.Model.ZIndex.Value), Is.EquivalentTo(expectedLayers));
            });
        }

        public void Dispose() => _host.Dispose();
    }
}
