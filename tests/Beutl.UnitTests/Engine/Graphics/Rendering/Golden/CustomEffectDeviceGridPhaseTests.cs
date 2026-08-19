using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// A custom (imperative) filter effect crops and re-lays-out its input in whole device pixels, so the
/// input has to be rasterized on a grid whose phase is zero. An effect that widens its layout box by an
/// odd half device pixel leaves the source off that grid; snapping it costs sub-pixel position, but
/// resampling it onto the grid spreads the outer edge over two pixels, and a DrawableBrush magnifying
/// the result turns that half pixel into a visibly soft fill edge.
/// </summary>
[NonParallelizable]
[TestFixture]
public class CustomEffectDeviceGridPhaseTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static SplitEffect Split(float spacing)
    {
        var split = new SplitEffect();
        split.HorizontalDivisions.CurrentValue = 2;
        split.VerticalDivisions.CurrentValue = 2;
        split.HorizontalSpacing.CurrentValue = spacing;
        split.VerticalSpacing.CurrentValue = spacing;
        return split;
    }

    private static Drawable.Resource BrushHost(FilterEffect sourceEffect)
    {
        var source = new RectShape();
        source.AlignmentX.CurrentValue = AlignmentX.Center;
        source.AlignmentY.CurrentValue = AlignmentY.Center;
        source.Width.CurrentValue = 60;
        source.Height.CurrentValue = 40;
        source.Fill.CurrentValue = Brushes.White;
        source.FilterEffect.CurrentValue = sourceEffect;

        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = source;
        brush.Stretch.CurrentValue = Stretch.Uniform;
        brush.TileMode.CurrentValue = TileMode.None;
        brush.DestinationRect.CurrentValue = RelativeRect.Fill;

        var host = new RectShape();
        host.AlignmentX.CurrentValue = AlignmentX.Center;
        host.AlignmentY.CurrentValue = AlignmentY.Center;
        host.Width.CurrentValue = 180;
        host.Height.CurrentValue = 120;
        host.Fill.CurrentValue = brush;
        return host.ToResource(CompositionContext.Default);
    }

    // 2x2 tiles with a 6px gap centre the layout box 3 logical pixels outside the 60x40 source, so at
    // 0.5x the source sits half a device pixel off the box grid.
    [Test]
    [Category("GpuPassFusionGpu")]
    public void DrawableBrushFill_SplitSourceOffTheBoxGrid_KeepsHardFillEdges()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            AssertMatchesAlignedControl(Split(6f), "offset split");
        });
    }

    // The control: an 8px gap moves the box a whole device pixel at 0.5x, so the source was already on
    // the grid and the edge must stay hard whatever the phase handling does.
    [Test]
    [Category("GpuPassFusionGpu")]
    public void DrawableBrushFill_SplitSourceOnTheBoxGrid_KeepsHardFillEdges()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() => AssertHardFillEdges(Split(8f), "aligned split"));
    }

    // The same off-grid split behind an ordinary colour stage. The colour stage is a separate render
    // fragment, so the split's input is materialized one execution frame deeper; the grid the custom
    // effect crops on has to reach that frame too.
    [Test]
    [Category("GpuPassFusionGpu")]
    public void DrawableBrushFill_SplitSourceBehindAColourStage_KeepsHardFillEdges()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            AssertMatchesAlignedControl(ColourThen(Split(6f)), "colour-chained offset split");
        });
    }

    private static FilterEffectGroup ColourThen(FilterEffect tail)
    {
        var hueRotate = new HueRotate();
        hueRotate.Angle.CurrentValue = 120f;
        var group = new FilterEffectGroup();
        group.Children.Add(hueRotate);
        group.Children.Add(tail);
        return group;
    }

    /// <summary>
    /// Measures an off-grid source against the aligned control rendered in the same run. The control
    /// cannot lose the half pixel by construction, so it carries whatever ripple the backend's own
    /// magnification kernel puts on a fully covered edge — which differs between rasterizers, where the
    /// loss under test does not: it costs 23%, against a ripple of about 1.4%.
    /// </summary>
    private static void AssertMatchesAlignedControl(FilterEffect sourceEffect, string label)
    {
        (float alignedLeft, float alignedRight) = MeasureEdges(Split(8f), "aligned control");
        (float left, float right) = MeasureEdges(sourceEffect, label);

        const float Ripple = 0.05f;
        Assert.Multiple(() =>
        {
            Assert.That(left, Is.GreaterThan(alignedLeft - Ripple),
                "the leading fill edge lost coverage: the effect's input was resampled onto the device "
                + "grid instead of being rasterized on it, and the brush magnified the loss");
            Assert.That(right, Is.GreaterThan(alignedRight - Ripple),
                "the trailing fill edge lost coverage: the effect's input was resampled onto the device "
                + "grid instead of being rasterized on it, and the brush magnified the loss");
        });
    }

    private static void AssertHardFillEdges(FilterEffect sourceEffect, string label)
    {
        (float left, float right) = MeasureEdges(sourceEffect, label);
        Assert.Multiple(() =>
        {
            Assert.That(left, Is.GreaterThan(0.95f), "the leading fill edge was not hard to begin with");
            Assert.That(right, Is.GreaterThan(0.95f), "the trailing fill edge was not hard to begin with");
        });
    }

    private static (float Left, float Right) MeasureEdges(FilterEffect sourceEffect, string label)
    {
        using Bitmap rendered = GoldenImageHarness.RenderAtScale(BrushHost(sourceEffect), Frame, 0.5f);
        (float left, float right) = EdgeCoverage(rendered);
        TestContext.WriteLine($"[{label} brush fill] edge coverage left={left:F4} right={right:F4}");
        return (left, right);
    }

    // The weakest leading and trailing edge pixel over the rows the fill covers completely. Rows the
    // content only clips into are dimmed by their own vertical coverage and say nothing about the
    // horizontal edge, so a row counts only once its interior reaches full coverage; among those, one
    // soft row is a failure, which a maximum over all rows would hide.
    private static (float Left, float Right) EdgeCoverage(Bitmap bitmap)
    {
        float leading = float.PositiveInfinity;
        float trailing = float.PositiveInfinity;
        int rows = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            int first = -1;
            int last = -1;
            float interior = 0f;
            for (int x = 0; x < bitmap.Width; x++)
            {
                float coverage = Coverage(row, x);
                if (coverage < 0.5f) continue;
                if (first < 0) first = x;
                last = x;
                interior = Math.Max(interior, coverage);
            }

            if (first < 0 || interior < 0.999f) continue;
            rows++;
            leading = Math.Min(leading, Coverage(row, first));
            trailing = Math.Min(trailing, Coverage(row, last));
        }

        Assert.That(rows, Is.GreaterThan(0), "no row was fully covered; the scene did not render");
        return (leading, trailing);
    }

    // The fill is opaque white, so in premultiplied linear RGBA the red channel is the pixel's coverage.
    private static float Coverage(ReadOnlySpan<ushort> row, int x)
        => (float)BitConverter.UInt16BitsToHalf(row[x * 4]);
}
