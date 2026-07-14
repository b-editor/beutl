using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the input-materialization exception-path op leak (feature 004, C7): when a
/// geometry, compute, or split pass materializes its input via <c>MaterializeInput</c> and the input operation's own
/// <c>Render</c> throws inside the bake, that render exception remains primary even if both the freshly acquired
/// materialization target and the detached input operation fault while disposing. Every pooled lease must still be
/// released. Before the fix, cleanup faults replaced the render failure and could strand the detached resources.
/// </summary>
[NonParallelizable]
[TestFixture]
public class MaterializeInputThrowLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void Geometry_InputMaterializationThrows_PreservesPrimaryFailureAndReleasesPooledLeases()
    {
        RunLeakProbe(builder => builder.Geometry(_ => { }));
    }

    [Test]
    public void Compute_InputMaterializationThrows_PreservesPrimaryFailureAndReleasesPooledLeases()
    {
        RunLeakProbe(builder => builder.Compute(ComputeNodeDescriptor.Create(
            dispatch: _ => { },
            passCount: 1,
            fallback: ComputeFallback.CpuCallback,
            cpuCallback: _ => { })));
    }

    [Test]
    public void Split_InputMaterializationThrows_PreservesPrimaryFailureAndReleasesPooledLeases()
    {
        RunLeakProbe(builder => builder.Split(SplitNodeDescriptor.Static(
            render: emitter => emitter.Emit(s_bounds, _ => { }),
            branchCount: 1)));
    }

    private static void RunLeakProbe(Action<EffectGraphBuilder> appendPass)
    {
        using var pool = new RenderTargetPool();

        var primary = new InvalidOperationException("simulated render failure during input materialization");
        var cleanup = new InvalidOperationException("simulated materialization cleanup failure");
        pool.SetDisposeBackingForTest(pooled =>
        {
            pooled.DisposeBacking();
            throw cleanup;
        });

        RenderTarget target = pool.Acquire(32, 32)
            ?? throw new InvalidOperationException("RenderTargetPool.Acquire returned null (raster surface unavailable).");
        Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "sanity: the input operation holds one pooled lease");

        bool inputDisposed = false;
        // Disposing the pool while both leases are live makes each later return destroy its backing through the
        // throwing test hook. The render failure must survive both cleanup attempts.
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            render: _ =>
            {
                pool.Dispose();
                throw primary;
            },
            hitTest: s_bounds.Contains,
            onDispose: () =>
            {
                try
                {
                    target.Dispose();
                }
                finally
                {
                    inputDisposed = true;
                }
            },
            effectiveScale: EffectiveScale.At(1f));

        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        appendPass(builder);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary), "cleanup faults must not replace the source-render failure");
            Assert.That(inputDisposed, Is.True, "the detached input operation must still be consumed");
            Assert.That(pool.LiveLeaseCount, Is.Zero,
                "the materialization target and detached input must both return their pooled leases");
        });
    }
}
