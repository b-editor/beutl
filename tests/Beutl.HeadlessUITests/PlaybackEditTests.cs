using System.Collections.Concurrent;
using System.Diagnostics;

using Avalonia.Headless.NUnit;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// An edit during playback drops the read-ahead frames it changes and their cache, so it shows from the next frame.
// The frame being drawn when it came in is still shown, so edits that keep coming cannot hold playback.
[NonParallelizable]
[TestFixture]
public class PlaybackEditTests
{
    private const int Rate = 30;

    // Pure colors keep every channel at 0 or 255 through the color conversions.
    private static readonly Color[] s_palette =
        [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Cyan, Colors.Magenta];

    // What a frame that is not filled at all shows.
    private static readonly Color[] s_unfilled = [Colors.Black, Colors.White];

    private static readonly (double X, double Y)[] s_samples =
        [(0.5, 0.5), (0.05, 0.05), (0.95, 0.05), (0.05, 0.95), (0.95, 0.95)];

    [AvaloniaTest]
    public async Task Frames_taken_after_each_edit_show_it_whole_and_in_order()
    {
        TestGpu.RequireAnyDevice();
        (EditViewModel editor, SolidColorBrush fill) = await OpenEditor(MediaTime.FromSeconds(30));
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, new Rational(Rate), CancellationToken.None);
        player.Start(PlaybackRange.From(MediaTime.Zero, editor.Scene.Start, editor.Scene.Duration, new Rational(Rate)));
        // The edits below have read-ahead frames to drop.
        await HeadlessTestHelpers.WaitUntilAsync(() => player.QueuedFrames >= 10, TimeSpan.FromSeconds(60), pump: false);

