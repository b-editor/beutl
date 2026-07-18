using System.Collections.Generic;
using System.Linq;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Covers the custom-render-node graph primitive (feature 004): an effect whose execution lives in a custom
/// <see cref="FilterEffectRenderNode"/> — the <see cref="NodeGraphFilterEffect"/> being the canonical case — is
/// describable everywhere via <see cref="EffectGraphBuilder.CustomRenderNode"/>, so it can be embedded in a
/// <see cref="FilterEffectGroup"/> or a <see cref="DelayAnimationEffect"/> branch. Regression guard for the P1
/// crash where <c>NodeGraphFilterEffect.Describe</c> threw <see cref="NotSupportedException"/> unconditionally, so a
/// group (or delay-animation) walking its children crashed the whole render. The graph-level and executor cases run
/// without a GPU (raster); the node-graph pixel/diagnostics cases are Vulkan-gated.
/// </summary>
[TestFixture]
public class CustomRenderNodeEffectInGraphTests
{
    private static readonly Rect s_bounds = new(0, 0, 128, 96);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // ---- Describe / compile (GPU-free) -----------------------------------------------------------------

    // The P1 defect: this describe threw NotSupportedException from NodeGraphFilterEffect.Describe.
    [Test]
    public void GroupWithNodeGraphChild_Describes_WithoutThrowing()
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.5f;
        group.Children.Add(gamma);
        group.Children.Add(new NodeGraphFilterEffect());
        var invert = new Invert();
        invert.Amount.CurrentValue = 1f;
        group.Children.Add(invert);

        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);

        Assert.DoesNotThrow(() => group.Describe(builder, resource));
        using EffectGraph graph = builder.Build();
        Assert.That(graph.Nodes.Count, Is.GreaterThanOrEqualTo(3),
            "gamma, the node-graph custom node, and invert each append a node");
    }

    [Test]
    public void DelayAnimationWrappingNodeGraph_Describes_WithoutThrowing()
    {
        var delay = new DelayAnimationEffect();
        delay.Effect.CurrentValue = new NodeGraphFilterEffect();

        using FilterEffect.Resource resource = (FilterEffect.Resource)delay.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);

        // The delay describes a nested-graph node; the branch callback (which describes the child NodeGraphFilterEffect)
        // runs at execution, so a describe-time crash would have to be in the outer append — assert it stays clean.
        Assert.DoesNotThrow(() => delay.Describe(builder, resource));
    }

    [Test]
    public void GroupWithCustomRenderNode_CompilesToExpectedPassSchedule()
    {
        CompiledPlan plan = CompileGroup(MakeGammaProbeInvertGroup(new int[1]));

        Assert.That(plan.Passes.Select(p => p.GetType()),
            Is.EqualTo(new[] { typeof(FusedShaderPass), typeof(CustomRenderNodePass), typeof(FusedShaderPass) }),
            "the custom node compiles to its own CustomRenderNodePass between the two fused color passes");

        var custom = (CustomRenderNodePass)plan.Passes[1];
        Assert.Multiple(() =>
        {
            Assert.That(custom.RequiresFullInput, Is.True, "a custom node cannot lay out until execution");
            Assert.That(custom.IsDynamicOutputs, Is.True, "its output count is execution-time-resolved (exempt from the peak-live bound)");
            Assert.That(custom.NodeType, Is.EqualTo(typeof(ProbeRenderNode)), "the pass carries the child's render-node type");
        });
    }

    // C10 non-capturable: an CustomRenderNodePass is neither a FusedShaderPass nor a SkiaFilterPass, so the pass-prefix
    // cache's capturable predicate must never retain it (it terminates the prefix like a split/nested pass).
    [Test]
    public void CustomRenderNodePass_TerminatesTheCapturablePrefix()
    {
        CompiledPlan plan = CompileGroup(MakeGammaProbeInvertGroup(new int[1]));

        Assert.That(plan.Passes[1], Is.Not.InstanceOf<FusedShaderPass>().And.Not.InstanceOf<SkiaFilterPass>(),
            "the capturable-pass predicate only ever matches Fused/Skia passes, so the custom pass ends the prefix");
    }

    [Test]
    public void RenderNodeFactory_RejectsDeclaredTypeThatDiffersFromCreatedType()
    {
        var effect = new ProbeCustomNodeEffect(new int[1]);
        using var resource = (ProbeCustomNodeEffect.Resource)effect.ToResource(CompositionContext.Default);
        FilterEffectRenderNodeFactory factory =
            FilterEffectRenderNodeFactory.Of<ProbeCustomNodeEffect.Resource, FilterEffectRenderNode>(
                static r => new ProbeRenderNode(r));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => factory.Create(resource))!;
        Assert.That(error.Message, Does.Contain(nameof(ProbeRenderNode)).And.Contain("concrete node type"));
    }

    // The public authoring pair: CustomRenderNodeDescriptor.Create is public, so the builder exposes a public
    // appender to reach it (Effect() covers resources; this covers an author holding a constructed descriptor).
    [Test]
    public void PublicCustomRenderNodeAppender_DrivesTheDescriptorLikeTheEffectPath()
    {
        var probeCalls = new int[1];
        var effect = new ProbeCustomNodeEffect(probeCalls);
        using ProbeCustomNodeEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.CustomRenderNode(CustomRenderNodeDescriptor.Create(resource));

        RenderNodeOperation[] outputs = Execute(builder);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(probeCalls[0], Is.GreaterThanOrEqualTo(1),
            "the publicly appended descriptor must drive the child render node");
    }

    // ---- Executor (GPU-free, raster) -------------------------------------------------------------------

    // Proves the CustomRenderNodePass genuinely drives the child render node (the probe counter increments) and threads
    // the ops through it. With an identity child, the group renders identically to the same group without the custom
    // node — so the surrounding Gamma and Invert stages both compose correctly across the custom boundary.
    [Test]
    public void Execute_CustomRenderNode_DrivesChildRenderNode_AndComposesSurroundingStages()
    {
        var probeCalls = new int[1];
        FilterEffectGroup withProbe = MakeGammaProbeInvertGroup(probeCalls);
        FilterEffectGroup withoutProbe = MakeGammaInvertGroup();

        using Bitmap withProbeResult = RenderGroupRaster(withProbe);
        using Bitmap withoutProbeResult = RenderGroupRaster(withoutProbe);

        Assert.Multiple(() =>
        {
            Assert.That(probeCalls[0], Is.GreaterThanOrEqualTo(1),
                "the custom node's custom render node ran (the ops were threaded through it)");

            double ssim = ImageMetrics.Ssim(withoutProbeResult, withProbeResult);
            double mae = ImageMetrics.MeanAbsoluteError(withoutProbeResult, withProbeResult);
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin),
                $"an identity custom child leaves Gamma->Invert byte-identical (SSIM {ssim})");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        });
    }

    [Test]
    public void Execute_CustomRenderNodeFanOut_FromLinearInput_PreservesOutputIdentityAfterDrop()
    {
        var seen = new List<int>();
        var effect = new FanOutCustomNodeEffect(outputCount: 3);
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var runtimeCache = new NestedGraphPlanCache();
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery,
            nestedPlanCache: runtimeCache);
        builder.Effect(resource);
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                if (session.Inputs[0].Bounds.X == 1)
                    session.DiscardOutput();
            },
            BoundsContract.Create(static bounds => bounds, static bounds => bounds),
            structuralToken: "drop-middle-custom-output"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "observe-linear-custom-output-indices"));

        RenderNodeOperation.DisposeAll(Execute(builder));

        Assert.That(seen, Is.EqualTo(new[] { 0, 2 }),
            "a custom fan-out creates a stable ordinal namespace even from a linear input, so dropping the middle "
            + "output cannot compress the final output onto ordinal one");
    }

    [Test]
    public void Execute_CustomRenderNodeFanOut_FromSparseParent_StartsFreshDynamicNamespace()
    {
        var seen = new List<int>();
        var effect = new FanOutCustomNodeEffect(outputCount: 2);
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var runtimeCache = new NestedGraphPlanCache();
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery,
            nestedPlanCache: runtimeCache);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                for (int i = 0; i < 3; i++)
                {
                    int parentOrdinal = i;
                    emitter.Emit(new Rect(parentOrdinal * 10, 0, 10, 10), session =>
                    {
                        if (parentOrdinal != 2)
                            session.DiscardOutput();
                    });
                }
            },
            branchCount: 3,
            structuralToken: "sparse-parent-before-custom-fan-out"));
        builder.Effect(resource);
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "observe-sparse-custom-output-indices"));

        RenderNodeOperation.DisposeAll(Execute(builder));

        Assert.That(seen, Is.EqualTo(new[] { 0, 1 }),
            "a custom fan-out's execution-time output count starts a fresh dynamic ordinal namespace (like a "
            + "dynamic split), rather than a parent-ordinal stride that can collide across differing sibling counts");
    }

    // Regression: a parent-ordinal * outputCount stride collides when sibling branches return different counts
    // (3 and 2 outputs both publish ordinals 4/5), folding two branches onto one downstream branch identity.
    [Test]
    public void Execute_CustomRenderNodeFanOut_DifferingSiblingCounts_KeepDistinctBranchIdentities()
    {
        var seen = new List<int>();
        int[] fanOutCounts = [1, 3, 2];
        FilterEffect.Resource[] resources = fanOutCounts
            .Select(count => (FilterEffect.Resource)new FanOutCustomNodeEffect(count).ToResource(CompositionContext.Default))
            .ToArray();
        try
        {
            using var runtimeCache = new NestedGraphPlanCache();
            var builder = new EffectGraphBuilder(
                s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery,
                nestedPlanCache: runtimeCache);
            builder.Split(SplitNodeDescriptor.Static(
                emitter =>
                {
                    for (int i = 0; i < 3; i++)
                        emitter.Emit(new Rect(i * 10, 0, 10, 10), static _ => { });
                },
                branchCount: 3,
                structuralToken: "uniform-parent-before-differing-fan-outs"));
            builder.NestedGraph(NestedGraphNodeDescriptor.Create(
                (childBuilder, branchIndex) => childBuilder.Effect(resources[branchIndex]),
                structuralToken: "per-branch-differing-custom-fan-out"));
            builder.NestedGraph(NestedGraphNodeDescriptor.Create(
                (_, branchIndex) => seen.Add(branchIndex),
                structuralToken: "observe-differing-fan-out-indices"));

            RenderNodeOperation.DisposeAll(Execute(builder));

            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }),
                "sibling branches fanning out to 1/3/2 outputs concatenate into one collision-free namespace, so "
                + "no two live branches share a downstream branch identity");
        }
        finally
        {
            foreach (FilterEffect.Resource resource in resources)
                resource.Dispose();
        }
    }

    // A throwing child render node must not leak the ops handed to it: the executor's catch disposes the inputs and
    // the whole plan execution unwinds (C7). Drives it through the executor directly so the throw is observable.
    [Test]
    public void Execute_CustomRenderNodeChildThrows_PropagatesAndReleasesInputs()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 1.5f } });
        group.Children.Add(new ThrowingCustomNodeEffect());

        using var runtimeCache = new NestedGraphPlanCache();
        CompiledPlan plan = CompileGroup(group, runtimeCache);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        var input = MakeInput(s_bounds);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery));
        Assert.That(input.IsDisposed, Is.True, "the executor released the op it fed to the throwing child (no leak)");
    }

    [Test]
    public void Execute_CustomRenderNodeChildFailure_IsNotReplacedByNodeDisposeFailure()
    {
        var primary = new InvalidOperationException("custom render-node process failed");
        var cleanup = new InvalidOperationException("custom render-node dispose failed");
        var group = new FilterEffectGroup();
        group.Children.Add(new ThrowingProcessAndDisposeCustomNodeEffect(primary, cleanup));

        var runtimeCache = new NestedGraphPlanCache();
        CompiledPlan plan = CompileGroup(group, runtimeCache);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        RenderNodeOperation input = MakeInput(s_bounds);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary),
                "custom-node cleanup must not replace the primary Process failure");
            Assert.That(input.IsDisposed, Is.True);
        });

        InvalidOperationException? disposeError = Assert.Throws<InvalidOperationException>(runtimeCache.Dispose);
        Assert.That(disposeError, Is.SameAs(cleanup),
            "the persistent node's disposal failure belongs to owner teardown, not the earlier Process failure");
    }

    [Test]
    public void Execute_CustomRenderNodeReturnsNullArray_ThrowsClearContractErrorAndReleasesInputs()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new NullArrayCustomNodeEffect());

        using var runtimeCache = new NestedGraphPlanCache();
        CompiledPlan plan = CompileGroup(group, runtimeCache);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        RenderNodeOperation input = MakeInput(s_bounds);

        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("null operation array"));
            Assert.That(input.IsDisposed, Is.True, "the invalid child result must not strand its input operation");
        });
    }

    // A custom-render-node whose FACTORY (RenderNodeFactory.Create) throws before the child node exists must still
    // release the ops the pass detached from the working set (C7). The upstream Gamma pass acquires one pooled output
    // buffer; if the factory throw stranded that op, its lease would leak. Before the fix the Create ran outside the
    // try that disposes the inputs and after current was cleared, so the outer Execute catch could not reach them.
    [Test]
    public void Execute_CustomRenderNodeFactoryThrows_PropagatesAndReleasesInputs()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 1.5f } });
        group.Children.Add(new ThrowingFactoryCustomNodeEffect());

        using var runtimeCache = new NestedGraphPlanCache();
        CompiledPlan plan = CompileGroup(group, runtimeCache);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        using var pool = new RenderTargetPool();

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [MakeInput(s_bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));
        Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
            "a factory throw must release the upstream pass output fed to the custom node (no pooled-lease leak)");
    }

    [Test]
    public void Execute_CustomRenderNode_StandaloneOwnerDefersNodeDisposeUntilOutputsExpire()
    {
        var disposed = new bool[1];
        var observedCache = new bool[1];
        var observedRoi = new Rect[1];
        var observedAuxiliary = new bool[1];
        var requested = new Rect(12, 8, 40, 30);
        var group = new FilterEffectGroup();
        group.Children.Add(new LifetimeProbeCustomNodeEffect(
            disposed, observedCache, observedRoi, observedAuxiliary));

        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        group.Describe(builder, resource);
        EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);
        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, res, [MakeInput(s_bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null,
            isRenderCacheEnabled: false,
            pullPurpose: RenderPullPurpose.Auxiliary, renderIntent: RenderIntent.Delivery);
        graph.Dispose();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(disposed[0], Is.False,
                    "retiring the standalone graph owner must defer node disposal while a lazy output is live");
                Assert.That(observedCache[0], Is.False,
                    "an embedded node inherits the caller's render-cache policy");
                Assert.That(observedRoi[0].IsInvalid, Is.True,
                    "an opaque embedded node must receive a conservative full-input request; forwarding the outer "
                    + "crop could clip pixels needed by a later expanding pass");
                Assert.That(observedAuxiliary[0], Is.True,
                    "an embedded node must preserve the parent auxiliary-pull policy");
            });

            using Bitmap rendered = Rasterize(outputs[0]);
            Assert.That(rendered.Width, Is.GreaterThan(0),
                "rendering the returned operation succeeds before its owning node is released");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }

        Assert.That(disposed[0], Is.True,
            "disposing the last returned operation completes the retired owner's deferred node disposal");
    }

    // ---- Plan cache / structural key -------------------------------------------------------------------

    [Test]
    public void StructuralKey_SameChildResource_IsStable_ButSwappedChild_Differs()
    {
        FilterEffectGroup group = MakeGammaProbeInvertGroup(new int[1]);
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);

        StructuralKey first = KeyOf(group, resource);
        StructuralKey again = KeyOf(group, resource);
        Assert.That(again, Is.EqualTo(first),
            "re-describing the same group (a re-render frame) yields an equal key — the child resource reference is stable");

        // Swap the custom child for a fresh instance: a new render-node target must recompile the plan (C5).
        FilterEffectGroup swapped = MakeGammaProbeInvertGroup(new int[1]);
        using FilterEffect.Resource swappedResource = (FilterEffect.Resource)swapped.ToResource(CompositionContext.Default);
        Assert.That(KeyOf(swapped, swappedResource), Is.Not.EqualTo(first),
            "a swapped custom child instance changes the structural key (recompile)");
    }

    // SC-002 with a custom node in the chain: an animated NEIGHBOR (Gamma amount) rebinds parameters without a
    // recompile — the custom node's structural key excludes the child's Version, so it never forces a per-frame
    // recompile. Drives a persistent node across frames on the pooled raster path.
    [Test]
    public void AnimatedNeighbor_WithCustomRenderNode_CompilesPlanExactlyOnce()
    {
        const int frames = 5;
        var probeCalls = new int[1];
        var gamma = new Gamma { Amount = { CurrentValue = 120f } };
        var group = new FilterEffectGroup();
        group.Children.Add(gamma);
        group.Children.Add(new ProbeCustomNodeEffect(probeCalls));
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });

        var resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        var perFrameCompiles = new long[frames];

        for (int f = 0; f < frames; f++)
        {
            pool.Trim(f);
            gamma.Amount.CurrentValue = 120f + 20f * f;
            bool updateOnly = false;
            resource.Update(group, CompositionContext.Default, ref updateOnly);
            node.Update(resource);

            diagnostics.Reset();
            var context = new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery) { Diagnostics = diagnostics, Pool = pool };
            RenderNodeOperation.DisposeAll(node.Process(context));
            perFrameCompiles[f] = diagnostics.Snapshot().PlanCompilations;
        }

        Assert.Multiple(() =>
        {
            Assert.That(perFrameCompiles[0], Is.EqualTo(1), "frame 0 compiles the plan once");
            Assert.That(perFrameCompiles.Skip(1).Sum(), Is.EqualTo(0),
                "later frames rebind the animated neighbor's parameters without recompiling (the custom node does not force it)");
            Assert.That(probeCalls[0], Is.EqualTo(frames), "the custom child render node ran every frame");
        });
    }

    [Test]
    public void EmbeddedCustomRenderNode_ReusesStateAcrossFrames_AndDisposesWithPlanOwner()
    {
        var calls = new int[1];
        var creations = new int[1];
        var disposals = new int[1];
        var observedStates = new List<int>();
        var group = new FilterEffectGroup();
        group.Children.Add(new ProbeCustomNodeEffect(calls, creations, disposals, observedStates));

        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var owner = new PlanFilterEffectRenderNode(resource);
        for (int frame = 0; frame < 2; frame++)
        {
            RenderNodeOperation[] outputs = owner.Process(
                new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery));
            RenderNodeOperation.DisposeAll(outputs);
        }

        Assert.Multiple(() =>
        {
            Assert.That(creations[0], Is.EqualTo(1),
                "parameter rebinds reuse the embedded custom node instead of recreating it per frame");
            Assert.That(observedStates, Is.EqualTo(new[] { 1, 2 }),
                "mutable node state survives across the descriptor's lifetime");
            Assert.That(disposals[0], Is.Zero,
                "disposing frame outputs releases leases but not the still-current persistent node");
        });

        owner.Dispose();
        Assert.That(disposals[0], Is.EqualTo(1),
            "the plan render-node owner disposes the embedded node exactly once");
    }

    [Test]
    public void AuxiliaryPull_WithDifferentStructure_PreservesFrameCustomNodeState()
    {
        var calls = new int[1];
        var creations = new int[1];
        var disposals = new int[1];
        var observedStates = new List<int>();
        var effect = new PurposeConditionalCustomNodeEffect(
            new ProbeCustomNodeEffect(calls, creations, disposals, observedStates));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var owner = new PlanFilterEffectRenderNode(resource);
        var diagnostics = new PipelineDiagnostics();

        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)
            {
                Diagnostics = diagnostics,
            }));
        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext(
                [MakeInput(s_bounds)], RenderIntent.Delivery,
                pullPurpose: RenderPullPurpose.Auxiliary)
            {
                Diagnostics = diagnostics,
            }));
        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)
            {
                Diagnostics = diagnostics,
            }));

        Assert.Multiple(() =>
        {
            Assert.That(creations[0], Is.EqualTo(1),
                "an auxiliary graph must not retire the persistent custom node owned by the frame graph");
            Assert.That(disposals[0], Is.Zero,
                "the frame custom node stays live until its frame-plan owner is disposed");
            Assert.That(observedStates, Is.EqualTo(new[] { 1, 2 }),
                "the frame custom node's mutable state survives a structurally different auxiliary pull");
            Assert.That(diagnostics.Snapshot().PlanCompilations, Is.EqualTo(2),
                "the auxiliary plan compiles independently and must not evict the frame plan");
        });

        owner.Dispose();
        Assert.That(disposals[0], Is.EqualTo(1),
            "the retained frame custom node is disposed exactly once with its owner");
    }

    [Test]
    public void EmbeddedCustomRenderNode_ReplacementAndPruneDisposeRetiredNodes()
    {
        var calls = new int[1];
        var creations = new int[1];
        var disposals = new int[1];
        var group = new FilterEffectGroup();
        group.Children.Add(new ProbeCustomNodeEffect(calls, creations, disposals));
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        using var owner = new PlanFilterEffectRenderNode(resource);

        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)));

        group.Children[0] = new ProbeCustomNodeEffect(calls, creations, disposals);
        bool updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);
        owner.Update(resource);
        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)));
        Assert.Multiple(() =>
        {
            Assert.That(creations[0], Is.EqualTo(2));
            Assert.That(disposals[0], Is.EqualTo(1),
                "a resource-identity replacement retires the previous persistent node");
        });

        group.Children.Clear();
        updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);
        owner.Update(resource);
        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)));
        Assert.That(disposals[0], Is.EqualTo(2),
            "removing the descriptor prunes and disposes its persistent cache entry");
    }

    [Test]
    public void EmbeddedCustomRenderNode_ReceivesAncestorCacheNotification()
    {
        var calls = new int[1];
        var cacheNotifications = new int[1];
        var group = new FilterEffectGroup();
        group.Children.Add(new ProbeCustomNodeEffect(
            calls, servedFromCacheCount: cacheNotifications));
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        using var owner = new PlanFilterEffectRenderNode(resource);

        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)));
        owner.OnServedFromCache();

        Assert.That(cacheNotifications[0], Is.EqualTo(1),
            "an ancestor cache hit must notify persistent embedded nodes that Process will not run");
    }

    [Test]
    public void DisabledOwner_NotifiesNestedCustomNodesThatExecutionWasSkipped()
    {
        var calls = new int[1];
        var cacheNotifications = new int[1];
        var group = new FilterEffectGroup();
        group.Children.Add(new ProbeCustomNodeEffect(
            calls, servedFromCacheCount: cacheNotifications));
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var owner = new PlanFilterEffectRenderNode(resource);

        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)));
        Assert.That(calls[0], Is.EqualTo(1), "sanity: the nested node was created and executed");

        group.IsEnabled = false;
        bool updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);
        owner.Update(resource);
        RenderNodeOperation.DisposeAll(owner.Process(
            new RenderNodeContext([MakeInput(s_bounds)], RenderIntent.Delivery)));

        Assert.That(cacheNotifications[0], Is.EqualTo(1),
            "a disabled plan owner must notify nested nodes so they release execution-dependent retained state");
    }

    [Test]
    public void EmbeddedPlanNode_WithOpaqueParentInput_NeverReusesContentBlindPrefix()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new NestedPrefixPlanEffect());
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var owner = new PlanFilterEffectRenderNode(resource);
        using var pool = new RenderTargetPool();
        var diagnostics = new PipelineDiagnostics();

        long totalHits = 0;
        for (int frame = 0; frame < 6; frame++)
        {
            pool.Trim(frame);
            diagnostics.Reset();
            var input = RenderNodeOperation.CreateLambda(
                s_bounds,
                canvas => canvas.DrawRectangle(
                    s_bounds,
                    frame % 2 == 0 ? Brushes.Resource.Red : Brushes.Resource.Blue,
                    null),
                hitTest: s_bounds.Contains);
            var context = new RenderNodeContext([input], RenderIntent.Delivery)
            {
                Diagnostics = diagnostics,
                Pool = pool,
                IsRenderCacheEnabled = true,
            };

            RenderNodeOperation.DisposeAll(owner.Process(context));
            totalHits += diagnostics.Snapshot().PrefixCacheHits;
        }

        Assert.Multiple(() =>
        {
            Assert.That(totalHits, Is.Zero,
                "opaque custom-pass inputs can change pixels at stable bounds, so the nested prefix must fail closed");
            Assert.That(pool.RetainedPrefixCount, Is.Zero,
                "an unstable nested input must not retain a content-blind prefix buffer");
        });
    }

    // ---- Base class: group-safe by construction (GPU-free) ---------------------------------------------

    // M2: an effect deriving from CustomRenderNodeFilterEffect inherits a sealed Describe (it declares none of its
    // own) and still renders correctly inside a FilterEffectGroup — grouping works with zero Describe boilerplate.
    [Test]
    public void CustomRenderNodeFilterEffectSubclass_IsGroupSafe_WithoutDescribeBoilerplate()
    {
        System.Reflection.MethodInfo describe = typeof(ProbeCustomNodeEffect).GetMethod(nameof(FilterEffect.Describe))!;
        Assert.That(describe.DeclaringType, Is.EqualTo(typeof(CustomRenderNodeFilterEffect)),
            "the subclass declares no Describe of its own — it inherits the base's sealed one");

        var probeCalls = new int[1];
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 1.5f } });
        group.Children.Add(new ProbeCustomNodeEffect(probeCalls));
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });

        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        Assert.DoesNotThrow(() => group.Describe(builder, resource),
            "the base's sealed Describe appends the custom node, so the group describes cleanly");

        using Bitmap withProbe = RenderGroupRaster(MakeGammaProbeInvertGroup(probeCalls));
        using Bitmap withoutProbe = RenderGroupRaster(MakeGammaInvertGroup());
        Assert.Multiple(() =>
        {
            Assert.That(probeCalls[0], Is.GreaterThanOrEqualTo(1), "the base-derived effect's custom render node ran");
            double ssim = ImageMetrics.Ssim(withoutProbe, withProbe);
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin),
                $"the identity custom child composes byte-identically between the group's stages (SSIM {ssim})");
        });
    }

    [Test]
    public void CustomRenderNodeFilterEffect_RequiresDedicatedResourceAndRejectsWrongResourceBeforeRecursion()
    {
        System.Reflection.PropertyInfo factory = typeof(CustomRenderNodeFilterEffect.Resource)
            .GetProperty(nameof(CustomRenderNodeFilterEffect.Resource.RenderNodeFactory))!;
        Assert.Multiple(() =>
        {
            Assert.That(factory.GetMethod!.IsAbstract, Is.True,
                "a custom-node resource must implement its factory at compile time");
            Assert.That(typeof(ProbeCustomNodeEffect.Resource)
                .IsSubclassOf(typeof(CustomRenderNodeFilterEffect.Resource)), Is.True);
        });

        var effect = new ProbeCustomNodeEffect(new int[1]);
        using FilterEffect.Resource wrongResource = (FilterEffect.Resource)new Gamma()
            .ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);

        try
        {
            Assert.That(() => effect.Describe(builder, wrongResource),
                Throws.ArgumentException.With.Message.Contains(nameof(CustomRenderNodeFilterEffect)),
                "an invalid resource must fail before the default plan factory can recurse");
        }
        finally
        {
            builder.Abort();
        }
    }

    [Test]
    public void Execute_CustomRenderNodeChildProcessor_InheritsRootAllocator()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new ChildRasterizingCustomNodeEffect());
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var owner = new PlanFilterEffectRenderNode(resource);
        owner.AddChild(new AllocationProbeInputNode());
        var processor = new ThrowingAllocationRenderNodeProcessor(owner);

        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() => processor.PullToRoot());

        Assert.That(error!.Message, Is.EqualTo("inherited-allocation-fault"),
            "a custom-node context created by PlanExecutor must preserve the root processor's allocation policy");
    }

    // ---- Node-graph end-to-end (Vulkan-gated) ----------------------------------------------------------

    // The real crash scenario, rendered: a group holding a NodeGraphFilterEffect (whose graph applies Gamma) must
    // render identically to the same group with a plain Gamma in that slot — proving the embedded node graph runs and
    // composes with the surrounding stages, not just that it stops throwing.
    [Test]
    public void GroupWithNodeGraphChild_RendersLikeEquivalentPlainChain()
    {
        VulkanTestEnvironment.EnsureAvailable();
        (Bitmap graphResult, Bitmap plainResult) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
            (RenderShapeScene(MakeShape(MakeGammaNodeGraphInvertGroup())),
             RenderShapeScene(MakeShape(MakeGammaPlainGammaInvertGroup()))));

        using (graphResult)
        using (plainResult)
        {
            double ssim = ImageMetrics.Ssim(plainResult, graphResult);
            double mae = ImageMetrics.MeanAbsoluteError(plainResult, graphResult);
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin),
                $"the node-graph Gamma stage composes exactly like a plain Gamma between the group's other stages (SSIM {ssim})");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        }
    }

    // The inner node-graph effect's GPU pass must count on the PARENT renderer's diagnostics and share its pool even
    // when the node graph is embedded in a group, and a second identical frame must reach steady-state pool reuse.
    [Test]
    public void GroupWithNodeGraphChild_SharesParentDiagnosticsAndPool()
    {
        const int frames = 4;
        VulkanTestEnvironment.EnsureAvailable();
        PipelineDiagnosticsSnapshot[] perFrame = RenderFramesWithPool(
            () => MakeShape(MakeGammaNodeGraphInvertGroup()), frames);

        Assert.Multiple(() =>
        {
            // frame 1: the group's two Gamma/Invert fused passes PLUS the inner node-graph Gamma pass all count on the
            // parent diagnostics; at least the inner pass proves the boundary threads through the custom node.
            Assert.That(perFrame[0].GpuPasses, Is.GreaterThanOrEqualTo(3),
                "the group's own passes and the embedded node-graph effect's pass all count on the parent diagnostics");
            Assert.That(perFrame[0].PoolAcquires, Is.GreaterThanOrEqualTo(1),
                "the embedded effect acquires its intermediate from the shared pool");

            for (int f = 1; f < frames; f++)
            {
                Assert.That(perFrame[f].TargetAllocations, Is.EqualTo(0),
                    $"frame {f + 1} adds no fresh allocations (steady-state reuse across the custom-node boundary)");
                Assert.That(perFrame[f].PoolMisses, Is.EqualTo(0),
                    $"frame {f + 1} has no pool misses (the warmed buffers are reused)");
            }
        });
    }

    [Test]
    public void DelayAnimationWrappingNodeGraph_Renders_WithoutThrowing()
    {
        VulkanTestEnvironment.EnsureAvailable();
        Bitmap result = VulkanTestEnvironment.InvokeOnRenderThread(
            () => RenderShapeScene(MakeShape(MakeDelayWrappingNodeGraph())));

        using (result)
        {
            Assert.That(result.Width * result.Height, Is.GreaterThan(0),
                "a delay-animation effect wrapping a node graph renders (branch 0 describes the node graph as a custom node)");
        }
    }

    // ---- Fixtures --------------------------------------------------------------------------------------

    private static FilterEffectGroup MakeGammaProbeInvertGroup(int[] probeCalls)
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 1.5f } });
        group.Children.Add(new ProbeCustomNodeEffect(probeCalls));
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
        return group;
    }

    private static FilterEffectGroup MakeGammaInvertGroup()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 1.5f } });
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
        return group;
    }

    private static FilterEffectGroup MakeGammaNodeGraphInvertGroup()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 120f } });
        group.Children.Add(MakeNodeGraphGamma(1.5f));
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
        return group;
    }

    private static FilterEffectGroup MakeGammaPlainGammaInvertGroup()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 120f } });
        group.Children.Add(new Gamma { Amount = { CurrentValue = 1.5f } });
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
        return group;
    }

    private static DelayAnimationEffect MakeDelayWrappingNodeGraph()
    {
        var delay = new DelayAnimationEffect();
        delay.Effect.CurrentValue = MakeNodeGraphGamma(1.5f);
        return delay;
    }

    // A NodeGraphFilterEffect whose graph is Input -> FilterEffectNode<Gamma> -> Output (the boundary-test pattern).
    private static NodeGraphFilterEffect MakeNodeGraphGamma(float amount)
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;

        var inputNode = new FilterEffectInputNode();
        var gammaNode = new FilterEffectNode<Gamma>();
        gammaNode.Object.Amount.CurrentValue = amount;
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(gammaNode);
        model.Nodes.Add(outputNode);

        model.Connect((IInputPort)gammaNode.Items[1], inputNode.Output);
        model.Connect(outputNode.InputPort, (IOutputPort)gammaNode.Items[0]);
        return effect;
    }

    private static CompiledPlan CompileGroup(FilterEffectGroup group)
    {
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    private static CompiledPlan CompileGroup(FilterEffectGroup group, NestedGraphPlanCache runtimeCache)
    {
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery,
            nestedPlanCache: runtimeCache);
        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    private static RenderNodeOperation[] Execute(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, resources, [MakeInput(s_bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null,
            renderIntent: RenderIntent.Delivery);
    }

    private static StructuralKey KeyOf(FilterEffectGroup group, FilterEffect.Resource resource)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return StructuralKey.Compute(graph);
    }

    private static Bitmap RenderGroupRaster(FilterEffectGroup group)
    {
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        using var runtimeCache = new NestedGraphPlanCache();
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery,
            nestedPlanCache: runtimeCache);
        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, res, [MakeInput(s_bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            return Rasterize(ops[0]);
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    private static RenderNodeOperation MakeInput(Rect bounds)
    {
        return RenderNodeOperation.CreateLambda(
            bounds,
            canvas =>
            {
                canvas.DrawRectangle(bounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(bounds.X, bounds.Y, bounds.Width / 2, bounds.Height), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(bounds.X, bounds.Y + bounds.Height / 2, bounds.Width, bounds.Height / 2), Brushes.Resource.Blue, null);
            },
            hitTest: bounds.Contains);
    }

    private static Bitmap Rasterize(RenderNodeOperation op)
    {
        var size = PixelRect.FromRect(s_bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-s_bounds.X, -s_bounds.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }

    // ---- Vulkan scene helpers (mirrors NodeGraphEffectBoundaryDiagnosticsTests) -------------------------

    private static Bitmap RenderShapeScene(Drawable.Resource resource)
    {
        PixelSize size = SceneFixtures.ReferenceSize;
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
        canvas.Clear(Colors.Black);

        using var node = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
        {
            resource.GetOriginal().Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f);
        RenderNodeOperation[] ops = processor.PullToRoot();
        foreach (RenderNodeOperation op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }

        return target.Snapshot();
    }

    private static PipelineDiagnosticsSnapshot[] RenderFramesWithPool(Func<Drawable.Resource> makeScene, int frames)
    {
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PixelSize size = SceneFixtures.ReferenceSize;
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            var snapshots = new PipelineDiagnosticsSnapshot[frames];

            for (int f = 0; f < frames; f++)
            {
                pool.Trim(f);
                diagnostics.Reset();

                using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
                using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
                canvas.Clear(Colors.Black);

                Drawable.Resource resource = makeScene();
                using var node = new DrawableRenderNode(resource);
                using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
                {
                    resource.GetOriginal().Render(ctx, resource);
                }

                var processor = new RenderNodeProcessor(
                    pool, node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f,
                    diagnostics: diagnostics);
                RenderNodeOperation[] ops = processor.PullToRoot();
                foreach (RenderNodeOperation op in ops)
                {
                    op.Render(canvas);
                    op.Dispose();
                }

                snapshots[f] = diagnostics.Snapshot();
            }

            return snapshots;
        });
    }

    private static Drawable.Resource MakeShape(FilterEffect effect)
    {
        var fill = new LinearGradientBrush();
        fill.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        fill.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        fill.GradientStops.Add(new GradientStop(Colors.Red, 0));
        fill.GradientStops.Add(new GradientStop(Colors.Lime, 0.5f));
        fill.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 240;
        shape.Height.CurrentValue = 150;
        shape.Fill.CurrentValue = fill;
        shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }
}

// A FilterEffect whose execution lives in a custom render node (the NodeGraphFilterEffect pattern). Deriving from
// CustomRenderNodeFilterEffect makes it describable — and group-safe — with no Describe boilerplate; the render node
// counts its invocations while passing the ops through unchanged (identity).
[SuppressResourceClassGeneration]
internal sealed partial class ProbeCustomNodeEffect(
    int[] callCount,
    int[]? creationCount = null,
    int[]? disposalCount = null,
    List<int>? observedStates = null,
    int[]? servedFromCacheCount = null) : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(
            callCount, creationCount, disposalCount, observedStates, servedFromCacheCount);
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(
        int[] callCount,
        int[]? creationCount,
        int[]? disposalCount,
        List<int>? observedStates,
        int[]? servedFromCacheCount) : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, ProbeRenderNode>(static r => new ProbeRenderNode(r));

        public int[] CallCount => callCount;

        public int[]? CreationCount => creationCount;

        public int[]? DisposalCount => disposalCount;

        public List<int>? ObservedStates => observedStates;

        public int[]? ServedFromCacheCount => servedFromCacheCount;

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class PurposeConditionalCustomNodeEffect(ProbeCustomNodeEffect child) : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        if (builder.PullPurpose == RenderPullPurpose.Frame)
            builder.Effect(((Resource)resource).Child);
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(
            (ProbeCustomNodeEffect.Resource)child.ToResource(context));
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(ProbeCustomNodeEffect.Resource childResource) : FilterEffect.Resource
    {
        public ProbeCustomNodeEffect.Resource Child => childResource;

        protected override void Dispose(bool disposing)
        {
            childResource.Dispose();
            base.Dispose(disposing);
        }
    }
}

