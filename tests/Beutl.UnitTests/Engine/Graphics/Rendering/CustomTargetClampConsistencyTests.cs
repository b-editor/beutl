using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// feature 003 (#6, FR-037(b)): when CreateTarget clamps the working scale down to keep an inflated buffer
// allocatable, the target reports the CLAMPED density and Open() tags its canvas with the same density, so a
// consumer deriving its working scale from the target stays in sync with the buffer (no draw at the un-clamped
// density into a smaller buffer). Unclamped, the target's density equals the context's WorkingScale.
[NonParallelizable]
[TestFixture]
public class CustomTargetClampConsistencyTests
{
    private static CustomFilterEffectContext Context(float workingScale)
        => new(new EffectTargets(), outputScale: 1f, workingScale: workingScale);

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
    public void CreateTarget_BufferBudgetExceeded_ClampsDensity_AndOpenMatchesClamp()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // 10000 logical px × w 2.0 = 20000 device px > the 16384 per-axis limit, so CreateTarget reduces
            // the density to fit (≈ 2 × 16384 / 20000 = 1.6384). The narrow (height 1) second axis keeps the
            // surface tiny (~16384 × 2 px) so a constrained CI GPU need not allocate a multi-hundred-MiB buffer.
            var bounds = new Rect(0, 0, 10000, 1);
            CustomFilterEffectContext context = Context(workingScale: 2f);
            using EffectTarget target = context.CreateTarget(bounds);

            Assert.That(target.Scale.IsUnbounded, Is.False);
            Assert.That(target.Scale.Value, Is.LessThan(2f),
                "CreateTarget did not clamp the density for an over-budget buffer");
            float expectedFit = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, 2f);
            Assert.That(target.Scale.Value, Is.EqualTo(expectedFit).Within(1e-4));

            // The fix: Open tags the canvas with the CLAMPED density, so a consumer pushing
            // CreateScale(target.Scale.Value) draws into the buffer at its real density.
            using ImmediateCanvas canvas = context.Open(target);
            Assert.That(canvas.SurfaceDensity, Is.EqualTo(target.Scale.Value).Within(1e-4),
                "Open must tag the canvas with the clamped density (#6 desync fix), not context.WorkingScale (2.0)");
            Assert.That(canvas.SurfaceDensity, Is.LessThan(2f));
        });
    }
}