        int lastFrame = -1;
        for (int step = 1; step <= 40; step++)
        {
            // One to three edits without a range, while frames render between them.
            Color color = default;
            for (int edit = 0; edit <= step % 3; edit++)
            {
                color = s_palette[(step + edit) % s_palette.Length];
                fill.Color.Value = color;
                await Task.Delay(1);
            }

            for (int taken = 0; taken < 2; taken++)
            {
                BufferedPlayer.Frame frame = await TakeAsync(player);
                using (frame.Bitmap)
                {
                    Assert.That(frame.Time, Is.GreaterThan(lastFrame), "Frames must stay in order.");
                    lastFrame = frame.Time;
                    Color filled = FillOf(frame.Bitmap.Value, $"Frame {frame.Time} after step {step}");
                    // Only the frame being drawn when the edits came in may show a color from before them.
                    if (taken > 0)
                        Assert.That(filled, Is.EqualTo(color), $"Frame {frame.Time} after step {step}");
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(player.Failure, Is.Null);
            Assert.That(player.ProducerStopped, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task An_edit_after_the_last_frame_was_rendered_draws_the_queued_frames_again()
    {
        TestGpu.RequireAnyDevice();
        (EditViewModel editor, SolidColorBrush fill) = await OpenEditor(MediaTime.FromSeconds(4d / Rate));
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, new Rational(Rate), CancellationToken.None);
        PlaybackRange range = PlaybackRange.From(MediaTime.Zero, editor.Scene.Start, editor.Scene.Duration, new Rational(Rate));
        int[] expected = [.. Enumerable.Range(range.StartFrame, range.EndFrame - range.StartFrame)];
        player.Start(range);
        await HeadlessTestHelpers.WaitUntilAsync(
            () => player.QueuedFrames == expected.Length, TimeSpan.FromSeconds(60), pump: false);

        fill.Color.Value = s_palette[1];

        var frames = new List<int>();
        foreach (int _ in expected)
        {
            BufferedPlayer.Frame frame = await TakeAsync(player);
            using (frame.Bitmap)
            {
                frames.Add(frame.Time);
                Assert.That(FillOf(frame.Bitmap.Value, $"Frame {frame.Time}"), Is.EqualTo(s_palette[1]));
            }
        }

        Assert.That(frames, Is.EqualTo(expected));
    }

    // Replacing an element sends no range. With the read-ahead full no frame is being drawn, so every frame after
    // the replacement shows it.
    [AvaloniaTest]
    public async Task Replacing_an_element_drops_every_queued_frame()
    {
        TestGpu.RequireAnyDevice();
        const int BudgetFrames = 10;
        (EditViewModel editor, _) = await OpenEditor(MediaTime.FromSeconds(30));
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, new Rational(Rate), CancellationToken.None)
        {
            ReadAheadBytes = ReadAheadBudget.SnapshotBytes(editor.Renderer.Value.DeviceSize) * BudgetFrames,
        };
        player.Start(PlaybackRange.From(MediaTime.Zero, editor.Scene.Start, editor.Scene.Duration, new Rational(Rate)));
        await HeadlessTestHelpers.WaitUntilAsync(
            () => player.QueuedFrames == BudgetFrames, TimeSpan.FromSeconds(60), pump: false);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        (Element replacement, _) = CreateRectangle(s_palette[2], editor.Scene.Duration);
        editor.Scene.Children[0] = replacement;

        for (int taken = 0; taken < BudgetFrames; taken++)
        {
            BufferedPlayer.Frame frame = await TakeAsync(player);
            using (frame.Bitmap)
                Assert.That(FillOf(frame.Bitmap.Value, $"Frame {frame.Time}"), Is.EqualTo(s_palette[2]));
        }
    }

    // A clip added after one with an exit transition turns its fade to nothing before the cut into a blend.
    [AvaloniaTest]
    public async Task Adding_a_clip_after_an_exit_transition_redraws_the_frames_before_the_cut()
    {
        TestGpu.RequireAnyDevice();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
            "playback-edits-transition", 320, 240, frameRate: Rate, duration: MediaTime.FromSeconds(4));
        (Element outgoing, _) = CreateRectangle(Colors.Red, MediaTime.FromSeconds(2));
        outgoing.ExitTransition = new CrossDissolveTransition { Duration = { Value = MediaTime.FromSeconds(1) } };
        scene.Children.Add(outgoing);
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(120));
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, new Rational(Rate), CancellationToken.None);
        player.Start(PlaybackRange.From(MediaTime.Zero, scene.Start, scene.Duration, new Rational(Rate)));
        // The fade's frames, 30 to 59, are queued before the clip after the cut arrives.
        await HeadlessTestHelpers.WaitUntilAsync(() => player.QueuedFrames >= 70, TimeSpan.FromSeconds(60), pump: false);

        (Element incoming, _) = CreateRectangle(Colors.Blue, MediaTime.FromSeconds(2));
        incoming.Start = MediaTime.FromSeconds(2);
        scene.Children.Add(incoming);

        BufferedPlayer.Frame frame = await TakeAsync(player);
        while (frame.Time < 45)
        {
            frame.Bitmap.Dispose();
            frame = await TakeAsync(player);
        }