internal sealed class ProbeRenderNode : FilterEffectRenderNode
{
    private readonly ProbeCustomNodeEffect.Resource _resource;
    private int _state;

    public ProbeRenderNode(ProbeCustomNodeEffect.Resource resource)
        : base(resource)
    {
        _resource = resource;
        if (resource.CreationCount != null)
            resource.CreationCount[0]++;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (FilterEffect?.Resource is ProbeCustomNodeEffect.Resource probe)
        {
            probe.CallCount[0]++;
            probe.ObservedStates?.Add(++_state);
        }

        return context.Input;
    }

    protected override void OnDispose(bool disposing)
    {
        if (_resource.DisposalCount != null)
            _resource.DisposalCount[0]++;
        base.OnDispose(disposing);
    }

    protected internal override void OnServedFromCache()
    {
        if (_resource.ServedFromCacheCount != null)
            _resource.ServedFromCacheCount[0]++;
        base.OnServedFromCache();
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class ChildRasterizingCustomNodeEffect : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, ChildRasterizingRenderNode>(
                static resource => new ChildRasterizingRenderNode(resource));

        public override FilterEffectRenderNodeFactory RenderNodeFactory => s_factory;
    }
}

internal sealed class ChildRasterizingRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        using var child = new AllocationProbeInputNode();
        RenderNodeProcessor processor = context.CreateChildProcessor(child, useRenderCache: false);
        List<Bitmap> bitmaps = processor.Rasterize();
        foreach (Bitmap bitmap in bitmaps)
            bitmap.Dispose();
        return context.Input;
    }
}

