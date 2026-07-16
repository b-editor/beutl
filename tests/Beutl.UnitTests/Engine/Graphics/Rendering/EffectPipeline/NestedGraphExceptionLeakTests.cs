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
    public void NestedGraph_StatefulFactoryRejectsNullCallbacks()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => NestedGraphNodeDescriptor.CreateStateful(
                describeBranch: null!,
                branchesCompleted: static _ => { }));
            Assert.Throws<ArgumentNullException>(() => NestedGraphNodeDescriptor.CreateStateful(
                describeBranch: static (_, _) => { },
                branchesCompleted: null!));
        });
    }

    [Test]
    public void NestedGraph_PublicCompletionCallbackPrunesStateForDisappearedBranches()
    {
        var branchState = new Dictionary<int, object>();
        var retiredOrdinals = new List<int>();
        IReadOnlySet<int>? lastCompleted = null;
        int completionCount = 0;
        var nested = NestedGraphNodeDescriptor.CreateStateful(
            (_, branchOrdinal) => branchState.TryAdd(branchOrdinal, new object()),
            liveBranchOrdinals =>
            {
                completionCount++;
                lastCompleted = new HashSet<int>(liveBranchOrdinals);
                foreach (int staleOrdinal in branchState.Keys
                             .Where(ordinal => !liveBranchOrdinals.Contains(ordinal))
                             .ToArray())
                {
                    branchState.Remove(staleOrdinal);
                    retiredOrdinals.Add(staleOrdinal);
                }
            },
            structuralToken: "public-completion-prune-probe");

        ExecuteNestedPull(nested, inputCount: 3);
        Assert.Multiple(() =>
        {
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(lastCompleted, Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(branchState.Keys, Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(retiredOrdinals, Is.Empty);
        });

        ExecuteNestedPull(nested, inputCount: 1);
        Assert.Multiple(() =>
        {
            Assert.That(completionCount, Is.EqualTo(2));
            Assert.That(lastCompleted, Is.EquivalentTo(new[] { 0 }));
            Assert.That(branchState.Keys, Is.EquivalentTo(new[] { 0 }),
                "state for the one remaining stable ordinal is retained");
            Assert.That(retiredOrdinals, Is.EquivalentTo(new[] { 1, 2 }),
                "the successful completion callback can retire every disappeared ordinal in the same pull");
        });
    }

    [Test]
    public void NestedGraph_DescribeBranchThrows_ReleasesInputPooledLease()
    {
        using var pool = new RenderTargetPool();
        bool branchesCompleted = false;

        RenderTarget target = pool.Acquire(32, 32)
            ?? throw new InvalidOperationException("RenderTargetPool.Acquire returned null (raster surface unavailable).");
        Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "sanity: the input operation holds one pooled lease");

        RenderNodeOperation input = RenderNodeOperation.CreateFromRenderTarget(
            s_bounds, s_bounds.Position, target, EffectiveScale.At(1f));

        var nested = NestedGraphNodeDescriptor.CreateStateful(
            (_, _) => throw new InvalidOperationException("NestedGraph: simulated describe failure"),
            _ => branchesCompleted = true,
            structuralToken: "leak-probe");
        using EffectGraph graph = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).NestedGraph(nested).Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
                "a nested-graph branch whose describe throws must still dispose the input, returning its pooled lease");
            Assert.That(branchesCompleted, Is.False,
                "the completion callback observes only a fully successful nested pull");
        });
    }

    [Test]
    public void NestedGraph_BranchCompletionThrows_PreservesFailureAndReleasesOutputPooledLease()
    {
        using var pool = new RenderTargetPool();

        RenderTarget target = pool.Acquire(32, 32)
            ?? throw new InvalidOperationException("RenderTargetPool.Acquire returned null (raster surface unavailable).");
        RenderNodeOperation input = RenderNodeOperation.CreateFromRenderTarget(
            s_bounds, s_bounds.Position, target, EffectiveScale.At(1f));
        var expected = new InvalidOperationException("NestedGraph: simulated branch-completion failure");
        var nested = NestedGraphNodeDescriptor.CreateStateful(
            static (_, _) => { },
            _ => throw expected,
            structuralToken: "completion-failure-leak-probe");
        using EffectGraph graph = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).NestedGraph(nested).Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected), "cleanup must not replace the completion callback's failure");
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
                "a completion callback failure must dispose every already-produced branch output");
        });
    }

    [Test]
    public void NestedGraph_ParameterRebindUsesCurrentBranchCompletionCallback()
    {
        var firstLiveSet = new List<int>();
        var currentLiveSet = new List<int>();
        var firstDescriptor = NestedGraphNodeDescriptor.CreateStateful(
            static (_, _) => { },
            live => firstLiveSet.AddRange(live),
            structuralToken: "rebound-completion-callback");
        using EffectGraph firstGraph = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery)
            .NestedGraph(firstDescriptor)
            .Build();
        CompiledPlan cached = EffectGraphCompiler.Compile(firstGraph, diagnostics: null);

        var currentDescriptor = NestedGraphNodeDescriptor.CreateStateful(
            static (_, _) => { },
            live => currentLiveSet.AddRange(live),
            structuralToken: "rebound-completion-callback");
        using EffectGraph currentGraph = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery)
            .NestedGraph(currentDescriptor)
            .Build();
        CompiledPlan rebound = ParameterBlock.Extract(currentGraph).RebindOnto(cached);
        FrameResources resources = EffectGraphCompiler.ResolveResources(rebound, s_bounds, workingScale: 1f);
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds, static _ => { }, hitTest: s_bounds.Contains);

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            rebound, resources, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null,
            renderIntent: RenderIntent.Delivery);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.Multiple(() =>
        {
            Assert.That(firstLiveSet, Is.Empty, "a cache hit must not retain the compiled frame's lifecycle callback");
            Assert.That(currentLiveSet, Is.EqualTo(new[] { 0 }),
                "the rebound pass receives the current frame's completion callback and stable live ordinal");
        });
    }

    private static void ExecuteNestedPull(NestedGraphNodeDescriptor descriptor, int inputCount)
    {
        using EffectGraph graph = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery)
            .NestedGraph(descriptor)
            .Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        RenderNodeOperation[] inputs = Enumerable.Range(0, inputCount)
            .Select(_ => RenderNodeOperation.CreateLambda(
                s_bounds, static _ => { }, hitTest: s_bounds.Contains))
            .ToArray();

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, resources, inputs, outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null,
            renderIntent: RenderIntent.Delivery);
        RenderNodeOperation.DisposeAll(outputs);
    }
}
