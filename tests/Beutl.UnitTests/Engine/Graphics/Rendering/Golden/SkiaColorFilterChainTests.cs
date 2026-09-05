using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class SkiaColorFilterChainTests
{
    private static readonly PixelSize s_frame = new(200, 200);

    private const float Brightness = 1.5f;

    [Test]
    public void ColorFilter_FollowedByImageFilter_AppliesOnce()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap plain = Render(effect: null);
            using Bitmap matrixOnly = Render(new ColorMatrixChainEffect(followWithBlur: false));
            using Bitmap matrixThenBlur = Render(new ColorMatrixChainEffect(followWithBlur: true));

            double basis = InteriorMean(plain);
            double once = InteriorMean(matrixOnly);
            double withBlur = InteriorMean(matrixThenBlur);

            TestContext.WriteLine(
                $"interior mean: plain={basis:F5} matrix={once:F5} (x{once / basis:F4}) "
                + $"matrix+blur={withBlur:F5} (x{withBlur / basis:F4})");

            Assert.Multiple(() =>
            {
                Assert.That(once / basis, Is.EqualTo(Brightness).Within(0.01),
                    "the colour matrix alone did not scale the uniform interior by its own factor");
                Assert.That(withBlur / basis, Is.EqualTo(Brightness).Within(0.01),
                    "a Skia image filter after the colour matrix re-applied the matrix");
            });
        });
    }

    private static Bitmap Render(FilterEffect? effect)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 180;
        shape.Height.CurrentValue = 180;
        shape.Fill.CurrentValue = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
        shape.FilterEffect.CurrentValue = effect;

        using Drawable.Resource resource = shape.ToResource(CompositionContext.Default);
        return GoldenImageHarness.RenderAtScale(resource, s_frame, 1f);
    }

    // The centre of a uniform rect is far enough from every edge that the blur cannot reach it, so any
    // change there is the colour matrix, not the blur.
    private static double InteriorMean(Bitmap bitmap)
    {
        int x0 = (bitmap.Width / 2) - 20;
        int x1 = (bitmap.Width / 2) + 20;
        int y0 = (bitmap.Height / 2) - 20;
        int y1 = (bitmap.Height / 2) + 20;
        double sum = 0;
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = x0; x < x1; x++)
            {
                double alpha = (double)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
                if (alpha < 0.999) continue;
                sum += (double)BitConverter.UInt16BitsToHalf(row[x * 4]) / alpha;
                count++;
            }
        }

        Assert.That(count, Is.GreaterThan(0), "the sampled interior was not opaque");
        return sum / count;
    }

    [SuppressResourceClassGeneration]
    private sealed partial class ColorMatrixChainEffect(bool followWithBlur) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.ColorMatrix(ColorMatrix.CreateBrightness(Brightness));
            if (followWithBlur)
                context.Blur(new Size(3, 3));
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }
}