internal sealed class AllocationProbeInputNode : RenderNode
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
        =>
        [
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 4, 4), static _ => { }),
        ];
}

internal sealed class ThrowingAllocationRenderNodeProcessor(RenderNode root)
    : RenderNodeProcessor(root, useRenderCache: false, RenderIntent.Delivery)
{
    protected override RenderTarget? CreateRenderTarget(int width, int height)
        => throw new InvalidOperationException("inherited-allocation-fault");
}

[SuppressResourceClassGeneration]
internal sealed partial class NestedPrefixPlanEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        builder.Blur(new Size(4, 4));
        builder.Geometry(GeometryNodeDescriptor.Create(
            static session =>
            {
                ImmediateCanvas canvas = session.OpenCanvas();
                using (canvas.PushDeviceSpace())
                    session.Inputs[0].Draw(canvas, default);
            },
            BoundsContract.Identity,
            structuralToken: "nested-prefix-tail"));
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        private static readonly PlanFilterEffectRenderNodeFactory s_factory =
            PlanFilterEffectRenderNodeFactory.Of<Resource, NestedPrefixPlanNode>(
                static resource => new NestedPrefixPlanNode(resource));

        public override PlanFilterEffectRenderNodeFactory PlanRenderNodeFactory => s_factory;
    }
}

