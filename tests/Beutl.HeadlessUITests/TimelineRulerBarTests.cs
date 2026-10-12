using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineRulerBarTests
{
    [AvaloniaTest]
    [TestCase(640, false)]
    [TestCase(960, false)]
    [TestCase(640, true)]
    [TestCase(960, true)]
    public async Task Compact_ruler_aligns_with_content_and_preserves_pointer_operations(int width, bool light)
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "timeline-ruler",
            TimeSpan.FromSeconds(2),
            [new ElementDescription(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(1.2), 0,
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
        var marker = new SceneMarker(TimeSpan.FromSeconds(0.8), name: "Cue");
        scene.Markers.Add(marker);
        HeadlessTestHelpers.Render();
        CheckLayout();

        // The 32px ruler and the 6px margin above it seek along the same time axis.
        Click(ruler, new Point(75, 24));
        Assert.That(model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
        Click(bar, new Point(90, 4));
        Assert.That(model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.6)));

        Drag(ruler, new Point(120, 8), new Point(180, 8), TimelineHelper.MouseFlags.MarkerPressed);
        Assert.That(marker.Time, Is.EqualTo(TimeSpan.FromSeconds(1.2)));

        // Playback range handles are hit in the ruler's own coordinates.
        Drag(ruler, new Point(298, 8), new Point(270, 8), TimelineHelper.MouseFlags.EndingBarMarkerPressed);
        Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.8)));
        Drag(ruler, new Point(2, 8), new Point(30, 8), TimelineHelper.MouseFlags.StartingBarMarkerPressed);
        Assert.That(scene.Start, Is.EqualTo(TimeSpan.FromSeconds(0.2)));
        Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.6)));

        model.Options.Value = model.Options.Value with { Offset = new System.Numerics.Vector2(75, 0) };
        HeadlessTestHelpers.Render();
        Assert.That(content.Offset.X, Is.EqualTo(75));
        CheckLayout();
        Click(bar, new Point(75, 4));
        Assert.That(model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(1)));

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
