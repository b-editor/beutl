using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the composite C9-fold exception-path target leak: <c>ExecuteComposite</c> acquires
/// the pooled composite target BEFORE composing the folded per-branch <see cref="SKColorFilter"/>. An effect-supplied
/// color-filter factory that throws while the compose runs stranded that target lease (the compose call sat outside the
/// try whose catch releases the target and the branch ops), and inside <c>ComposeCompositeColorFilter</c> a mid-loop
/// factory throw stranded the partially composed filter. The fix moves the compose inside the guarded region and
/// disposes the partial accumulator, so a throwing fold leaves zero live pooled leases.
/// </summary>
[NonParallelizable]
[TestFixture]
public class CompositeFoldFactoryThrowLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void CompositeFold_ColorFilterFactoryThrows_ReleasesAllPooledLeases()
    {
        using var pool = new RenderTargetPool();
        Assert.That(pool.LiveLeaseCount, Is.Zero, "sanity: the pool starts clean");

        // Split(2) -> ColorFilter(throwing) -> Composite(SrcOver): the compiler folds the color-filter run into the
        // composite's per-branch draw (C9), so the throwing factory fires from ComposeCompositeColorFilter inside
        // ExecuteComposite, after the composite target has been acquired.
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                EffectInput input = emitter.Input;
                for (int i = 0; i < 2; i++)
                {
                    emitter.Emit(input.Bounds, session =>
                    {
                        ImmediateCanvas canvas = session.OpenCanvas();
                        using (canvas.PushDeviceSpace())
                            input.Draw(canvas, default);
                    });
                }
            },
            branchCount: 2,
            structuralToken: "composite-fold-throw-split"));
        builder.ColorFilter(ColorFilterNodeDescriptor.Create(
            () => throw new InvalidOperationException("simulated folded color-filter factory failure"),
            structuralToken: "composite-fold-throw-filter"));
        builder.Composite(CompositeNodeDescriptor.Create(BlendMode.SrcOver, structuralToken: "composite-fold-throw"));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, frame, [Input()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));

        Assert.That(pool.LiveLeaseCount, Is.Zero,
            "a throwing folded color-filter factory must still release the composite target and the branch ops");
    }

    [Test]
    public void CompositeFold_FilterCleanupThrows_ReleasesTargetAndBranchOperations()
    {
        using var pool = new RenderTargetPool();
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                for (int i = 0; i < 2; i++)
                {
                    emitter.Emit(emitter.Input.Bounds, session =>
                        session.Inputs[0].Draw(session.OpenCanvas(), default));
                }
            },
            branchCount: 2,
            structuralToken: "composite-filter-cleanup-failure-split"));
        builder.ColorFilter(ColorFilterNodeDescriptor.Create(
            SKColorFilter.CreateLumaColor,
            structuralToken: "composite-filter-cleanup-failure-filter"));
        builder.Composite(CompositeNodeDescriptor.Create(
            BlendMode.SrcOver,
            structuralToken: "composite-filter-cleanup-failure"));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        var injected = new InvalidOperationException("composite filter cleanup failed");

        IDisposable compositeFilterHook = PlanExecutor.UseTestHooks(hooks => hooks.CompositeFilterDisposeFailure = injected);
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, frame, [Input()], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(injected));
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "a filter cleanup failure must release the composite target and every branch operation");
            });
        }
        finally
        {
            compositeFilterHook.Dispose();
        }
    }

    [Test]
    public void CompositeFold_StageCleanupThrows_ReleasesFreshComposedFilterAndAllLeases()
    {
        using var pool = new RenderTargetPool();
        var builder = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                for (int i = 0; i < 2; i++)
                {
                    emitter.Emit(emitter.Input.Bounds, session =>
                        session.Inputs[0].Draw(session.OpenCanvas(), default));
                }
            },
            branchCount: 2,
            structuralToken: "composite-stage-cleanup-split"));
        builder.ColorFilter(ColorFilterNodeDescriptor.Create(
            SKColorFilter.CreateLumaColor,
            structuralToken: "composite-stage-cleanup-first"));
        builder.ColorFilter(ColorFilterNodeDescriptor.Create(
            SKColorFilter.CreateLumaColor,
            structuralToken: "composite-stage-cleanup-second"));
        builder.Composite(CompositeNodeDescriptor.Create(
            BlendMode.SrcOver,
            structuralToken: "composite-stage-cleanup"));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, 1f);
        var injected = new InvalidOperationException("composite folded-stage cleanup failed");
        SKColorFilter? freshComposed = null;

        using IDisposable hook = PlanExecutor.UseTestHooks(hooks =>
        {
            hooks.CompositeFoldCreated = filter => freshComposed = filter;
            hooks.CompositeFoldStageDisposeFailure = injected;
        });
        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, frame, [Input()], 1f, 1f, float.PositiveInfinity,
            diagnostics: null, pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(injected));
            Assert.That(freshComposed, Is.Not.Null);
            Assert.That(freshComposed!.Handle, Is.EqualTo(IntPtr.Zero),
                "the newly composed filter must be released if retiring an earlier stage fails");
            Assert.That(pool.LiveLeaseCount, Is.Zero);
        });
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void CompositeAllocationMiss_PreservesFirstBranchCleanupFailureAndSweepsRemaining(
        RenderIntent renderIntent)
    {
        using var pool = new RenderTargetPool();
        pool.SetBackingFactoryFailingAfterForTest(successfulAcquires: 0);
        var builder = new EffectGraphBuilder(s_bounds, 1f, 1f, renderIntent);
        builder.Composite(CompositeNodeDescriptor.Create(
            BlendMode.SrcOver,
            structuralToken: "composite-allocation-cleanup"));
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, 1f);
        var injected = new InvalidOperationException("composite allocation branch cleanup failed");
        bool secondDisposed = false;
        RenderNodeOperation first = RenderNodeOperation.CreateLambda(
            s_bounds,
            _ => { },
            onDispose: () => throw injected);
        RenderNodeOperation second = RenderNodeOperation.CreateLambda(
            s_bounds,
            _ => { },
            onDispose: () => secondDisposed = true);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, frame, [first, second], 1f, 1f, float.PositiveInfinity,
            diagnostics: null, pool, renderIntent: renderIntent));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(injected));
            Assert.That(secondDisposed, Is.True,
                "an earlier cleanup failure must not abandon later composite branches");
            Assert.That(pool.LiveLeaseCount, Is.Zero);
        });
    }

    [Test]
    public void Composite_BranchCleanupThrows_SweepsRemainingBranchesAndReleasesTarget()
    {
        using var pool = new RenderTargetPool();
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Composite(CompositeNodeDescriptor.Create(
            BlendMode.SrcOver,
            structuralToken: "composite-branch-cleanup-failure"));
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        var injected = new InvalidOperationException("composite branch cleanup failed");
        bool secondDisposed = false;
        RenderNodeOperation first = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => throw injected);
        RenderNodeOperation second = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds.Deflate(4), Brushes.Resource.Red, null),
            onDispose: () => secondDisposed = true);
        RenderNodeOperation[] unexpectedOutputs = [];

        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
                unexpectedOutputs = PlanExecutor.Execute(
                    plan, frame, [first, second], outputScale: 1f, workingScale: 1f,
                    maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(injected));
                Assert.That(secondDisposed, Is.True, "one branch failure must not abort the remaining cleanup sweep");
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "a branch cleanup failure must release the completed composite target");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(unexpectedOutputs);
        }
    }

    private static RenderNodeOperation Input()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds.Deflate(4), Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
    }
}