internal sealed class NestedPrefixPlanNode(FilterEffect.Resource resource) : PlanFilterEffectRenderNode(resource);

[SuppressResourceClassGeneration]
internal sealed partial class FanOutCustomNodeEffect(int outputCount) : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(outputCount);
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(int outputCount) : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, FanOutRenderNode>(static r => new FanOutRenderNode(r));

        public int OutputCount => outputCount;

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class FanOutRenderNode(FanOutCustomNodeEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        RenderNodeOperation input = context.Input.Single();
        Rect inputBounds = input.Bounds;
        EffectiveScale effectiveScale = input.EffectiveScale;
        input.Dispose();

        return Enumerable.Range(0, resource.OutputCount)
            .Select(index =>
            {
                var bounds = new Rect(
                    inputBounds.X + index, inputBounds.Y, inputBounds.Width, inputBounds.Height);
                return RenderNodeOperation.CreateLambda(
                    bounds, static _ => { }, hitTest: bounds.Contains, effectiveScale: effectiveScale);
            })
            .ToArray();
    }
}

// A custom-render-node effect whose node always throws, to exercise the executor's C7 input-release-on-throw path.
[SuppressResourceClassGeneration]
internal sealed partial class ThrowingCustomNodeEffect : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, ThrowingRenderNode>(static r => new ThrowingRenderNode(r));

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class ThrowingRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
        => throw new InvalidOperationException("custom child render node failed");
}

