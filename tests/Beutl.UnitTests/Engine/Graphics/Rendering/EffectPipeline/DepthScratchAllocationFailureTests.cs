using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the pool-less depth-scratch C7 bypass (feature 004): when a compute pass runs without a render-target
/// pool, <c>AcquireDepthScratch</c> creates a raw depth texture. A create failure there must surface as the same
/// <see cref="PlanExecutor.ComputeScratchAllocationException"/> the pooled branch raises, so <c>ExecuteCompute</c>'s C7
/// catch normalizes it (preview drops, delivery throws) instead of aborting as an unclassified dispatch bug.
/// </summary>
[TestFixture]
public class DepthScratchAllocationFailureTests
{
    [Test]
    public void CreateNonPooledDepthScratch_RawCreateThrows_NormalizesToComputeScratchAllocationException()
    {
        var inner = new InvalidOperationException("simulated GPU out-of-memory");
        var gfx = new Mock<IGraphicsContext>(MockBehavior.Strict);
        gfx.Setup(g => g.CreateTexture2D(128, 96, TextureFormat.Depth32Float)).Throws(inner);

        PlanExecutor.ComputeScratchAllocationException ex =
            Assert.Throws<PlanExecutor.ComputeScratchAllocationException>(
                () => PlanExecutor.CreateNonPooledDepthScratch(gfx.Object, 128, 96));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Does.Contain("128x96"), "the failure message carries the requested dimensions");
            Assert.That(ex.InnerException, Is.SameAs(inner), "the raw create exception is preserved as the inner cause");
        });
    }
}
