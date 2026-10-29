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
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public class MotionBlurRenderTests
{
    [TestCase("translate", 360, 0, ShutterPosition.Center)]
    [TestCase("rotate", 360, 0, ShutterPosition.Center)]
    [TestCase("deform", 360, 0, ShutterPosition.Center)]
    [TestCase("video", 360, 0, ShutterPosition.Center)]
    [TestCase("scene", 360, 0, ShutterPosition.Center)]
    [TestCase("singular", 360, 0, ShutterPosition.Center)]
    [TestCase("effect", 360, 0, ShutterPosition.Center)]
    [TestCase("translate", 720, 0, ShutterPosition.End)]
    [TestCase("effect", 720, 35, ShutterPosition.End)]
    [TestCase("effect", 720, 100, ShutterPosition.End)]
    public void MatchesTheAverageOfIndependentlyRenderedSubframesAndArbitrarySeeks(
        string kind, float angle, float sourceMix, ShutterPosition position)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Drawable input = CreateInput(kind);
            var chain = new FilterEffectGroup();
            if (input.FilterEffect.Value is { } preceding) chain.Children.Add(preceding);
            var blur = MotionBlurTests.Blur();
            blur.ShutterAngle.Value = angle;
            blur.ShutterPosition.Value = position;
            blur.SourceMix.Value = sourceMix;
            chain.Children.Add(blur);
            input.FilterEffect.Value = chain;
            Scene scene = MotionBlurTests.SceneWith(input);
            Scene referenceScene = MotionBlurTests.SceneWith(CreateInput(kind));
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery, forceOriginalSource: true);
            using var reference = new SceneRenderer(referenceScene, RenderIntent.Delivery, forceOriginalSource: true);
            using Bitmap actual = Render(renderer, 1);
            if (Environment.GetEnvironmentVariable("BEUTL_MOTION_BLUR_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                actual.Save(Path.Combine(directory, $"motion-blur-{kind}.png"));
            }
            var expected = new float[96 * 64 * 4];
            double[] sampleTimes = position == ShutterPosition.End
                ? [0.125, 0.375, 0.625, 0.875] // Two frames of exposure ending at the current time.
                : [0.8125, 0.9375, 1.0625, 1.1875];
            foreach (double time in sampleTimes)
            {
                using Bitmap frame = Render(reference, time);
                ReadOnlySpan<Half> pixels = frame.GetPixelSpan<Half>();
                for (int i = 0; i < pixels.Length; i++) expected[i] += (float)pixels[i] * (1 - sourceMix / 100) / 4;
            }
            using (Bitmap frame = Render(reference, 1))
            {
                ReadOnlySpan<Half> pixels = frame.GetPixelSpan<Half>();
                for (int i = 0; i < pixels.Length; i++) expected[i] += (float)pixels[i] * sourceMix / 100;
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

    [TestCase(1, 0, 0.5f)]
    [TestCase(0, 0, 0.25f)]
    [TestCase(1, 35, 0.5f)]
    [TestCase(0, 50, 0.375f)]
    [TestCase(1, 100, 0.5f)]
    [TestCase(0, 100, 0.5f)]
    public void AveragingPreservesOwnerOpacityAndTransparentClipEdgeSamples(double time, float sourceMix, float expected)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RectShape first = MotionBlurTests.Rectangle();
            RectShape second = MotionBlurTests.Rectangle();
            first.Width.SetKeyFrameAnimation(null);
            second.Width.SetKeyFrameAnimation(null);
            var blur = MotionBlurTests.Blur();
            blur.SourceMix.Value = sourceMix;
            var group = new DrawableGroup { FilterEffect = { Value = blur }, Opacity = { Value = 50 } };
            using var renderer = new SceneRenderer(MotionBlurTests.SceneWith(first, second, group), RenderIntent.Delivery);
            using Bitmap image = Render(renderer, time);
            Rgba pixel = ImageMetrics.ReadPixel(image, 48, 32);
            Assert.Multiple(() =>
            {
                Assert.That(pixel.Alpha, Is.EqualTo(expected).Within(0.003));
                Assert.That(pixel.Red, Is.EqualTo(expected).Within(0.003));
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PortalInputsFollowTheirOwnClipRangeAtEverySample(bool retimed)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new DrawableGroup { FilterEffect = { Value = MotionBlurTests.Blur() } };
            Scene scene = MotionBlurTests.SceneWith(new PortalObject { Count = { Value = 1 } }, group);
            if (retimed) scene.Children[0].Objects.Add(new DrawableTimeController());
            RectShape shape = MotionBlurTests.Rectangle();
            shape.Width.SetKeyFrameAnimation(null);
            var input = new Element
            {
                Start = { Value = MediaTime.FromSeconds(0.5) },
                Length = { Value = MediaTime.FromSeconds(0.5) },
                ZIndex = { Value = 1 },
            };
            input.Objects.Add(shape);
            scene.Children.Add(input);
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            foreach (double time in new[] { 0.75, 0.95, 0.55, 0.75, 0.95 })
            {
                using Bitmap image = Render(renderer, time);
                Assert.That(ImageMetrics.ReadPixel(image, 48, 32).Alpha,
                    Is.EqualTo(time == 0.75 ? 1 : 0.5).Within(0.003), $"sampled clip coverage at {time}");
            }
        });
    }

    [TestCase(false, false, 100, BlendMode.SrcOver)]
    [TestCase(true, false, 100, BlendMode.SrcOver)]
    [TestCase(false, true, 100, BlendMode.SrcOver)]
    [TestCase(true, true, 100, BlendMode.SrcOver)]
    [TestCase(false, false, 50, BlendMode.SrcOver)]
    [TestCase(true, false, 50, BlendMode.SrcOver)]
    [TestCase(false, true, 50, BlendMode.SrcOver)]
    [TestCase(true, true, 50, BlendMode.SrcOver)]
    [TestCase(true, false, 0, BlendMode.SrcOver)]
    [TestCase(true, false, 50, BlendMode.DstIn)]
    [TestCase(true, false, 50, BlendMode.Multiply)]
    public void StaticBackdropDoesNotFeedPreviousSamplesIntoTheAverage(bool clear, bool inGroup, float opacity, BlendMode blendMode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var background = new RectShape
            {
                Width = { Value = 96 },
                Height = { Value = 64 },
                Fill = { Value = new SolidColorBrush(Colors.Red) },
            };
            var blur = MotionBlurTests.Blur();
            blur.Samples.Value = 1;
            var chain = new FilterEffectGroup();
            chain.Children.Add(new Invert());
            if (!inGroup) chain.Children.Add(blur);
            var backdrop = new SourceBackdrop
            {
                Clear = { Value = clear },
                Opacity = { Value = opacity },
                BlendMode = { Value = blendMode },
                Transform = { Value = new TranslateTransform { X = { Value = 16 } } },
                FilterEffect = { Value = chain },
            };
            Scene scene = inGroup
                ? MotionBlurTests.SceneWith(background, backdrop, new DrawableGroup { FilterEffect = { Value = blur } })
                : MotionBlurTests.SceneWith(background, backdrop);
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            using Bitmap expected = Render(renderer, 1);
            foreach ((int count, float mix) in new[] { (2, 0f), (4, 0f), (8, 0f), (4, 35f), (4, 100f) })
            {
                blur.Samples.Value = count;
                blur.SourceMix.Value = mix;
                using Bitmap actual = Render(renderer, 1);
                // Include the empty strip left by the transform: Clear must still replace those pixels.
                RgbaMaximumError error = ImageMetrics.MaximumAbsoluteErrorPerChannel(expected, actual);
                Assert.That(error.Maximum, Is.LessThan(0.003), $"backdrop with {count} samples and {mix}% source: {error}");
            }
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
                RectShape shape = MotionBlurTests.Rectangle();
                shape.Width.SetKeyFrameAnimation(null);
                MotionBlurTests.Animate(shape.Opacity, 0, 100);
                var chain = new FilterEffectGroup();
                var threshold = new Threshold { Value = { Value = 55 } };
                chain.Children.Add(blurFirst ? MotionBlurTests.Blur() : threshold);
                chain.Children.Add(blurFirst ? threshold : MotionBlurTests.Blur());
                shape.FilterEffect.Value = chain;
                using var renderer = new SceneRenderer(MotionBlurTests.SceneWith(shape), RenderIntent.Delivery);
                using Bitmap image = Render(renderer, 1);
                return ImageMetrics.ReadPixel(image, 48, 32).Alpha;
            }

            Assert.That(RenderChain(true), Is.EqualTo(0).Within(0.003));
            // Before the blur, the opaque white input passes the threshold and its sampled opacity remains.
            Assert.That(RenderChain(false), Is.EqualTo(0.5).Within(0.003));
        });
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
            return new SceneDrawable { ReferencedScene = { Value = MotionBlurTests.SceneWith(CreateInput("rotate")) } };
        RectShape shape = MotionBlurTests.Rectangle();
        if (kind != "deform") shape.Width.SetKeyFrameAnimation(null);
        if (kind == "translate")
        {
            var transform = new TranslateTransform();
            MotionBlurTests.Animate(transform.X, -40, 40);
            shape.Transform.Value = transform;
        }
        if (kind == "rotate")
        {
            shape.Width.Value = 40;
            var transform = new RotationTransform();
            MotionBlurTests.Animate(transform.Rotation, 0, 180);
            shape.Transform.Value = transform;
        }
        if (kind == "singular")
        {
            shape.Width.Value = 80;
            var transform = new ScaleTransform();
            MotionBlurTests.Animate(transform.ScaleX, -100, 100);
            shape.Transform.Value = transform;
        }
        if (kind == "effect")
        {
            var transform = new TranslateTransform();
            MotionBlurTests.Animate(transform.X, -40, 40);
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