        using (frame.Bitmap)
        {
            // A producer that stopped before the edit leaves the frames drawn without the clip queued (#3429).
            Assert.That(player.Failure, Is.Null, $"Rendering failed at frame {player.Failure?.Frame}: {player.Failure?.Exception}");
            Assert.That(player.ProducerStopped, Is.False, "The producer stopped, so the edit dropped no queued frame.");
            using Bitmap bgra = frame.Bitmap.Value.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul);
            Span<byte> center = bgra.GetRow(bgra.Height / 2).Slice(bgra.Width / 2 * 4, 4);
            Assert.That(frame.Time, Is.EqualTo(45));
            Assert.That(center[0], Is.GreaterThan(40),
                $"Half way through the transition the incoming clip must show (red {center[2]}).");
        }
    }

    // A drag on a drawable that takes longer to draw than the drag's edits take to come.
    [AvaloniaTest]
    public async Task Edits_that_come_faster_than_frames_draw_do_not_hold_playback()
    {
        TestGpu.RequireAnyDevice();
        (EditViewModel editor, _) = await OpenEditor(MediaTime.FromSeconds(30));
        var slow = new SlowPlaybackDrawable();
        var element = new Element { Length = editor.Scene.Duration, ZIndex = 1 };
        element.AddObject(slow);
        editor.Scene.Children.Add(element);
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(120));
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, new Rational(Rate), CancellationToken.None);
        var frames = new ConcurrentQueue<int>();
        using var stop = new CancellationTokenSource();
        player.Start(PlaybackRange.From(MediaTime.Zero, editor.Scene.Start, editor.Scene.Duration, new Rational(Rate)));
        Task consumer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (player.TryDequeue(out BufferedPlayer.Frame frame))
                {
                    frames.Enqueue(frame.Time);
                    frame.Bitmap.Dispose();
                }
            }
        });

        try
        {
            var stopwatch = Stopwatch.StartNew();
            for (int edit = 1; stopwatch.Elapsed < TimeSpan.FromSeconds(1.5); edit++)
            {
                slow.Opacity.Value = 100 - (edit * 0.001f);
                await Task.Delay(5);
            }
        }
        finally
        {
            stop.Cancel();
            await consumer.WaitAsync(TimeSpan.FromSeconds(10));
        }

        int[] taken = [.. frames];
        Assert.Multiple(() =>
        {
            Assert.That(player.Failure, Is.Null);
            Assert.That(taken, Has.Length.GreaterThanOrEqualTo(5), "Playback must go on while the edits come.");
            Assert.That(taken, Is.Ordered.Ascending.And.Unique);
        });
    }

    private static async Task<BufferedPlayer.Frame> TakeAsync(BufferedPlayer player)
    {
        var stopwatch = Stopwatch.StartNew();
        BufferedPlayer.Frame frame;
        while (!player.TryDequeue(out frame))
        {
            if (player.Failure is { } failure)
                Assert.Fail($"Rendering failed: {failure.Exception}");
            if (stopwatch.Elapsed > TimeSpan.FromMinutes(1))
                Assert.Fail("No frame came within a minute.");

            await Task.Delay(1);
        }

        return frame;
    }

    // The palette color the whole frame is filled with; a frame filled with more than one is torn.
    private static Color FillOf(Bitmap bitmap, string message)
    {
        using Bitmap bgra = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul);
        var filled = new List<Color>();
        foreach ((double fx, double fy) in s_samples)
        {
            int x = (int)(fx * (bgra.Width - 1));
            int y = (int)(fy * (bgra.Height - 1));
            Span<byte> pixel = bgra.GetRow(y).Slice(x * 4, 4);
            Color actual = Color.FromArgb(255, pixel[2], pixel[1], pixel[0]);
            filled.Add(s_palette.Concat(s_unfilled)
                .MinBy(color => Math.Abs(color.R - actual.R) + Math.Abs(color.G - actual.G) + Math.Abs(color.B - actual.B)));
        }

        Assert.That(filled.Distinct().Count(), Is.EqualTo(1), $"{message} is not filled with one color: {string.Join(", ", filled)}.");
        Assert.That(s_unfilled, Does.Not.Contain(filled[0]), $"{message} is not filled.");
        return filled[0];
    }

    private static (Element Element, SolidColorBrush Fill) CreateRectangle(Color color, MediaTime length)
    {
        var fill = new SolidColorBrush(color);
        var element = new Element { Length = length };
        element.AddObject(new RectShape
        {
            Width = { Value = 320 },
            Height = { Value = 240 },
            Fill = { Value = fill },
        });
        return (element, fill);
    }

    private static async Task<(EditViewModel Editor, SolidColorBrush Fill)> OpenEditor(MediaTime duration)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
            "playback-edits", 320, 240, frameRate: Rate, duration: duration);
        (Element element, SolidColorBrush fill) = CreateRectangle(s_palette[0], duration);
        scene.Children.Add(element);

        // Let the preview render queued by the edit above finish first.
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(120));
        return (editor, fill);
    }
}

// Draws nothing, slowly, each time it changes.
internal sealed partial class SlowPlaybackDrawable : Drawable
{
    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(16, 16);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource) => Thread.Sleep(30);
}
