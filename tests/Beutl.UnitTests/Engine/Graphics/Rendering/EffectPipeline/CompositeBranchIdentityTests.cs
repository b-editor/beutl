using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression coverage for stable split-branch identity. A branch that produces no live operation must leave a hole
/// in the authored Emit order: downstream per-branch passes, nested graphs and composites must not renumber later
/// branches merely because the executor stores only the surviving operations.
/// </summary>
[NonParallelizable]
[TestFixture]
public class CompositeBranchIdentityTests
{
    private static readonly Rect s_input = new(0, 0, 30, 10);
    private static readonly Point[] s_offsets = [default, new(100, 0), new(200, 0)];

    [Test]
    public void Composite_AfterGeometryDropsMiddleBranch_UsesOriginalInputOffset()
    {
        var builder = CreateSplitBuilder(discardInsideSplit: false);
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                if (session.Inputs[0].Bounds.X == 10)
                {
                    session.DiscardOutput();
                    return;
                }

                DrawInput(session);
            },
            BoundsContract.Create(static bounds => bounds, static bounds => bounds),
            structuralToken: "drop-middle-geometry"));
        builder.Composite(CompositeNodeDescriptor.Create(
            BlendMode.SrcOver, s_offsets, structuralToken: "stable-offset-composite"));

        RenderNodeOperation[] outputs = Execute(builder);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].Bounds, Is.EqualTo(new Rect(0, 0, 230, 10)),
                "the surviving third branch keeps InputOffsets[2]; it must not slide onto InputOffsets[1]");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    [Test]
    public void Composite_WhenSplitDiscardsMiddleEmit_UsesOriginalInputOffset()
    {
        var builder = CreateSplitBuilder(discardInsideSplit: true);
        builder.Composite(CompositeNodeDescriptor.Create(
            BlendMode.SrcOver, s_offsets, structuralToken: "stable-sparse-split-composite"));

        RenderNodeOperation[] outputs = Execute(builder);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].Bounds, Is.EqualTo(new Rect(0, 0, 230, 10)),
                "a discarded Emit still consumes its branch ordinal, so the third branch keeps InputOffsets[2]");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    [Test]
    public void NestedGraph_AfterGeometryDropsMiddleBranch_ReceivesOriginalBranchIndex()
    {
        var seen = new List<int>();
        var builder = CreateSplitBuilder(discardInsideSplit: false);
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                if (session.Inputs[0].Bounds.X == 10)
                    session.DiscardOutput();
                else
                    DrawInput(session);
            },
            BoundsContract.Create(static bounds => bounds, static bounds => bounds),
            structuralToken: "drop-middle-before-nested"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "stable-nested-index"));

        RenderNodeOperation[] outputs = Execute(builder);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(seen, Is.EqualTo(new[] { 0, 2 }),
            "nested graph timing/cache identity must follow authored Emit order, not the compressed live-op index");
    }

    [Test]
    public void NestedGraph_AfterNestedStaticSplit_PreservesSparseParentBranchIndices()
    {
        var seen = new List<int>();
        var builder = CreateSplitBuilder(discardInsideSplit: true);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                for (int i = 0; i < 2; i++)
                {
                    emitter.Emit(emitter.Input.Bounds, session => DrawInput(session));
                }
            },
            branchCount: 2,
            structuralToken: "nested-static-split"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "nested-static-split-indices"));

        RenderNodeOperation[] outputs = Execute(builder);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(seen, Is.EqualTo(new[] { 0, 1, 4, 5 }),
            "each child ordinal must be based on its sparse parent ordinal, not the compressed live-input index");
    }

    [Test]
    public void NestedGraph_ChildFanOut_PreservesChildOrdinalsOutsideRecursiveExecution()
    {
        var seen = new List<int>();
        var builder = CreateSplitBuilder(discardInsideSplit: true);
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (branchBuilder, _) => branchBuilder.Split(SplitNodeDescriptor.Static(
                emitter =>
                {
                    for (int i = 0; i < 2; i++)
                        emitter.Emit(emitter.Input.Bounds, session => DrawInput(session));
                },
                branchCount: 2,
                structuralToken: "child-static-split")),
            structuralToken: "nested-child-fan-out"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "observe-child-fan-out-indices"));

        RenderNodeOperation[] outputs = Execute(builder);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(seen, Is.EqualTo(new[] { 0, 1, 4, 5 }),
            "recursive execution must return the child split's ordinals instead of relabeling every output with "
            + "its sparse parent ordinal");
    }

    [Test]
    public void NestedGraph_ChildDynamicFanOut_ConcatenatesSiblingOrdinalNamespaces()
    {
        var seen = new List<int>();
        var builder = CreateSplitBuilder(discardInsideSplit: false);
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (branchBuilder, _) => branchBuilder.Split(SplitNodeDescriptor.Dynamic(
                emitter =>
                {
                    emitter.Emit(emitter.Input.Bounds, static session => session.DiscardOutput());
                    emitter.Emit(emitter.Input.Bounds, session => DrawInput(session));
                },
                structuralToken: "child-dynamic-split")),
            structuralToken: "nested-child-dynamic-fan-out"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "observe-child-dynamic-indices"));

        RenderNodeOperation[] outputs = Execute(builder);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(seen, Is.EqualTo(new[] { 1, 3, 5 }),
            "each recursive dynamic split owns a consecutive slice of the downstream ordinal namespace, and a "
            + "discarded emit retains its hole instead of letting every parent publish ordinal one");
    }

    [Test]
    public void NestedGraph_MixedDynamicAndSparseStaticChildren_PreservesLeadingStaticHole()
    {
        var seen = new List<int>();
        var builder = new EffectGraphBuilder(
            s_input, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                emitter.Emit(new Rect(0, 0, 10, 10), session => DrawInput(session));
                emitter.Emit(new Rect(10, 0, 10, 10), session => DrawInput(session));
            },
            branchCount: 2,
            structuralToken: "two-parent-branches"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (branchBuilder, branchIndex) =>
            {
                if (branchIndex == 0)
                {
                    branchBuilder.Split(SplitNodeDescriptor.Dynamic(
                        emitter => emitter.Emit(emitter.Input.Bounds, session => DrawInput(session)),
                        structuralToken: "dynamic-first-child"));
                }
                else
                {
                    branchBuilder.Split(SplitNodeDescriptor.Static(
                        emitter =>
                        {
                            emitter.Emit(emitter.Input.Bounds, static session => session.DiscardOutput());
                            emitter.Emit(emitter.Input.Bounds, session => DrawInput(session));
                        },
                        branchCount: 2,
                        structuralToken: "sparse-static-second-child"));
                }
            },
            structuralToken: "mixed-child-cardinality"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "observe-mixed-child-indices"));

        RenderNodeOperation[] outputs = Execute(builder);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(seen, Is.EqualTo(new[] { 0, 2 }),
            "the surviving second static emit must retain the authored leading hole after a dynamic sibling");
    }

    [Test]
    public void NestedGraph_SiblingsWithDifferingStaticFanOut_ConcatenateOrdinalNamespaces()
    {
        var seen = new List<int>();
        var builder = new EffectGraphBuilder(
            s_input, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                emitter.Emit(new Rect(0, 0, 10, 10), session => DrawInput(session));
                emitter.Emit(new Rect(10, 0, 10, 10), session => DrawInput(session));
            },
            branchCount: 2,
            structuralToken: "two-parent-branches-for-mixed-fan-out"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (branchBuilder, branchIndex) =>
            {
                // Branch 0 fans out 2-way; branch 1 appends nothing (an identity child keeps its single branch).
                if (branchIndex == 0)
                {
                    branchBuilder.Split(SplitNodeDescriptor.Static(
                        emitter =>
                        {
                            emitter.Emit(emitter.Input.Bounds, session => DrawInput(session));
                            emitter.Emit(emitter.Input.Bounds, session => DrawInput(session));
                        },
                        branchCount: 2,
                        structuralToken: "wide-static-child"));
                }
            },
            structuralToken: "differing-static-fan-out"));
        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (_, branchIndex) => seen.Add(branchIndex),
            structuralToken: "observe-differing-fan-out-indices"));

        RenderNodeOperation[] outputs = Execute(builder);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2 }),
            "siblings with differing static fan-out factors cannot share one multiplicative namespace — the "
            + "2-way child's [0,1] must not collide with the identity sibling's [1]");
    }

    [Test]
    public void FoldedColorFilterFallback_AfterGeometryDropsMiddleBranch_KeepsOriginalOffset()
    {
        var builder = CreateSplitBuilder(discardInsideSplit: false, thirdBranchWidth: 100);
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                if (session.Inputs[0].Bounds.X == 10)
                    session.DiscardOutput();
                else
                    DrawInput(session);
            },
            BoundsContract.Create(static bounds => bounds, static bounds => bounds),
            structuralToken: "drop-middle-before-fold-fallback"));
        builder.Saturate(1.1f);
        builder.Composite(CompositeNodeDescriptor.Create(
            BlendMode.SrcOver, s_offsets, structuralToken: "stable-fold-fallback-composite"));

        RenderNodeOperation[] outputs = Execute(builder, maxDimension: 64);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].Bounds, Is.EqualTo(new Rect(0, 0, 320, 10)),
                "the density fallback remaps every surviving branch but must retain the third branch's ordinal");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    private static EffectGraphBuilder CreateSplitBuilder(bool discardInsideSplit, float thirdBranchWidth = 10)
    {
        var builder = new EffectGraphBuilder(s_input, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                for (int i = 0; i < 3; i++)
                {
                    int branch = i;
                    var bounds = new Rect(branch * 10, 0, branch == 2 ? thirdBranchWidth : 10, 10);
                    emitter.Emit(bounds, session =>
                    {
                        if (discardInsideSplit && branch == 1)
                            session.DiscardOutput();
                        else
                            DrawInput(session);
                    });
                }
            },
            branchCount: 3,
            structuralToken: discardInsideSplit ? "sparse-split" : "full-split"));
        return builder;
    }

    private static void DrawInput(GeometrySession session)
    {
        ImmediateCanvas canvas = session.OpenCanvas();
        using (canvas.PushDeviceSpace())
            session.Inputs[0].Draw(canvas, default);
    }

    private static RenderNodeOperation[] Execute(EffectGraphBuilder builder, int? maxDimension = null)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        if (maxDimension is { } cap)
            frame = frame with { MaxDimension = cap };
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_input,
            canvas => canvas.DrawRectangle(s_input, Brushes.Resource.White, null),
            hitTest: s_input.Contains);
        return PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
    }
}
