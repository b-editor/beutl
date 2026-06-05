using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// US1: a vector scene renders faithfully at a reduced output scale and byte-identically at 1.0.
[NonParallelizable]
public class Slice1ReducedScaleTests
{
    private static readonly PixelSize Frame = new(250, 250);

    private static Drawable.Resource MakeScene()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 160;
        shape.Height.CurrentValue = 120;
        shape.Fill.CurrentValue = Brushes.White;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void ScaleOne_IsDeterministic()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap a = GoldenImageHarness.RenderAtScale(MakeScene(), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(MakeScene(), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }

    [Test]
    public void RootSurface_IsCeilFrameTimesScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            foreach (float s in new[] { 1f, 0.5f, 0.25f, 0.333f, 1.5f, 2f })
            {
                using Bitmap b = GoldenImageHarness.RenderAtScale(MakeScene(), Frame, s);
                Assert.That(b.Width, Is.EqualTo((int)MathF.Ceiling(Frame.Width * s)), $"width @ {s}");
                Assert.That(b.Height, Is.EqualTo((int)MathF.Ceiling(Frame.Height * s)), $"height @ {s}");
            }
        });
    }

    [Test]
    public void HalfScale_UpscaledMatchesFull()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = GoldenImageHarness.RenderAtScale(MakeScene(), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(MakeScene(), Frame, 0.5f);
            GoldenImageHarness.AssertReducedScaleExact(full, half);
        });
    }
}