[SuppressResourceClassGeneration]
internal sealed partial class ThrowingProcessAndDisposeCustomNodeEffect(
    Exception processFailure, Exception disposeFailure) : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(processFailure, disposeFailure);
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(Exception processFailure, Exception disposeFailure)
        : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, ThrowingProcessAndDisposeRenderNode>(
                static r => new ThrowingProcessAndDisposeRenderNode(r));

        public Exception ProcessFailure => processFailure;

        public Exception DisposeFailure => disposeFailure;

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class ThrowingProcessAndDisposeRenderNode(
    ThrowingProcessAndDisposeCustomNodeEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    private bool _disposeFailureInjected;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
        => throw resource.ProcessFailure;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        if (!_disposeFailureInjected)
        {
            _disposeFailureInjected = true;
            throw resource.DisposeFailure;
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class NullArrayCustomNodeEffect : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, NullArrayRenderNode>(static r => new NullArrayRenderNode(r));

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class NullArrayRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context) => null!;
}

[SuppressResourceClassGeneration]
internal sealed partial class LifetimeProbeCustomNodeEffect(
    bool[] disposed, bool[] observedCache, Rect[] observedRoi, bool[] observedAuxiliary)
    : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(disposed, observedCache, observedRoi, observedAuxiliary);
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(
        bool[] disposed, bool[] observedCache, Rect[] observedRoi, bool[] observedAuxiliary)
        : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, LifetimeProbeRenderNode>(
                static r => new LifetimeProbeRenderNode(r));

        public bool[] Disposed => disposed;
        public bool[] ObservedCache => observedCache;
        public Rect[] ObservedRoi => observedRoi;
        public bool[] ObservedAuxiliary => observedAuxiliary;

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class LifetimeProbeRenderNode(LifetimeProbeCustomNodeEffect.Resource resource)
    : FilterEffectRenderNode(resource)
{
    private bool _disposed;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        resource.ObservedCache[0] = context.IsRenderCacheEnabled;
        resource.ObservedRoi[0] = context.RequestedBounds;
        resource.ObservedAuxiliary[0] = context.IsAuxiliaryPull;
        RenderNodeOperation input = context.Input[0];
        return
        [
            RenderNodeOperation.CreateDecorator(input, canvas =>
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(LifetimeProbeRenderNode));
                input.Render(canvas);
            }),
        ];
    }

    protected override void OnDispose(bool disposing)
    {
        _disposed = true;
        resource.Disposed[0] = true;
        base.OnDispose(disposing);
    }
}

// A custom-render-node effect whose render-node FACTORY throws (Create fails before any node exists), to exercise the
// executor's C7 input-release-on-factory-throw path.
[SuppressResourceClassGeneration]
internal sealed partial class ThrowingFactoryCustomNodeEffect : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, FilterEffectRenderNode>(static _ =>
                throw new InvalidOperationException("custom render-node factory failed"));

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}
