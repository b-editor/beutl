using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
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
    [TestCase("translate")]
    [TestCase("rotate")]
    [TestCase("deform")]
    [TestCase("video")]
    [TestCase("scene")]
    public void MatchesTheAverageOfIndependentlyRenderedSubframesAndArbitrarySeeks(string kind)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Scene scene = MotionBlurTests.SceneWith(CreateInput(kind), MotionBlurTests.Blur());
            Scene referenceScene = MotionBlurTests.SceneWith(CreateInput(kind));
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery, forceOriginalSource: true);
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
            RectShape first = MotionBlurTests.Rectangle();
            RectShape second = MotionBlurTests.Rectangle();
            first.Width.SetKeyFrameAnimation(null);
            second.Width.SetKeyFrameAnimation(null);
            var blur = MotionBlurTests.Blur();
            blur.Opacity.Value = 50;
            using var renderer = new SceneRenderer(MotionBlurTests.SceneWith(first, second, blur), RenderIntent.Delivery);
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
    public void CancellationAfterRecordingReleasesTheRequestAndAllowsTheNextFrame()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var source = new CancellationTokenSource();
            using var root = new CancelRenderNode(source);
            using var shape = MotionBlurTests.Rectangle().ToResource(CompositionContext.Default);
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

    private sealed class CancelRenderNode(CancellationTokenSource source) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            source.Cancel();
            context.PassThrough();
        }
    }

    private static EngineObject CreateInput(string kind)
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
        return shape;
    }

    private static Bitmap Render(SceneRenderer renderer, double time)
    {
        renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.FromSeconds(time), default, new(2, 1)));
        return renderer.Snapshot();
    }
}
