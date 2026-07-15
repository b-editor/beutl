using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// MapDescriptorPass detaches each op from the working set (<c>current[i] = null!</c>) before mapping it, so the
/// outer exception sweeps (the outputs sweep, Execute's current sweep) cannot reach the in-flight op. A throw
/// BEFORE <c>MapOneOperation</c> takes ownership — here a plugin-authored forward bounds lambda invoked for a
/// fan-out branch — stranded the op's pooled lease for the rest of the pool's lifetime.
/// </summary>
[NonParallelizable]
[TestFixture]
public class MapOperationThrowLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void ForwardBoundsThrowsOnAFanOutBranch_ReleasesTheDetachedOpsLease()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();

            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                s_bounds,
                canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
                hitTest: s_bounds.Contains);

            // The forward lambda must survive describe-time evaluation (Append calls it once with the union
            // bounds), so it only starts throwing once armed — i.e. at execution, per fanned-out branch.
            bool armed = false;
            var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
            builder.Split(SplitNodeDescriptor.Static(
                emitter =>
                {
                    emitter.Emit(s_bounds, session => session.Inputs[0].Draw(session.OpenCanvas(), default));
                    emitter.Emit(s_bounds, session => session.Inputs[0].Draw(session.OpenCanvas(), default));
                },
                branchCount: 2,
                structuralToken: "throwing-forward-split"));
            builder.SkiaFilter(SkiaFilterNodeDescriptor.Create(
                static inner => inner,
                BoundsContract.Create(
                    rect => armed
                        ? throw new InvalidOperationException("simulated plugin forward-bounds failure")
                        : rect,
                    static rect => rect),
                structuralToken: "throwing-forward-filter"));

            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
            armed = true;

            Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, frame, [input], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));

            Assert.That(pool.LiveLeaseCount, Is.Zero,
                "the detached branch op's pooled lease must be released when the forward bounds lambda throws");
        });
    }

    [Test]
    public void ForwardBoundsFailure_IsNotReplacedByDetachedOperationCleanupFailure()
    {
        var primary = new InvalidOperationException("simulated plugin forward-bounds failure");
        var cleanup = new InvalidOperationException("simulated operation cleanup failure");
        bool armed = false;
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.SkiaFilter(SkiaFilterNodeDescriptor.Create(
            static inner => inner,
            BoundsContract.Create(
                rect => armed ? throw primary : rect,
                static rect => rect),
            structuralToken: "throwing-forward-and-cleanup"));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        armed = true;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            new Rect(64, 0, 32, 32),
            static _ => { },
            onDispose: () => throw cleanup);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery));

        Assert.That(actual, Is.SameAs(primary),
            "cleanup of the detached operation must not replace the plugin's primary bounds failure");
    }
}
