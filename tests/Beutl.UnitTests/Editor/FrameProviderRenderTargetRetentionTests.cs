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
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Editor;

[NonParallelizable]
public sealed class FrameProviderRenderTargetRetentionTests
{
    [Test]
    public void Checkpoint_ReleasesEveryThirtyRenderedFrames()
    {
        var checkpoint = new RetainedRenderTargetCheckpoint();
        var releases = new List<int>();

        for (int frame = 1; frame <= 75; frame++)
        {
            if (checkpoint.Advance())
                releases.Add(frame);
        }

        Assert.That(releases, Is.EqualTo(new[] { 30, 60 }));
    }

    [Test]
    public async Task SequentialExportFrames_PeriodicallyReleaseRetainedIntermediateTargets()
    {
        VulkanTestEnvironment.EnsureAvailable();
        RetentionRun disabled = await RunSequentialExport(int.MaxValue);
        RetentionRun enabled = await RunSequentialExport(RetainedRenderTargetCheckpoint.DefaultReleaseInterval);

        Assert.Multiple(() =>
        {
            Assert.That(disabled.FrameCount, Is.EqualTo(150));
            Assert.That(enabled.FrameCount, Is.EqualTo(disabled.FrameCount));
            Assert.That(disabled.PeakRetainedBytes, Is.GreaterThan(0));
            Assert.That(disabled.FinalRetainedBytes, Is.GreaterThan(0));
            Assert.That(enabled.PeakRetainedBytes, Is.LessThan(disabled.PeakRetainedBytes / 2),
                "Periodic export checkpoints must materially reduce peak retention under the same workload.");
            Assert.That(enabled.FinalRetainedBytes, Is.Zero,
                "The final periodic export checkpoint must release every idle intermediate target.");
        });
    }

    [Test]
    public void ReleaseRetainedRenderTargets_DoesNotChangeCurrentFramePixels()
    {
        VulkanTestEnvironment.EnsureAvailable();
        RenderThread.Dispatcher.Invoke(() =>
        {
            Scene scene = CreateAnimatedBlurScene(frameRate: 30, frameCount: 150);
            using var renderer = new SceneRenderer(scene);
            renderer.CacheOptions = RenderCacheOptions.Disabled;
            const int renderedFrameCount = 150;
            for (int frame = 0; frame < renderedFrameCount; frame++)
            {
                renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(frame / 30d)));
            }

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

    private static async Task<RetentionRun> RunSequentialExport(int releaseInterval)
    {
        const int frameRate = 30;
        const int frameCount = 150;
        Scene scene = CreateAnimatedBlurScene(frameRate, frameCount);
        using var renderer = new SceneRenderer(scene);
        renderer.CacheOptions = RenderCacheOptions.Disabled;
        using var progress = new Subject<TimeSpan>();
        using var provider = new FrameProviderImpl(
            scene,
            new Rational(frameRate, 1),
            renderer,
            progress,
            releaseInterval);
        long peakRetainedBytes = 0;

        for (long frame = 0; frame < provider.FrameCount; frame++)
        {
            using Bitmap bitmap = await provider.RenderFrame(frame);
            Assert.That(bitmap.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            peakRetainedBytes = Math.Max(
                peakRetainedBytes,
                RenderThread.Dispatcher.Invoke(() => renderer.RetainedRenderTargetBytes));
        }

        long retainedBytes = RenderThread.Dispatcher.Invoke(() => renderer.RetainedRenderTargetBytes);
        return new RetentionRun(provider.FrameCount, peakRetainedBytes, retainedBytes);
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

    private readonly record struct RetentionRun(
        long FrameCount,
        long PeakRetainedBytes,
        long FinalRetainedBytes);
}
