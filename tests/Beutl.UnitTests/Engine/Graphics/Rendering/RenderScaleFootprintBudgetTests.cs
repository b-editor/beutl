using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Every clamp must bound the footprint PixelRect.FromRect actually allocates, not a logical-extent estimate.
[TestFixture]
public class RenderScaleFootprintBudgetTests
{
    private static readonly float[] s_origins =
        [-40000.5f, -16384.5f, -1.5f, -0.75f, -0.5f, 0f, 0.1f, 0.5f, 0.75f, 1.5f, 1000.3f];

    private static readonly float[] s_extents =
        [0f, 0.25f, 1f, 16383.5f, 16384f, 16384.5f, 20000.7f, 40000f, 100000f];

    private static readonly float[] s_scales = [0.5f, 1f, 1.7f, 4f, 8f];

    [Test]
    public void ExactBufferBudget_DegenerateAxisWithFractionalOrigin_KeepsFootprintWithinBudget()
    {
        var bounds = new Rect(0.5f, 0.5f, 0f, BufferDimensionBudget.EngineCeiling.MaxDimension);

        float clamped = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(bounds, 1f);

        Assert.That(
            PixelRect.FromRect(bounds, 1f).Height,
            Is.GreaterThan(BufferDimensionBudget.EngineCeiling.MaxDimension),
            "the fixture must actually overflow at the requested scale");
        Assert.That(
            PixelRect.FromRect(bounds, clamped).Height,
            Is.LessThanOrEqualTo(BufferDimensionBudget.EngineCeiling.MaxDimension));
        Assert.That(clamped, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
    }

    [Test]
    public void RasterApronBudget_DegenerateAxisWithFractionalOrigin_KeepsAproneFootprintWithinBudget()
    {
        var bounds = new Rect(0.5f, 0.5f, 0f, BufferDimensionBudget.EngineCeiling.MaxDimension - 2f);

        float clamped = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(bounds, 1f);

        Assert.That(
            RenderScaleUtilities.AddRasterApron(PixelRect.FromRect(bounds, 1f)).Height,
            Is.GreaterThan(BufferDimensionBudget.EngineCeiling.MaxDimension),
            "the fixture must actually overflow at the requested scale");
        Assert.That(
            RenderScaleUtilities.AddRasterApron(PixelRect.FromRect(bounds, clamped)).Height,
            Is.LessThanOrEqualTo(BufferDimensionBudget.EngineCeiling.MaxDimension));
        Assert.That(clamped, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
    }

    [Test]
    public void BufferBudget_FractionalOrigin_KeepsFootprintWithinBudget()
    {
        var bounds = new Rect(0.5f, 0f, BufferDimensionBudget.EngineCeiling.MaxDimension - 0.4f, 1f);

        float clamped = BufferDimensionBudget.EngineCeiling.ClampWorkingScale(bounds, 1f);

        Assert.That(
            PixelRect.FromRect(bounds, 1f).Width,
            Is.GreaterThan(BufferDimensionBudget.EngineCeiling.MaxDimension),
            "the fixture must actually overflow at the requested scale");
        Assert.That(
            PixelRect.FromRect(bounds, clamped).Width,
            Is.LessThanOrEqualTo(BufferDimensionBudget.EngineCeiling.MaxDimension));
        Assert.That(clamped, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
    }

    [Test]
    public void BufferBudget_NonPositiveMaxDimension_IsRejected()
    {
        // Refused where the budget is named, so no clamp can be handed a dimension nothing fits.
        Assert.That(() => BufferDimensionBudget.Named(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => BufferDimensionBudget.Named(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => default(BufferDimensionBudget).ClampWorkingScale(new Rect(0, 0, 100, 100), 2),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void AllClamps_SweepOfOriginsExtentsAndScales_NeverExceedTheBudget()
    {
        foreach (Rect bounds in EnumerateBounds())
        {
            foreach (float requested in s_scales)
            {
                AssertWithinBudget(
                    "coarse",
                    bounds,
                    requested,
                    BufferDimensionBudget.EngineCeiling.ClampWorkingScale(bounds, requested),
                    apronPixels: 0);
                AssertWithinBudget(
                    "exact",
                    bounds,
                    requested,
                    BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(bounds, requested),
                    apronPixels: 0);
                AssertWithinBudget(
                    "apron",
                    bounds,
                    requested,
                    BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(bounds, requested),
                    apronPixels: 2);
            }
        }

        static void AssertWithinBudget(
            string clamp, Rect bounds, float requested, float clamped, int apronPixels)
        {
            string context = $"{clamp}: bounds={bounds}, requested={requested}, clamped={clamped}";
            Assert.That(clamped, Is.GreaterThan(0f), context);
            Assert.That(clamped, Is.LessThanOrEqualTo(requested), context);

            PixelRect footprint = PixelRect.FromRect(bounds, clamped);
            Assert.That(
                footprint.Width + apronPixels,
                Is.LessThanOrEqualTo(BufferDimensionBudget.EngineCeiling.MaxDimension),
                context);
            Assert.That(
                footprint.Height + apronPixels,
                Is.LessThanOrEqualTo(BufferDimensionBudget.EngineCeiling.MaxDimension),
                context);
        }
    }

    private static IEnumerable<Rect> EnumerateBounds()
    {
        foreach (float x in s_origins)
        {
            foreach (float width in s_extents)
            {
                foreach (float height in s_extents)
                {
                    yield return new Rect(x, -x, width, height);
                }
            }
        }
    }
}
