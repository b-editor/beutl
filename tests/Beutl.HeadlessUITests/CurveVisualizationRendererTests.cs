using Avalonia.Headless.NUnit;
using Beutl.Controls.Curves;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class CurveVisualizationRendererTests
{
    // Every fourth column and row was all a frame this size used to get.
    [AvaloniaTest]
    public async Task A_thin_area_counts_with_every_one_of_its_pixels()
    {
        var renderer = new CurveVisualizationRenderer();
        using Ref<Bitmap> frame = CreateFrame(1024, 1024, (x, _) => x == 1 ? new Bgra8888(255, 0, 0, 255) : new Bgra8888(0, 0, 0, 255));

        await UpdateAsync(renderer, frame, HistogramCategory.Rgb);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.GetHistogram(CurveVisualization.Red)[255], Is.EqualTo(1024));
            Assert.That(renderer.GetHistogram(CurveVisualization.Red)[0], Is.EqualTo(1023 * 1024));
            Assert.That(renderer.GetHistogram(CurveVisualization.Green)[0], Is.EqualTo(1024 * 1024));
        });
    }

    [AvaloniaTest]
    public async Task The_other_categories_count_every_pixel_as_well()
    {
        var renderer = new CurveVisualizationRenderer();
        using Ref<Bitmap> frame = CreateFrame(1024, 1024, (x, _) => x == 1 ? new Bgra8888(255, 0, 0, 255) : new Bgra8888(0, 0, 0, 255));

        await UpdateAsync(renderer, frame, HistogramCategory.Hue | HistogramCategory.Luminance | HistogramCategory.Saturation);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.GetHistogram(CurveVisualization.HueVsHue)[0], Is.EqualTo(1024), "the red column");
            Assert.That(renderer.GetHistogram(CurveVisualization.LuminanceVsSaturation)[128], Is.EqualTo(1024));
            Assert.That(renderer.GetHistogram(CurveVisualization.SaturationVsSaturation)[255], Is.EqualTo(1024));
        });
    }

    [AvaloniaTest]
    public async Task Counting_leaves_the_caller_free_and_reports_back_on_its_thread()
    {
        var renderer = new CurveVisualizationRenderer();
        using Ref<Bitmap> frame = CreateFrame(16, 16, (_, _) => new Bgra8888(0, 0, 0, 255));
        var done = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.Updated += (_, _) => done.TrySetResult(Thread.CurrentThread);

        renderer.UpdateHistogram(frame, HistogramCategory.Rgb);

        Assert.That(done.Task.IsCompleted, Is.False, "The frame was counted before the call returned.");
        Assert.That(await done.Task.WaitAsync(TimeSpan.FromSeconds(30)), Is.SameAs(Thread.CurrentThread));
    }

    // Five frames arrive while the first is still being counted. The last one is what stays on show, and those
    // skipped on the way give their bitmaps back.
    [AvaloniaTest]
    public async Task A_frame_waiting_for_a_newer_one_is_skipped_and_released()
    {
        var renderer = new CurveVisualizationRenderer();
        Ref<Bitmap>[] frames =
        [
            .. Enumerable.Range(0, 5).Select(i => CreateFrame(16, 16, (_, _) => new Bgra8888((byte)(i * 10), 0, 0, 255)))
        ];
        int updates = 0;
        renderer.Updated += (_, _) => updates++;
        try
        {
            foreach (Ref<Bitmap> frame in frames)
                renderer.UpdateHistogram(frame, HistogramCategory.Rgb);

            for (int i = 0; i < 300 && renderer.GetHistogram(CurveVisualization.Red)[40] == 0; i++)
                await Task.Delay(100);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.GetHistogram(CurveVisualization.Red)[40], Is.EqualTo(16 * 16));
                Assert.That(updates, Is.LessThan(frames.Length));
                Assert.That(frames.Select(frame => frame.RefCount), Is.All.EqualTo(1));
            });
        }
        finally
        {
            foreach (Ref<Bitmap> frame in frames)
                frame.Dispose();
        }
    }

    private static async Task UpdateAsync(CurveVisualizationRenderer renderer, Ref<Bitmap> frame, HistogramCategory categories)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.Updated += (_, _) => done.TrySetResult();
        renderer.UpdateHistogram(frame, categories);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static Ref<Bitmap> CreateFrame(int width, int height, Func<int, int, Bgra8888> color)
    {
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            Span<Bgra8888> row = bitmap.GetRow<Bgra8888>(y);
            for (int x = 0; x < width; x++)
                row[x] = color(x, y);
        }

        return Ref<Bitmap>.Create(bitmap);
    }
}
