using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class SteppedMotionRenderTests
{
    [Test]
    public void PreviewHoldsTheSampleUntilTheBoundaryAndWhenSeekingBack()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RectShape shape = MotionBlurTests.Rectangle();
            var move = new TranslateTransform();
            MotionBlurTests.Animate(move.X, -24, 24);
            shape.Transform.Value = move;
            var effect = new SteppedMotion { FrameRate = { Value = 2 } };
            shape.FilterEffect.Value = effect;

            using var renderer = new SceneRenderer(MotionBlurTests.SceneWith(shape), TestFonts.Manager, RenderIntent.Preview);
            using Bitmap atSampleTime = Render(0.5);
            using Bitmap held = Render(0.83);
            using Bitmap next = Render(1);
            using Bitmap soughtBack = Render(0.6);
            GoldenImageHarness.AssertByteIdentical(atSampleTime, held, "held frame", requireVisibleContent: true);
            GoldenImageHarness.AssertByteIdentical(atSampleTime, soughtBack, "backward seek", requireVisibleContent: true);
            Assert.That(ImageMetrics.MaximumAbsoluteErrorPerChannel(atSampleTime, next).Alpha, Is.GreaterThan(0.5));

            effect.IsEnabled.Value = false;
            using Bitmap reference = Render(0.5);
            GoldenImageHarness.AssertByteIdentical(reference, atSampleTime, "unquantized input at the sample time", requireVisibleContent: true);

            Bitmap Render(double time)
            {
                renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.FromSeconds(time), new Rational(24)));
                return renderer.Snapshot();
            }
        });
    }
}
