using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphExportTests
{
    [TestCase(1f)]
    [TestCase(2f)]
    public async Task SavedAnimatedEffectsMatchRandomSeeksAndExportedFrames(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-graph-export", duration: MediaTime.FromSeconds(1));
        Scene scene = Populate(files);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        using var original = new SceneRenderer(scene, RenderIntent.Preview, scale);
        using var preview = new SceneRenderer(restored, RenderIntent.Preview, scale);
        var frames = new Dictionary<int, ushort[]>();

        foreach (int index in new[] { 2, 0, 3, 1, 2 })
        {
            MediaTime time = MediaTime.FromFrames(index, new Rational(4));
            ushort[] expected = Render(original, time, scale);
            ushort[] actual = Render(preview, time, scale);
            Assert.That(actual, Is.EqualTo(expected), $"Saved scene at frame {index}");
            if (frames.TryGetValue(index, out ushort[]? previous))
                Assert.That(actual, Is.EqualTo(previous), "Seeking backward and forward must not retain earlier frame state.");
            frames[index] = actual;
        }
        Assert.That(frames[0], Is.Not.EqualTo(frames[2]), "The saved animations must actually change the image.");
        Assert.That(frames[0], Has.Some.Not.EqualTo((ushort)0), "An empty frame is not a valid parity result.");

        using SceneRenderer export = ExportRendererFactory.Create(restored, scale);
        using var progress = new Subject<MediaTime>();
        using var provider = new FrameProviderImpl(restored, new Rational(4), export, progress);
        for (int index = 0; index < 4; index++)
        {
            using Bitmap? bitmap = await provider.ReadNextFrame().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(bitmap!.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(frames[index]), $"Exported frame {index}");
            if (index == 2)
            {
                string directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "Artifacts");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"shader-graph-effects-{scale}x.png");
                Assert.That(bitmap.Save(path), Is.True);
                TestContext.AddTestAttachment(path);
            }
        }
        Assert.That(await provider.ReadNextFrame(), Is.Null);
    }

    [Test]
    public async Task CancellationDisposesTheFrameWaitingForTheEncoder()
    {
        var channel = Channel.CreateBounded<Bitmap>(1);
        using var queued = new Bitmap(1, 1);
        using var waiting = new Bitmap(1, 1);
        using var cancellation = new CancellationTokenSource();
        await FrameProviderImpl.WriteFrameAsync(channel.Writer, queued, CancellationToken.None);
        Task pending = FrameProviderImpl.WriteFrameAsync(channel.Writer, waiting, cancellation.Token).AsTask();
        Assert.That(pending.IsCompleted, Is.False);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        Assert.That(waiting.IsDisposed, Is.True);
        Assert.That(queued.IsDisposed, Is.False, "A successful write transfers ownership to the consumer.");
        Assert.That(await channel.Reader.ReadAsync(), Is.SameAs(queued));
    }

    [Test]
    public async Task CancellingAnExportReleasesItsRendererAndTheNextExportStillWorks()
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-graph-cancel", duration: MediaTime.FromSeconds(20));
        Scene scene = Populate(files);
        using (SceneRenderer renderer = ExportRendererFactory.Create(scene))
        using (var progress = new Subject<MediaTime>())
        {
            var provider = new FrameProviderImpl(scene, new Rational(30), renderer, progress);
            using (Bitmap? first = await provider.ReadNextFrame().AsTask().WaitAsync(TimeSpan.FromSeconds(15)))
                Assert.That(first, Is.Not.Null);
            provider.Dispose();
            provider.Dispose();
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await provider.ReadNextFrame());
        }

        scene.Duration.Value = MediaTime.FromFrames(1, new Rational(30));
        using SceneRenderer next = ExportRendererFactory.Create(scene);
        using var nextProgress = new Subject<MediaTime>();
        using var nextProvider = new FrameProviderImpl(scene, new Rational(30), next, nextProgress);
        using Bitmap? frame = await nextProvider.ReadNextFrame().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.That(frame!.GetPixelSpan<ushort>().ToArray(), Has.Some.Not.EqualTo((ushort)0));
        Assert.That(await nextProvider.ReadNextFrame(), Is.Null);
    }

    private static Scene Populate(SceneHistoryHarness files)
    {
        Scene scene = files.Scene;
        scene.FrameSize.Value = new PixelSize(48, 40);
        var bloom = new Bloom { Threshold = { Value = 0.5f }, Intensity = { Value = 0.75f } };
        bloom.Radius.SetKeyFrameAnimation(Animation(2, 6));
        bloom.SoftKnee.SetKeyFrameAnimation(Animation(0, 100));
        var refraction = new WaveRefraction { Amount = { Value = 3 }, Wavelength = { Value = 16 } };
        refraction.Phase.SetKeyFrameAnimation(Animation(0, 180));
        var effects = new FilterEffectGroup { Children = { bloom, refraction } };
        var shape = new RectShape
        {
            Width = { Value = 20 },
            Height = { Value = 12 },
            Fill = { Value = new LinearGradientBrush
            {
                StartPoint = { Value = RelativePoint.TopLeft },
                EndPoint = { Value = RelativePoint.BottomRight },
                GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.Red, 1) },
            } },
            FilterEffect = { Value = effects },
        };
        files.AddElement(MediaTime.Zero, scene.Duration.Value).AddObject(shape);
        return scene;
    }

    private static KeyFrameAnimation<float> Animation(float start, float end) => new()
    {
        KeyFrames =
        {
            new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = start } },
            new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = end } },
        },
    };

    private static ushort[] Render(SceneRenderer renderer, MediaTime time, float scale)
        => VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            renderer.Render(renderer.Compositor.EvaluateGraphics(time));
            using Bitmap original = renderer.Snapshot();
            Bitmap delivered = SupersampleDownscaler.ToFrameSize(original, renderer.FrameSize, scale);
            try { return delivered.GetPixelSpan<ushort>().ToArray(); }
            finally { if (!ReferenceEquals(original, delivered)) delivered.Dispose(); }
        });
}
