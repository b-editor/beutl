using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using AvaColors = Avalonia.Media.Colors;
using BtlColors = Beutl.Media.Colors;
using BtlPixelSize = Beutl.Media.PixelSize;

namespace Beutl.HeadlessUITests;

// The seek bar marks each marker where the thumb stands at the marker's time, its tip tells the time under the
// pointer and the marker there, and a click on a mark moves to the marker's time exactly (#4065).
[NonParallelizable]
[TestFixture]
public sealed class PlayerSeekBarTests
{
    private const string MarkerName = "Intro";
    private static readonly MediaTime s_markerTime = MediaTime.FromSeconds(100);

    private static async Task<(EditViewModel Editor, PlayerView View, Window Window, SceneMarker Marker)> Open(
        params MediaTime[] moreMarkers)
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("seek-bar")).Editor;
        editor.Scene.FrameSize.Value = new BtlPixelSize(1920, 1080);
        var marker = new SceneMarker(s_markerTime, MarkerName, BtlColors.Red);
        editor.Scene.Markers.Add(marker);
        foreach (MediaTime time in moreMarkers)
            editor.Scene.Markers.Add(new SceneMarker(time, $"At {time}", BtlColors.Red));

        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1200, Height = 520 };
        window.Show();
        HeadlessTestHelpers.Render();
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
        ToolTip.SetShowDelay(SeekBar(view), 0);
        return (editor, view, window, marker);
    }

    private static PlayerSeekBar SeekBar(PlayerView view)
        => view.Player.GetVisualDescendants().OfType<PlayerSeekBar>().Single();

    // Moves the playhead, and gives where the thumb's center then stands in the window.
    private static Point ThumbCenterAt(EditViewModel editor, PlayerView view, Window window, MediaTime time)
    {
        editor.GetRequiredService<IEditorClock>().Seek(time);
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
        return HeadlessTestHelpers.Center(SeekBar(view).GetVisualDescendants().OfType<Thumb>().Single(), window);
    }

    // A point on the bar's middle line, the given part of the way along it.
    private static Point AlongTheBar(PlayerView view, Window window, double part)
    {
        PlayerSeekBar seekBar = SeekBar(view);
        return seekBar.TranslatePoint(new Point(seekBar.Bounds.Width * part, seekBar.Bounds.Height / 2), window)!.Value;
    }

    // The lines the tip shows, from the top.
    private static string[] TipLines(PlayerSeekBar seekBar)
        => [.. ((Panel)ToolTip.GetTip(seekBar)!).Children.OfType<TextBlock>()
            .Where(line => line.IsVisible)
            .Select(line => line.Text ?? string.Empty)];

    // The box of the tip on screen, in the window: the headless platform shows popups in the window.
    private static Rect ShownTip(Window window)
    {
        ToolTip tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
        return new Rect(tip.TranslatePoint(default, window)!.Value, tip.Bounds.Size);
    }

    private static (int R, int G, int B) PixelAt(Window window, Point point)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        return HeadlessTestHelpers.ReadPixel(frame, new PixelPoint((int)Math.Floor(point.X), (int)Math.Floor(point.Y)));
    }

    private static bool IsRed((int R, int G, int B) pixel) => pixel.R > 200 && pixel.G < 60 && pixel.B < 60;

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
    }

    // Near the ends, a mark that ignored the thumb's inset would miss the thumb's center by 4 px or more.
    [AvaloniaTest]
    public async Task A_mark_stands_where_the_thumb_does_at_its_time_and_lies_under_the_thumb()
    {
        MediaTime nearTheStart = MediaTime.FromSeconds(15);
        MediaTime nearTheEnd = MediaTime.FromSeconds(285);
        (EditViewModel editor, PlayerView view, Window window, _) = await Open(nearTheStart, nearTheEnd);
        try
        {
            // 3 px above the middle line, only the mark and the thumb draw.
            var above = new Vector(0, -3);
            Point startMark = ThumbCenterAt(editor, view, window, nearTheStart) + above;
            (int R, int G, int B) underTheThumb = PixelAt(window, startMark);
            Point endMark = ThumbCenterAt(editor, view, window, nearTheEnd) + above;
            ThumbCenterAt(editor, view, window, MediaTime.FromSeconds(150));
            (int R, int G, int B) atTheStart = PixelAt(window, startMark);
            (int R, int G, int B) atTheEnd = PixelAt(window, endMark);

            Assert.Multiple(() =>
            {
                Assert.That(IsRed(atTheStart), Is.True, $"The mark near the start: {atTheStart}");
                Assert.That(IsRed(atTheEnd), Is.True, $"The mark near the end: {atTheEnd}");
                Assert.That(IsRed(underTheThumb), Is.False, $"The thumb over the mark: {underTheThumb}");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_tip_tells_the_time_that_a_click_there_moves_to()
    {
        (_, PlayerView view, Window window, _) = await Open();
        try
        {
            PlayerSeekBar seekBar = SeekBar(view);
            Point point = AlongTheBar(view, window, 0.6);
            TimelineTestHost.WaitForHitTest(window, seekBar, point);

            window.MouseMove(point);
            HeadlessTestHelpers.Render();
            bool open = ToolTip.GetIsOpen(seekBar);
            string[] tip = TipLines(seekBar);
            Rect shown = ShownTip(window);
            double barTop = seekBar.TranslatePoint(default, window)!.Value.Y;
            Click(window, point);

            Assert.Multiple(() =>
            {
                Assert.That(open, Is.True, "The tip opens over the bar.");
                Assert.That(tip, Is.EqualTo(new[] { view.Player.CurrentTime }));
                Assert.That(shown.Center.X, Is.EqualTo(point.X).Within(1), "The tip stands over the pointer.");
                Assert.That(shown.Bottom, Is.LessThanOrEqualTo(barTop), "The tip stands above the bar.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Over_a_mark_the_tip_names_the_marker_and_stands_over_the_mark()
    {
        (EditViewModel editor, PlayerView view, Window window, _) = await Open();
        try
        {
            PlayerSeekBar seekBar = SeekBar(view);
            Point mark = ThumbCenterAt(editor, view, window, s_markerTime);
            ThumbCenterAt(editor, view, window, MediaTime.Zero);
            TimelineTestHost.WaitForHitTest(window, seekBar, mark);

            window.MouseMove(mark + new Vector(3, 0));
            HeadlessTestHelpers.Render();
            string[] overTheMark = TipLines(seekBar);
            Rect shown = ShownTip(window);

            window.MouseMove(mark + new Vector(9, 0));
            HeadlessTestHelpers.Render();
            string[] pastTheMark = TipLines(seekBar);

            Assert.Multiple(() =>
            {
                Assert.That(overTheMark, Is.EqualTo(new[] { MarkerName, s_markerTime.ToTimeSpan().ToString(@"hh\:mm\:ss\.ff") }));
                Assert.That(shown.Center.X, Is.EqualTo(mark.X).Within(1), "The tip stands over the mark.");
                Assert.That(pastTheMark, Has.Length.EqualTo(1).And.No.Member(MarkerName));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_click_on_a_mark_moves_to_the_marker_time_exactly()
    {
        (EditViewModel editor, PlayerView view, Window window, _) = await Open();
        try
        {
            PlayerViewModel player = editor.Player;
            Point mark = ThumbCenterAt(editor, view, window, s_markerTime);
            ThumbCenterAt(editor, view, window, MediaTime.Zero);
            TimelineTestHost.WaitForHitTest(window, SeekBar(view), mark);

            Click(window, mark + new Vector(3, 0));
            MediaTime onTheMark = player.CurrentFrame.Value;
            Click(window, mark + new Vector(9, 0));
            MediaTime pastTheMark = player.CurrentFrame.Value;

            Assert.Multiple(() =>
            {
                Assert.That(onTheMark, Is.EqualTo(s_markerTime));
                Assert.That(pastTheMark, Is.GreaterThan(s_markerTime), "Out of the mark's reach, a click goes where it lands.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_click_that_slips_within_the_mark_still_lands_on_the_marker()
    {
        (EditViewModel editor, PlayerView view, Window window, _) = await Open();
        try
        {
            PlayerViewModel player = editor.Player;
            Point mark = ThumbCenterAt(editor, view, window, s_markerTime);
            ThumbCenterAt(editor, view, window, MediaTime.Zero);
            TimelineTestHost.WaitForHitTest(window, SeekBar(view), mark);

            window.MouseDown(mark + new Vector(1, 0), MouseButton.Left);
            window.MouseMove(mark + new Vector(3, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(mark + new Vector(3, 0), MouseButton.Left);
            HeadlessTestHelpers.Settle();
            MediaTime slipped = player.CurrentFrame.Value;

            window.MouseDown(mark + new Vector(-1, 0), MouseButton.Left);
            window.MouseMove(mark + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(mark + new Vector(40, 0), MouseButton.Left);
            HeadlessTestHelpers.Settle();
            MediaTime dragged = player.CurrentFrame.Value;

            Assert.Multiple(() =>
            {
                Assert.That(slipped, Is.EqualTo(s_markerTime));
                Assert.That(dragged, Is.GreaterThan(s_markerTime + MediaTime.FromSeconds(5)), "A drag off the mark follows the pointer.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_tip_waits_out_a_drag_and_comes_back_at_the_next_move()
    {
        (_, PlayerView view, Window window, _) = await Open();
        try
        {
            PlayerSeekBar seekBar = SeekBar(view);
            Point start = AlongTheBar(view, window, 0.6);
            Point end = AlongTheBar(view, window, 0.7);
            TimelineTestHost.WaitForHitTest(window, seekBar, start);

            window.MouseMove(start);
            HeadlessTestHelpers.Render();
            bool openOnHover = ToolTip.GetIsOpen(seekBar);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render();
            bool openWhileDragging = ToolTip.GetIsOpen(seekBar);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            string playhead = view.Player.CurrentTime;

            window.MouseMove(AlongTheBar(view, window, 0.75));
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(openOnHover, Is.True, "Over the bar");
                Assert.That(openWhileDragging, Is.False, "While dragging");
                Assert.That(ToolTip.GetIsOpen(seekBar), Is.True, "At the next move");
                Assert.That(TipLines(seekBar), Is.Not.EqualTo(new[] { playhead }), "The tip tells the time under the pointer again.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // The color changes last, so only a refresh on a color change can carry it.
    [AvaloniaTest]
    public async Task The_marks_follow_the_markers_time_name_and_color()
    {
        (_, PlayerView view, Window window, SceneMarker marker) = await Open();
        try
        {
            marker.Time.Value = MediaTime.FromSeconds(50);
            marker.Name.Value = "Chorus";
            marker.Color.Value = BtlColors.Blue;
            HeadlessTestHelpers.Settle();

            PlayerMarkerEntry entry = view.Player.Markers!.Single();
            Assert.Multiple(() =>
            {
                Assert.That(entry.Time, Is.EqualTo(MediaTime.FromSeconds(50)));
                Assert.That(entry.Name, Is.EqualTo("Chorus"));
                Assert.That(entry.Color, Is.EqualTo(AvaColors.Blue));
                Assert.That(SeekBar(view).Markers, Is.SameAs(view.Player.Markers));
            });
        }
        finally
        {
            window.Close();
        }
    }
}
