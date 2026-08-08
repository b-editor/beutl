using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[NonParallelizable]
public sealed class FrameProviderRenderTargetRetentionTests
{
    [Test]
    public async Task SequentialExportFrames_PeriodicallyReleaseRetainedIntermediateTargets()
    {
        const int frameRate = 30;
        const int checkpointFrameCount = 30;
        const int checkpointCount = 5;
        long peakRetainedBytes = 0;
        var checkpoints = new List<RetentionCheckpoint>();
        Scene scene = CreateAnimatedBlurScene(frameRate, checkpointFrameCount * checkpointCount);
        using var renderer = new SceneRenderer(scene);
        renderer.CacheOptions = RenderCacheOptions.Disabled;
        using var progress = new Subject<TimeSpan>();
        using var provider = new FrameProviderImpl(
            scene,
            new Rational(frameRate, 1),
            renderer,
            progress,
            (frameCount, releasedBytes) => checkpoints.Add(new RetentionCheckpoint(
                frameCount,
                releasedBytes,
                renderer.RetainedRenderTargetBytes)));

        for (long frame = 0; frame < provider.FrameCount; frame++)
        {
            using Bitmap bitmap = await provider.RenderFrame(frame);
            Assert.That(bitmap.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            peakRetainedBytes = Math.Max(
                peakRetainedBytes,
                RenderThread.Dispatcher.Invoke(() => renderer.RetainedRenderTargetBytes));
        }

        Assert.Multiple(() =>
        {
            Assert.That(provider.FrameCount, Is.EqualTo(checkpointFrameCount * checkpointCount));
            Assert.That(peakRetainedBytes, Is.LessThan(1_000_000),
                "Periodic export checkpoints must bound growth instead of merely releasing at shutdown.");
            Assert.That(checkpoints, Has.Count.EqualTo(checkpointCount));
            Assert.That(
                checkpoints.Select(static item => item.FrameCount),
                Is.EqualTo(new[] { 30, 60, 90, 120, 150 }),
                "One continuous export must release retained targets at each 30-frame interval.");
            Assert.That(
                checkpoints.Select(static item => item.ReleasedBytes),
                Has.All.GreaterThan(0),
                "Every checkpoint must release targets accumulated since the preceding interval.");
            Assert.That(
                checkpoints.Select(static item => item.RetainedBytes),
                Has.All.EqualTo(0),
                "Every 30-frame export checkpoint must release every idle intermediate target.");
        });
    }

    [Test]
    public void ReleaseRetainedRenderTargets_DoesNotChangeCurrentFramePixels()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            Scene scene = CreateAnimatedBlurScene(frameRate: 30, frameCount: 150);
            using var renderer = new SceneRenderer(scene);
            renderer.CacheOptions = RenderCacheOptions.Disabled;
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(1)));
            using Bitmap before = renderer.Snapshot();

            long released = renderer.ReleaseRetainedRenderTargets();
            using Bitmap after = renderer.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(released, Is.GreaterThan(0));
                Assert.That(after.GetPixelSpan().ToArray(), Is.EqualTo(before.GetPixelSpan().ToArray()));
            });
        });
    }

    private static Scene CreateAnimatedBlurScene(int frameRate, int frameCount)
    {
        TimeSpan duration = TimeSpan.FromSeconds((double)frameCount / frameRate);
        var width = new KeyFrameAnimation<float>();
        width.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 24,
            Easing = new LinearEasing(),
        });
        width.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = duration,
            Value = 174,
            Easing = new LinearEasing(),
        });

        var shape = new RectShape
        {
            Height = { CurrentValue = 32 },
            Fill = { CurrentValue = Brushes.White },
            FilterEffect =
            {
                CurrentValue = new Blur
                {
                    Sigma = { CurrentValue = new Size(4, 4) },
                },
            },
        };
        shape.Width.Animation = width;
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = duration,
            IsEnabled = true,
        };
        element.AddObject(shape);
        string root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "frame-provider-retention-" + Guid.NewGuid().ToString("N"));
        var scene = new Scene(240, 120, "Retention")
        {
            Duration = duration,
            Uri = new Uri(Path.Combine(root, "retention.scene")),
        };
        element.Uri = new Uri(Path.Combine(root, "retention.belm"));
        scene.Children.Add(element);
        return scene;
    }

    private readonly record struct RetentionCheckpoint(
        int FrameCount,
        long ReleasedBytes,
        long RetainedBytes);
}
