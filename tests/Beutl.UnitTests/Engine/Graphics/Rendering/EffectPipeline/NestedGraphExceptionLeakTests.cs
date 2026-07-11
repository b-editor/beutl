using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the nested-graph exception-path op leak (feature 004): when a nested-graph
/// branch's DescribeBranch/Build/Compile/ResolveResources throws, the input operation must still be disposed so its
/// pooled buffer returns to the pool. Before the fix the executor nulled the operation out of the working set before
/// those steps ran, so a throw there left it in neither disposal sweep and stranded one pooled lease.
/// </summary>
[NonParallelizable]
[TestFixture]
public class NestedGraphExceptionLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void NestedGraph_DescribeBranchThrows_ReleasesInputPooledLease()
    {
        using var pool = new RenderTargetPool();

        RenderTarget target = pool.Acquire(32, 32)
            ?? throw new InvalidOperationException("RenderTargetPool.Acquire returned null (raster surface unavailable).");
        Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "sanity: the input operation holds one pooled lease");

        RenderNodeOperation input = RenderNodeOperation.CreateFromRenderTarget(
            s_bounds, s_bounds.Position, target, EffectiveScale.At(1f));

        var nested = NestedGraphNodeDescriptor.Create(
            (_, _) => throw new InvalidOperationException("NestedGraph: simulated describe failure"),
            structuralToken: "leak-probe");
        using EffectGraph graph = new EffectGraphBuilder(s_bounds, 1f, 1f).NestedGraph(nested).Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool));

        Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
            "a nested-graph branch whose describe throws must still dispose the input, returning its pooled lease");
    }
}
