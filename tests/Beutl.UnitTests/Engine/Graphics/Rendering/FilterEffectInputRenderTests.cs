using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public class FilterEffectInputRenderTests
{
    [TestCase("brush")]
    [TestCase("preview")]
    [TestCase("translate")]
    [TestCase("rotate")]
    [TestCase("deform")]
    [TestCase("video")]
    [TestCase("scene")]
    [TestCase("singular")]
    [TestCase("effect")]
    public void MatchesTheAverageOfIndependentlyRenderedSubframesAndArbitrarySeeks(string kind)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Drawable input = CreateInput(kind);
            var chain = new FilterEffectGroup();
            if (input.FilterEffect.Value is { } preceding) chain.Children.Add(preceding);
            chain.Children.Add(new SampleInputEffect());
            input.FilterEffect.Value = chain;
            Drawable referenceInput = CreateInput(kind);
            if (kind == "brush")
            {
                input = BrushOwner(input);
                referenceInput = BrushOwner(referenceInput);
            }
            Scene scene = FilterEffectInputTests.SceneWith(input);
            Scene referenceScene = FilterEffectInputTests.SceneWith(referenceInput);
            using var renderer = new SceneRenderer(scene, kind == "preview" ? RenderIntent.Preview : RenderIntent.Delivery, forceOriginalSource: true);
            using var reference = new SceneRenderer(referenceScene, RenderIntent.Delivery, forceOriginalSource: true);
            using Bitmap actual = Render(renderer, 1);
            var expected = new float[96 * 64 * 4];
            foreach (double time in new[] { 0.8125, 0.9375, 1.0625, 1.1875 })
            {
                using Bitmap frame = Render(reference, time);
                ReadOnlySpan<Half> pixels = frame.GetPixelSpan<Half>();
                for (int i = 0; i < pixels.Length; i++) expected[i] += (float)pixels[i] / 4;
            }
            ReadOnlySpan<Half> output = actual.GetPixelSpan<Half>();
            double error = 0;
            for (int i = 0; i < output.Length; i++) error += Math.Abs((float)output[i] - expected[i]);
            Assert.That(expected.Max(), Is.GreaterThan(0.1), "the reference must contain visible content");
            Assert.That(error / expected.Length, Is.LessThan(0.002), "premultiplied linear subframe average");
            foreach (double time in new[] { 1.5, 0.25, 1.0 })
            {
                using Bitmap seek = Render(renderer, time);
                if (time == 1) GoldenImageHarness.AssertByteIdentical(actual, seek, kind, requireVisibleContent: true);
            }
        });
    }

    [Test]
    public void StaticOverlappingInputsStayOpaqueAndOwnerOpacityAppliesOnce()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RectShape first = FilterEffectInputTests.Rectangle();
            RectShape second = FilterEffectInputTests.Rectangle();
            first.Width.SetKeyFrameAnimation(null);
            second.Width.SetKeyFrameAnimation(null);
            var blur = new SampleInputEffect();
            var group = new DrawableGroup { FilterEffect = { Value = blur }, Opacity = { Value = 50 } };
            using var renderer = new SceneRenderer(FilterEffectInputTests.SceneWith(first, second, group), RenderIntent.Delivery);
            using Bitmap image = Render(renderer, 1);
            Rgba pixel = ImageMetrics.ReadPixel(image, 48, 32);
            Assert.Multiple(() =>
            {
                Assert.That(pixel.Alpha, Is.EqualTo(0.5).Within(0.003));
                Assert.That(pixel.Red, Is.EqualTo(0.5).Within(0.003));
            });
        });
    }

    [Test]
    public void FollowingEffectsRunOnTheAverageAndPrecedingEffectsRunOnEachSample()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            float RenderChain(bool blurFirst)
            {
                RectShape shape = FilterEffectInputTests.Rectangle();
                shape.Width.SetKeyFrameAnimation(null);
                FilterEffectInputTests.Animate(shape.Opacity, 0, 100);
                var chain = new FilterEffectGroup();
                var threshold = new Threshold { Value = { Value = 55 } };
                chain.Children.Add(blurFirst ? new SampleInputEffect() : threshold);
                chain.Children.Add(blurFirst ? threshold : new SampleInputEffect());
                shape.FilterEffect.Value = chain;
                using var renderer = new SceneRenderer(FilterEffectInputTests.SceneWith(shape), RenderIntent.Delivery);
                using Bitmap image = Render(renderer, 1);
                return ImageMetrics.ReadPixel(image, 48, 32).Alpha;
            }

            Assert.That(RenderChain(true), Is.EqualTo(0).Within(0.003));
            // Before the blur, the opaque white input passes the threshold and its sampled opacity remains.
            Assert.That(RenderChain(false), Is.EqualTo(0.5).Within(0.003));
        });
    }

    [Test]
    public void CancellationAfterRecordingReleasesTheRequestAndAllowsTheNextFrame()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var source = new CancellationTokenSource();
            using var root = new CancelRenderNode(source);
            using var shape = FilterEffectInputTests.Rectangle().ToResource(CompositionContext.Default);
            using (var graphics = new GraphicsContext2D(root, new Size(16, 16)))
            {
                graphics.DrawDrawable(shape);
            }
            using var target = RenderTarget.Create(16, 16)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery);
            using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest { Intent = RenderIntent.Delivery });
            Assert.Throws<OperationCanceledException>(() => renderer.Render(canvas, null, source.Token));
            Assert.DoesNotThrow(() => renderer.Render(canvas));
        });
    }

    [TestCase(BlendMode.Multiply)]
    [TestCase(BlendMode.DstOut)]
    public void OwnerBlendModeCompositesOnlyOnceAfterSampling(BlendMode mode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Scene MakeScene(bool sampled)
            {
                var background = new RectShape
                {
                    Width = { Value = 96 },
                    Height = { Value = 64 },
                    Fill = { Value = new SolidColorBrush(Colors.Gray) },
                };
                var shape = new RectShape
                {
                    Width = { Value = 20 },
                    Height = { Value = 12 },
                    Opacity = { Value = 50 },
                    BlendMode = { Value = mode },
                    Fill = { Value = new SolidColorBrush(Colors.Red) },
                };
                if (sampled) shape.FilterEffect.Value = new SampleInputEffect();
                return FilterEffectInputTests.SceneWith(background, shape);
            }
            using var renderer = new SceneRenderer(MakeScene(true), RenderIntent.Delivery);
            using var reference = new SceneRenderer(MakeScene(false), RenderIntent.Delivery);
            using var actual = Render(renderer, 1);
            using var expected = Render(reference, 1);
            GoldenImageHarness.AssertByteIdentical(expected, actual, mode.ToString(), requireVisibleContent: true);
        });
    }

    [Test]
    public void CancellationBetweenRenderedSamplesRejectsThePartialFrameAndAllowsRetry()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            var shape = FilterEffectInputTests.Rectangle();
            var chain = new FilterEffectGroup();
            chain.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
                context.CustomEffect(0, (_, _) => { if (++calls == 2) cancellation.Cancel(); })));
            chain.Children.Add(new SampleInputEffect());
            shape.FilterEffect.Value = chain;
            using var source = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(1)) { FrameRate = new(2, 1) });
            using var root = new ContainerRenderNode();
            using (var graphics = new GraphicsContext2D(root, new Size(96, 64))) graphics.DrawDrawable(source);
            using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest { Intent = RenderIntent.Delivery });
            using var target = RenderTarget.Create(96, 64)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery);
            Assert.Throws<OperationCanceledException>(() => renderer.Render(canvas, null, cancellation.Token));
            Assert.That(calls, Is.EqualTo(2));
            canvas.Clear();
            Assert.DoesNotThrow(() => renderer.Render(canvas));
            Assert.That(calls, Is.EqualTo(6), "retry must execute every sample instead of reusing partial outputs");
        });
    }

    [Test]
    public async Task FrameProviderUsesItsOutputRateForExposureInsteadOfTheProjectDefault()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var shape = CreateInput("translate");
        shape.FilterEffect.Value = new SampleInputEffect();
        var scene = FilterEffectInputTests.SceneWith(shape);
        scene.Duration.Value = MediaTime.FromSeconds(1);
        using var renderer = ExportRendererFactory.Create(scene);
        using var reference = ExportRendererFactory.Create(scene);
        using var progress = new Subject<MediaTime>();
        var expected = new List<ushort[]>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            foreach (double time in new[] { 0, 0.5 })
            {
                using var image = Render(reference, time);
                expected.Add(image.GetPixelSpan<ushort>().ToArray());
            }
        });
        using var provider = new FrameProviderImpl(scene, new Rational(2, 1), renderer, progress);
        for (int i = 0; i < 2; i++)
        {
            using var image = await provider.ReadNextFrame().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(image, Is.Not.Null);
            Assert.That(image!.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(expected[i]));
        }
        Assert.That(await provider.ReadNextFrame(), Is.Null);
    }

    private static RectShape BrushOwner(Drawable input) => new()
    {
        Width = { Value = 96 },
        Height = { Value = 64 },
        Fill = { Value = new DrawableBrush(new FixedDomainDrawable { Content = { Value = input } }) },
    };

    private sealed class CancelRenderNode(CancellationTokenSource source) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            source.Cancel();
            context.PassThrough();
        }
    }

    private static Drawable CreateInput(string kind)
    {
        if (kind == "video")
        {
            TestMediaHelper.RegisterTestDecoder();
            var source = new VideoSource();
            source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(96, 64, new(30, 1), 120)));
            return new SourceVideo { Source = { Value = source } };
        }
        if (kind == "scene")
            return new SceneDrawable { ReferencedScene = { Value = FilterEffectInputTests.SceneWith(CreateInput("rotate")) } };
        RectShape shape = FilterEffectInputTests.Rectangle();
        if (kind != "deform") shape.Width.SetKeyFrameAnimation(null);
        if (kind is "translate" or "brush" or "preview")
        {
            var transform = new TranslateTransform();
            FilterEffectInputTests.Animate(transform.X, -40, 40);
            shape.Transform.Value = transform;
        }
        if (kind == "rotate")
        {
            shape.Width.Value = 40;
            var transform = new RotationTransform();
            FilterEffectInputTests.Animate(transform.Rotation, 0, 180);
            shape.Transform.Value = transform;
        }
        if (kind == "singular")
        {
            shape.Width.Value = 80;
            var transform = new ScaleTransform();
            FilterEffectInputTests.Animate(transform.ScaleX, -100, 100);
            shape.Transform.Value = transform;
        }
        if (kind == "effect")
        {
            var transform = new TranslateTransform();
            FilterEffectInputTests.Animate(transform.X, -40, 40);
            shape.FilterEffect.Value = new TransformEffect { Transform = { Value = transform } };
        }
        return shape;
    }

    private static Bitmap Render(SceneRenderer renderer, double time)
    {
        renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.FromSeconds(time), new Rational(2, 1)));
        return renderer.Snapshot();
    }
}

// A drawable brush normally fits its content bounds to the destination on each frame. Keep that layout
// fixed when comparing a time average with separately rendered frames; only the content changes here.
public sealed partial class FixedDomainDrawable : Drawable
{
    public FixedDomainDrawable() => ScanProperties<FixedDomainDrawable>();

    public IExpressionProperty<Drawable?> Content { get; } = Property.CreateExpression<Drawable?>();

    public new partial class Resource
    {
        protected override Size MeasureCore(Size availableSize) => new(96, 64);
        protected override void OnDraw(GraphicsContext2D context)
        {
            context.DrawRectangle(new Rect(0, 0, 96, 64), Brushes.Resource.Transparent, null);
            if (Content is { } child) context.DrawDrawable(child);
        }
    }
}
