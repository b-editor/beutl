using System.Diagnostics;

using Avalonia.Headless.NUnit;

using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// An edit during playback drops the read-ahead frames it changes and their cache, so it shows on the next frame.
// Edits that keep coming, as a slider drag makes them, drop and draw the frames again each time.
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
        (EditViewModel editor, SolidColorBrush fill) = await OpenEditor(TimeSpan.FromSeconds(30));
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, Rate, CancellationToken.None);
        player.Start(PlaybackRange.From(TimeSpan.Zero, editor.Scene.Start, editor.Scene.Duration, Rate));
        // The edits below have read-ahead frames to drop.
        await HeadlessTestHelpers.WaitUntilAsync(() => player.QueuedFrames >= 10, TimeSpan.FromMinutes(1), pump: false);

        int lastFrame = -1;
        for (int step = 1; step <= 40; step++)
        {
            // One to three edits without a range, while frames render between them.
            Color color = default;
            for (int edit = 0; edit <= step % 3; edit++)
            {
                color = s_palette[(step + edit) % s_palette.Length];
                fill.Color.CurrentValue = color;
                await Task.Delay(1);
            }

            for (int taken = 0; taken < 2; taken++)
            {
                BufferedPlayer.Frame frame = await TakeAsync(player);
                using (frame.Bitmap)
                {
                    Assert.That(frame.Time, Is.GreaterThan(lastFrame), "Frames must stay in order.");
                    lastFrame = frame.Time;
                    AssertFilledWith(frame.Bitmap.Value, color, $"Frame {frame.Time} after step {step}");
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
        (EditViewModel editor, SolidColorBrush fill) = await OpenEditor(TimeSpan.FromSeconds(4d / Rate));
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, Rate, CancellationToken.None);
        PlaybackRange range = PlaybackRange.From(TimeSpan.Zero, editor.Scene.Start, editor.Scene.Duration, Rate);
        int[] expected = [.. Enumerable.Range(range.StartFrame, range.EndFrame - range.StartFrame)];
        player.Start(range);
        await HeadlessTestHelpers.WaitUntilAsync(
            () => player.QueuedFrames == expected.Length, TimeSpan.FromMinutes(1), pump: false);

        fill.Color.CurrentValue = s_palette[1];

        var frames = new List<int>();
        foreach (int _ in expected)
        {
            BufferedPlayer.Frame frame = await TakeAsync(player);
            using (frame.Bitmap)
            {
                frames.Add(frame.Time);
                AssertFilledWith(frame.Bitmap.Value, s_palette[1], $"Frame {frame.Time}");
            }
        }

        Assert.That(frames, Is.EqualTo(expected));
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

    private static void AssertFilledWith(Bitmap bitmap, Color expected, string message)
    {
        using Bitmap bgra = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul);
        foreach ((double fx, double fy) in s_samples)
        {
            int x = (int)(fx * (bgra.Width - 1));
            int y = (int)(fy * (bgra.Height - 1));
            Span<byte> pixel = bgra.GetRow(y).Slice(x * 4, 4);
            Color actual = Color.FromArgb(255, pixel[2], pixel[1], pixel[0]);
            Color nearest = s_palette.Concat(s_unfilled)
                .MinBy(color => Math.Abs(color.R - actual.R) + Math.Abs(color.G - actual.G) + Math.Abs(color.B - actual.B));
            Assert.That(nearest, Is.EqualTo(expected), $"{message} at ({x}, {y}) is {actual}.");
        }
    }

    private static async Task<(EditViewModel Editor, SolidColorBrush Fill)> OpenEditor(TimeSpan duration)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
            "playback-edits", 320, 240, frameRate: Rate, duration: duration);
        var fill = new SolidColorBrush(s_palette[0]);
        var element = new Element { Length = duration };
        element.AddObject(new RectShape
        {
            Width = { CurrentValue = 320 },
            Height = { CurrentValue = 240 },
            Fill = { CurrentValue = fill },
        });
        scene.Children.Add(element);

        // Let the preview render queued by the edit above finish first.
        RenderThreadDrain.Drain(TimeSpan.FromMinutes(2));
        return (editor, fill);
    }
}
