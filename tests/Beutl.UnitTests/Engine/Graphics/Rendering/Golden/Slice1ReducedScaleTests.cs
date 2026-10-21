using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A vector scene renders faithfully at a reduced output scale.
[NonParallelizable]
[TestFixture]
public class Slice1ReducedScaleTests
{
    private static readonly PixelSize Frame = new(250, 250);

    private static Drawable.Resource MakeScene()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 160;
        shape.Height.Value = 120;
        shape.Fill.Value = Brushes.White;
        return shape.ToResource(CompositionContext.Default);
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
