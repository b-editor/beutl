using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the input-materialization exception-path op leak (feature 004, C7): when a
/// geometry or compute pass materializes its input via <c>MaterializeInput</c> and the input operation's own
/// <c>Render</c> throws inside the bake, the input operation must still be disposed so its pooled buffer returns to
/// the pool. Before the fix <c>MapDescriptorPass</c> nulled the operation out of the working set before the
/// materialize ran, so a bake throw left it in neither disposal sweep and stranded one pooled lease.
/// </summary>
[NonParallelizable]
[TestFixture]
public class MaterializeInputThrowLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void Geometry_InputMaterializationThrows_ReleasesInputPooledLease()
    {
        RunLeakProbe(builder => builder.Geometry(_ => { }));
    }

    [Test]
    public void Compute_InputMaterializationThrows_ReleasesInputPooledLease()
    {
        RunLeakProbe(builder => builder.Compute(ComputeNodeDescriptor.Create(
            dispatch: _ => { },
            passCount: 1,
            fallback: ComputeFallback.CpuCallback,
            cpuCallback: _ => { })));
    }

    private static void RunLeakProbe(Action<EffectGraphBuilder> appendPass)
    {
        using var pool = new RenderTargetPool();

        RenderTarget target = pool.Acquire(32, 32)
            ?? throw new InvalidOperationException("RenderTargetPool.Acquire returned null (raster surface unavailable).");
        Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "sanity: the input operation holds one pooled lease");

        // An input operation whose Render throws during the bake; owns the pooled lease so its disposal returns it.
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            render: _ => throw new InvalidOperationException("simulated render failure during input materialization"),
            hitTest: s_bounds.Contains,
            onDispose: target.Dispose,
            effectiveScale: EffectiveScale.At(1f));

        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        appendPass(builder);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool));

        Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
            "a bake throw during input materialization must still dispose the input, returning its pooled lease");
    }
}
