using System.Reactive.Subjects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Editor;

[NonParallelizable]
public sealed class ExportFrameCountTests
{
    private static readonly int[] s_projectRates = [24, 25, 30, 50, 60];

    // Dragging the scene end snaps it with RoundToRate
    [Test]
    public void Counts_every_frame_of_a_snapped_scene_length([ValueSource(nameof(s_projectRates))] int rate)
    {
        var mismatches = new List<string>();
        for (int frames = 1; frames <= 3000; frames++)
        {
            TimeSpan duration = TimeSpan.FromSeconds((frames + 0.25) / rate).RoundToRate(rate);
            long count = FrameProviderImpl.ToFrameCount(duration, new Rational(rate));
            if (count != frames)
                mismatches.Add($"{frames} frames ({duration.Ticks} ticks) counted as {count}");
        }

        Assert.That(mismatches, Is.Empty);
    }

    // "Set end time to the current frame" ends the scene one frame after a snapped frame,
    // which is what TimelineTabViewModel.SetSceneEndAfterFrame and SceneTimeRangeService.SetEnd do
    [Test]
    public void Counts_every_frame_when_the_end_is_set_after_a_frame(
        [ValueSource(nameof(s_projectRates))] int rate,
        [Values(0, 37)] int startFrame)
    {
        TimeSpan start = TimeSpan.FromSeconds((double)startFrame / rate).RoundToRate(rate);
        var mismatches = new List<string>();
        for (int frames = 1; frames <= 3000; frames++)
        {
            TimeSpan lastFrame = TimeSpan.FromSeconds((double)(startFrame + frames - 1) / rate).RoundToRate(rate);
            TimeSpan end = lastFrame + TimeSpan.FromSeconds(1d / rate);
            TimeSpan duration = end - start;
            long count = FrameProviderImpl.ToFrameCount(duration, new Rational(rate));
            if (count != frames)
                mismatches.Add($"{frames} frames ({duration.Ticks} ticks) counted as {count}");
        }

        Assert.That(mismatches, Is.Empty);
    }

    // FrameProviderImpl places frame n at n * denominator / numerator seconds, truncated to ticks
    [TestCase(30000, 1001)]
    [TestCase(24000, 1001)]
    [TestCase(60000, 1001)]
    public void Counts_every_frame_at_a_fractional_rate(long numerator, long denominator)
    {
        var rate = new Rational(numerator, denominator);
        var mismatches = new List<string>();
        for (long frames = 1; frames <= 3000; frames++)
        {
            var duration = TimeSpan.FromTicks(frames * denominator * TimeSpan.TicksPerSecond / numerator);
            long count = FrameProviderImpl.ToFrameCount(duration, rate);
            if (count != frames)
                mismatches.Add($"{frames} frames ({duration.Ticks} ticks) counted as {count}");
        }

        Assert.That(mismatches, Is.Empty);
    }

    [Test]
    public void Counts_no_frames_for_an_empty_duration_or_an_unusable_rate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FrameProviderImpl.ToFrameCount(TimeSpan.Zero, new Rational(30)), Is.Zero);
            Assert.That(FrameProviderImpl.ToFrameCount(TimeSpan.FromSeconds(-1), new Rational(30)), Is.Zero);
            Assert.That(FrameProviderImpl.ToFrameCount(TimeSpan.FromSeconds(1), new Rational(0, 1)), Is.Zero);
            Assert.That(FrameProviderImpl.ToFrameCount(TimeSpan.FromSeconds(1), new Rational(1, 0)), Is.Zero);
            Assert.That(FrameProviderImpl.ToFrameCount(TimeSpan.FromSeconds(1), new Rational(0, 0)), Is.Zero);
            Assert.That(FrameProviderImpl.ToFrameCount(TimeSpan.FromSeconds(1), new Rational(-30, -1)), Is.EqualTo(30));
        });
    }

    [Test]
    public async Task Providers_include_the_last_frame_and_sample_of_a_snapped_scene()
    {
        VulkanTestEnvironment.EnsureAvailable();
        const int rate = 30;
        const int frames = 10;
        const int sampleRate = 48000;
        // Ten frames at 30 fps are 3,333,333 ticks, just short of a third of a second
        var scene = new Scene(32, 32, "FrameCount")
        {
            Duration = TimeSpan.FromSeconds((double)frames / rate).RoundToRate(rate),
        };
        Assert.That(scene.Duration.Ticks, Is.EqualTo(3_333_333));

        using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
        using var frameProgress = new Subject<TimeSpan>();
        using var frameProvider = new FrameProviderImpl(scene, new Rational(rate), renderer, frameProgress);
        Assert.That(frameProvider.FrameCount, Is.EqualTo(frames));
        for (long frame = 0; frame < frameProvider.FrameCount; frame++)
        {
            using Bitmap bitmap = await frameProvider.RenderFrame(frame);
            Assert.That(bitmap.Width, Is.EqualTo(32));
        }

        using var composer = new SceneComposer(scene, disableResourceShare: true, forceOriginalSource: true)
        {
            SampleRate = sampleRate,
        };
        using var sampleProgress = new Subject<TimeSpan>();
        using var sampleProvider = new SampleProviderImpl(scene, composer, sampleRate, sampleProgress);
        Assert.That(sampleProvider.SampleCount, Is.EqualTo(sampleRate * frames / rate));
    }
}
