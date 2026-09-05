using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class CurvesFiniteOutputTests
{
    private static readonly PixelSize s_frame = new(128, 96);

    [Test]
    [Category("GpuPassFusionGpu")]
    public void OutOfRangeMasterCurve_ProducesFiniteExtendedRangePixels()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var curves = new Curves();
            curves.MasterCurve.CurrentValue = new CurveMap(
            [
                new CurveControlPoint(-0.5f, -0.5f),
                new CurveControlPoint(1.5f, 1.5f),
            ]);

            var shape = new RectShape();
            shape.Width.CurrentValue = s_frame.Width;
            shape.Height.CurrentValue = s_frame.Height;
            shape.Fill.CurrentValue = new SolidColorBrush(Colors.Black);
            shape.FilterEffect.CurrentValue = curves;

            using Drawable.Resource resource = shape.ToResource(CompositionContext.Default);
            using Bitmap actual = Render(resource, outputScale: 2f);

            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("out-of-range curves", actual)),
                    Is.Null,
                    "An extended-range curve must not turn a finite input into NaN or infinity.");
                Assert.That(
                    HasVisibleCoverage(actual),
                    Is.True,
                    "The curve render must retain visible content so the finiteness assertion is meaningful.");
                Assert.That(
                    HasExtendedRangeRgb(actual),
                    Is.True,
                    "The curve render must preserve finite RGB values outside [0, 1].");
            });
        });
    }

    private static Bitmap Render(Drawable.Resource resource, float outputScale)
    {
        PixelSize pixelSize = PixelSize.FromSize(s_frame.ToSize(1), outputScale);
        using RenderTarget target = RenderTarget.Create(pixelSize.Width, pixelSize.Height)
            ?? throw new InvalidOperationException("Could not allocate the curves render target.");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, outputScale, logicalSize: s_frame.ToSize(1));
        canvas.Clear();

        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frame.ToSize(1), outputScale))
        {
            context.DrawDrawable(resource);
        }

        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, s_frame.ToSize(1)),
            OutputScale = outputScale,
            MaxWorkingScale = outputScale,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        renderer.Render(canvas);
        return target.Snapshot();
    }

    private static bool HasVisibleCoverage(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                float alpha = (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
                if (float.IsFinite(alpha) && alpha > 0.01f)
                    return true;
            }
        }

        return false;
    }

    private static bool HasExtendedRangeRgb(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int i = 0; i < row.Length; i += 4)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    float value = (float)BitConverter.UInt16BitsToHalf(row[i + channel]);
                    if (float.IsFinite(value) && (value < 0f || value > 1f))
                        return true;
                }
            }
        }

        return false;
    }
}
