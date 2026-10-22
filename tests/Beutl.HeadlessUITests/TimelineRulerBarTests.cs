using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineRulerBarTests
{
    public enum RulerDragEnd { CaptureLoss, CaptureMoved, Playback, Escape, Undo }

    [AvaloniaTest]
    [TestCase("marker", RulerDragEnd.CaptureLoss)]
    [TestCase("marker", RulerDragEnd.Playback)]
    [TestCase("marker", RulerDragEnd.Escape)]
    [TestCase("marker", RulerDragEnd.Undo)]
    [TestCase("end", RulerDragEnd.CaptureLoss)]
    [TestCase("end", RulerDragEnd.Playback)]
    [TestCase("end", RulerDragEnd.Escape)]
    [TestCase("end", RulerDragEnd.Undo)]
    [TestCase("marker", RulerDragEnd.CaptureMoved)]
    [TestCase("end", RulerDragEnd.CaptureMoved)]
    [TestCase("start", RulerDragEnd.CaptureLoss)]
    [TestCase("start", RulerDragEnd.Escape)]
    public async Task Capture_loss_and_playback_keep_a_ruler_drag_and_Escape_and_Undo_revert_it(string target, RulerDragEnd end)
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-ruler-drag-end", MediaTime.FromSeconds(2));
        var editor = (EditViewModel)host.Model.EditorContext;
        HistoryManager history = editor.HistoryManager;
        Scene scene = host.Scene;
        Window window = host.Window;
        var marker = new SceneMarker(MediaTime.FromSeconds(0.8), name: "Cue");
        scene.Markers.Add(marker);
        history.Commit();
        history.ExecuteInTransaction(() => marker.Note = "earlier", "Earlier");
        HeadlessTestHelpers.Render();
        int undoCount = history.UndoCount;
        IPointer? pointer = null;
        host.RulerBar.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // 150 px per second: the marker sits at 120 px, the ending bar at 300 px and the starting bar at 0 px.
        (double from, double to, MediaTime initial, MediaTime reached) = target switch
        {
            "marker" => (120d, 180d, MediaTime.FromSeconds(0.8), MediaTime.FromSeconds(1.2)),
            "end" => (298d, 270d, MediaTime.FromSeconds(2), MediaTime.FromSeconds(1.8)),
            _ => (2d, 30d, MediaTime.Zero, MediaTime.FromSeconds(0.2))
        };
        Point press = host.Ruler.TranslatePoint(new Point(from, 8), window)!.Value;
        Point moved = host.Ruler.TranslatePoint(new Point(to, 8), window)!.Value;
        window.MouseMove(press);
        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render();
        Assert.That(Value(), Is.EqualTo(reached));
        Assert.That(history.IsEditing, Is.True, "The drag must hold an edit session.");
        // The scale under the press holds the capture, inside the ruler bar that the drag watches.
        Assert.That(pointer!.Captured, Is.SameAs(host.Ruler));

        switch (end)
        {
            case RulerDragEnd.CaptureLoss:
                pointer!.Capture(null);
                break;
            case RulerDragEnd.CaptureMoved:
                pointer!.Capture(host.Timeline);
                break;
            case RulerDragEnd.Playback:
                editor.Player.IsPlaying.Value = true;
                HeadlessTestHelpers.Settle();
                editor.Player.IsPlaying.Value = false;
                break;
            case RulerDragEnd.Escape:
                window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                break;
            case RulerDragEnd.Undo:
                Assert.That(await editor.UndoAsync(), Is.True);
                break;
        }

        HeadlessTestHelpers.Settle();
        Assert.That(history.IsEditing, Is.False, "The end closes the session before any release.");
        // The pointer no longer holds the marker or bar, so a further move or release changes nothing.
        window.MouseMove(moved + new Vector(30, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(moved + new Vector(30, 0), MouseButton.Left);
        HeadlessTestHelpers.Settle();

        bool kept = end is RulerDragEnd.CaptureLoss or RulerDragEnd.CaptureMoved or RulerDragEnd.Playback;
        Assert.Multiple(() =>
        {
            Assert.That(Value(), Is.EqualTo(kept ? reached : initial));
            Assert.That(scene.Start + scene.Duration, Is.EqualTo(MediaTime.FromSeconds(target == "end" && kept ? 1.8 : 2)));
            Assert.That(marker.Note, Is.EqualTo("earlier"), "Undo during the drag leaves earlier history alone.");
            Assert.That(history.UndoCount, Is.EqualTo(undoCount + (kept ? 1 : 0)));
            Assert.That(history.IsEditing, Is.False);
            Assert.That(host.View._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.Free));
        });

        if (kept)
        {
            Assert.That(history.Undo(), Is.True);
            Assert.That(Value(), Is.EqualTo(initial), "The drag is one entry.");
        }

        MediaTime Value() => target switch
        {
            "marker" => marker.Time,
            "end" => scene.Duration,
            _ => scene.Start
        };
    }

    [AvaloniaTest]
    [TestCase(640, false)]
    [TestCase(960, false)]
    [TestCase(640, true)]
    [TestCase(960, true)]
    public async Task Compact_ruler_aligns_with_content_and_preserves_pointer_operations(int width, bool light)
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "timeline-ruler",
            MediaTime.FromSeconds(2),
            [new ElementDescription(MediaTime.FromSeconds(0.2), MediaTime.FromSeconds(1.2), 0,
                new ElementSource.EngineObject(() => new RectShape()))],
            width: width,
            theme: light ? ThemeVariant.Light : ThemeVariant.Dark);
        TimelineTabViewModel model = host.Model;
        Scene scene = host.Scene;
        TimelineTabView view = host.View;
        Window window = host.Window;
        Border bar = host.RulerBar;
        TimelineScale ruler = host.Ruler;
        ScrollViewer content = host.Content;
        Panel timeline = host.Timeline;
        var marker = new SceneMarker(MediaTime.FromSeconds(0.8), name: "Cue");
        scene.Markers.Add(marker);
        HeadlessTestHelpers.Render();
        CheckLayout();

        // The 32px ruler and the 6px margin above it seek along the same time axis.
        Click(ruler, new Point(75, 24));
        Assert.That(model.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
        Click(bar, new Point(90, 4));
        Assert.That(model.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(0.6)));

        Drag(ruler, new Point(120, 8), new Point(180, 8), TimelineHelper.MouseFlags.MarkerPressed);
        Assert.That(marker.Time, Is.EqualTo(MediaTime.FromSeconds(1.2)));

        // Playback range handles are hit in the ruler's own coordinates.
        Drag(ruler, new Point(298, 8), new Point(270, 8), TimelineHelper.MouseFlags.EndingBarMarkerPressed);
        Assert.That(scene.Duration, Is.EqualTo(MediaTime.FromSeconds(1.8)));
        Drag(ruler, new Point(2, 8), new Point(30, 8), TimelineHelper.MouseFlags.StartingBarMarkerPressed);
        Assert.That(scene.Start, Is.EqualTo(MediaTime.FromSeconds(0.2)));
        Assert.That(scene.Duration, Is.EqualTo(MediaTime.FromSeconds(1.6)));

        model.Options.Value = model.Options.Value with { Offset = new System.Numerics.Vector2(75, 0) };
        HeadlessTestHelpers.Render();
        Assert.That(content.Offset.X, Is.EqualTo(75));
        CheckLayout();
        Click(bar, new Point(75, 4));
        Assert.That(model.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(1)));

        Point zoomPoint = bar.TranslatePoint(new Point(100, 4), window)!.Value;
        window.MouseWheel(zoomPoint, new Vector(0, 1), TimelineTestHost.CommandModifier);
        HeadlessTestHelpers.Render();
        Assert.That(model.Options.Value.Scale, Is.EqualTo(1.2f).Within(0.0001));
        CheckLayout();

        void CheckLayout()
        {
            Assert.Multiple(() =>
            {
                Assert.That(bar.Bounds.Height, Is.EqualTo(38));
                Assert.That(ruler.Bounds.Height, Is.EqualTo(32));
                Assert.That(ruler.TranslatePoint(default, bar)!.Value.Y, Is.EqualTo(6));
                Assert.That(ruler.TranslatePoint(default, bar)!.Value.X, Is.Zero);
                Assert.That(ruler.TranslatePoint(new Point(0, ruler.Bounds.Height), view)!.Value.Y,
                    Is.EqualTo(content.TranslatePoint(default, view)!.Value.Y));
                Assert.That(ruler.TranslatePoint(new Point(100, 0), view)!.Value.X,
                    Is.EqualTo(timeline.TranslatePoint(new Point(100 + content.Offset.X, 0), view)!.Value.X).Within(0.001));
            });
        }

        void Click(Control control, Point position)
        {
            Point point = control.TranslatePoint(position, window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }

        void Drag(Control control, Point from, Point to, TimelineHelper.MouseFlags expectedMode)
        {
            Point start = control.TranslatePoint(from, window)!.Value;
            Point end = control.TranslatePoint(to, window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            Assert.That(view._mouseFlag, Is.EqualTo(expectedMode));
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.That(view._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.Free));
        }
    }
}
