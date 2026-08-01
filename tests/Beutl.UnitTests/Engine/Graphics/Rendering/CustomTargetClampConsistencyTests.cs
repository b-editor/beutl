using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// When CreateTarget clamps working scale for buffer budget, Open() must tag the canvas with the clamped density.
[NonParallelizable]
[TestFixture]
public class CustomTargetClampConsistencyTests
{
    private static CustomFilterEffectContext Context(float workingScale)
        => new(
            new EffectTargets(),
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: workingScale);

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
    public void CreateTarget_FractionalBounds_PreservesLegacyLocalBufferPlacement()
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

    [Test]
    public void CreateTarget_BufferBudgetExceeded_ClampsDensity_AndOpenMatchesClamp()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // 10000 * 2.0 = 20000 > 16384 limit, so density is clamped.
            var bounds = new Rect(0, 0, 10000, 1);
            CustomFilterEffectContext context = Context(workingScale: 2f);
            using EffectTarget target = context.CreateTarget(bounds);

            Assert.That(target.Scale.IsUnbounded, Is.False);
            Assert.That(target.Scale.Value, Is.LessThan(2f),
                "CreateTarget did not clamp the density for an over-budget buffer");
            float expectedFit = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, 2f);
            Assert.That(target.Scale.Value, Is.EqualTo(expectedFit).Within(1e-4));

            // Open must tag the canvas with the clamped density.
            using ImmediateCanvas canvas = context.Open(target);
            Assert.That(canvas.SurfaceDensity, Is.EqualTo(target.Scale.Value).Within(1e-4),
                "Open must tag the canvas with the clamped density (#6 desync fix), not context.WorkingScale (2.0)");
            Assert.That(canvas.SurfaceDensity, Is.LessThan(2f));
        });
    }
}
