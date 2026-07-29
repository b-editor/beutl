using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class PixelSortEffectContentTests
{
    private static readonly PixelSize Frame = new(160, 160);

    private static Drawable.Resource MakeSortedShape()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 120;
        shape.Fill.CurrentValue = Brushes.White;
        var sort = new PixelSortEffect();
        shape.FilterEffect.CurrentValue = sort;
        return shape.ToResource(CompositionContext.Default);
    }

    // The GLSL passes sample the source target's raw texture, so the effect has to flush the Skia
    // surface first; without it every pixel falls outside the sort threshold and the frame comes back
    // empty instead of merely rearranged.
    [Test]
    public void PixelSort_KeepsTheContentItSorts()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap sorted = GoldenImageHarness.RenderAtScale(MakeSortedShape(), Frame, 1f);

            double energy = 0;
            ReadOnlySpan<ushort> pixels = sorted.GetPixelSpan<ushort>();
            for (int i = 3; i < pixels.Length; i += 4)
                energy += (float)BitConverter.UInt16BitsToHalf(pixels[i]);

            Assert.That(energy, Is.GreaterThan(1000),
                "PixelSortEffect returned an empty frame; the source surface was sampled unflushed.");
        });
    }
}
