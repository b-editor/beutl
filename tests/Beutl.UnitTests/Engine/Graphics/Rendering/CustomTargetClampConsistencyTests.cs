using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// When CreateTarget clamps working scale for buffer budget, Open() must tag the canvas with the clamped density.
[NonParallelizable]
[TestFixture]
public class CustomTargetClampConsistencyTests
{
    // Below every device the suite runs on, so the clamp fires and the buffer stays allocatable everywhere.
    private const int TestBufferDimension = 8192;

    private static CustomFilterEffectContext Context(float workingScale)
        => new(
            new EffectTargets(),
            new FilterEffectExecutionSettings(RenderIntent.Delivery, RenderRequestPurpose.Auxiliary)
            {
                OutputScale = 1f,
                WorkingScale = workingScale,
                Budget = BufferDimensionBudget.Named(TestBufferDimension),
            });

    [Test]
    public void CreateTarget_WithinBudget_KeepsWorkingScale_AndOpenMatches()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            CustomFilterEffectContext context = Context(workingScale: 2f);
            using EffectTarget target = context.CreateTarget(new Rect(0, 0, 100, 80));

            Assert.That(target.Scale.IsUnbounded, Is.False);
            Assert.That(target.Scale.Value, Is.EqualTo(2f).Within(1e-4),
                "an in-budget buffer must keep the context working scale");

            using ImmediateCanvas canvas = context.Open(target);
            Assert.That(canvas.SurfaceDensity, Is.EqualTo(target.Scale.Value).Within(1e-4),
                "Open must tag the canvas with the target's actual density");
        });
    }

    [Test]
    public void CreateTarget_FractionalBounds_PreservesEffectItemLocalBufferPlacement()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(5.5f, 3.5f, 181.75f, 101.25f);
            CustomFilterEffectContext context = Context(workingScale: 1f);
            using EffectTarget target = context.CreateTarget(bounds);
            using ImmediateCanvas canvas = context.Open(target);

            Assert.Multiple(() =>
            {
                Assert.That(target.RenderTarget!.Width, Is.EqualTo(181));
                Assert.That(target.RenderTarget.Height, Is.EqualTo(101));
                Assert.That(target.RasterBounds,
                    Is.EqualTo(new Rect(bounds.Position, new Size(181, 101))));
                Assert.That(canvas.LogicalSize, Is.EqualTo(bounds.Size));
                Assert.That(canvas.Transform.Transform(default(Point)), Is.EqualTo(default(Point)));
            });
        });
    }

    // Bounds laid on the device grid come back from effect-local space a rounding error off it. At 2x with a grid
    // offset of 0.51, pixel -127 lies at -64.01, and -64.01 + 0.51 lands just short of -63.5; the buffer still starts
    // on that pixel, where a target described by its raster is drawn.
    [Test]
    public void CreateTarget_BoundsOnTheDeviceGrid_StartOnTheirFirstPixel()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var gridOffset = new Vector(0.51f, 0);
            var deviceBounds = new PixelRect(-127, 0, 10, 4);
            Rect bounds = deviceBounds.ToRect(2).Translate(-gridOffset);
            Assert.That(
                (double)(bounds.X + gridOffset.X) * 2,
                Is.LessThan(-127d),
                "The edge comes back short of the pixel.");
            var context = new CustomFilterEffectContext(
                new EffectTargets(),
                new FilterEffectExecutionSettings(RenderIntent.Delivery, RenderRequestPurpose.Auxiliary)
                {
                    OutputScale = 1f,
                    WorkingScale = 2f,
                    Budget = BufferDimensionBudget.Named(TestBufferDimension),
                    DeviceGridOffset = gridOffset,
                });
            using EffectTarget target = context.CreateTarget(bounds);

            Assert.That(target.DeviceBounds, Is.EqualTo(deviceBounds));
        });
    }

    // The allowance for that rounding grows with the coordinate but stays under a pixel, so bounds on a pixel boundary
    // a million pixels out still start on it.
    [Test]
    public void CreateTarget_BoundsOnAPixelFarOut_StartOnIt()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            CustomFilterEffectContext context = Context(workingScale: 1f);
            using EffectTarget target = context.CreateTarget(new Rect(1_000_000, 0, 10, 4));

            Assert.That(target.DeviceBounds, Is.EqualTo(new PixelRect(1_000_000, 0, 10, 4)));
        });
    }

    [Test]
    public void CreateTarget_BufferBudgetExceeded_ClampsDensity_AndOpenMatchesClamp()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // 10000 * 2.0 = 20000 > the 8192 budget, so density is clamped.
            var bounds = new Rect(0, 0, 10000, 1);
            CustomFilterEffectContext context = Context(workingScale: 2f);
            using EffectTarget target = context.CreateTarget(bounds);

            Assert.That(target.Scale.IsUnbounded, Is.False);
            Assert.That(target.Scale.Value, Is.LessThan(2f),
                "CreateTarget did not clamp the density for an over-budget buffer");
            float expectedFit = BufferDimensionBudget.Named(TestBufferDimension).ClampWorkingScale(bounds, 2f);
            Assert.That(target.Scale.Value, Is.EqualTo(expectedFit).Within(1e-4));

            using ImmediateCanvas canvas = context.Open(target);
            Assert.That(canvas.SurfaceDensity, Is.EqualTo(target.Scale.Value).Within(1e-4),
                "Open must tag the canvas with the clamped density, not context.WorkingScale (2.0)");
            Assert.That(canvas.SurfaceDensity, Is.LessThan(2f));
        });
    }
}
