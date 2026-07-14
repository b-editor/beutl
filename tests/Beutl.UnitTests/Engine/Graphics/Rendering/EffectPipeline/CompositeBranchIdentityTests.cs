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
        var builder = new EffectGraphBuilder(s_input, outputScale: 1f, workingScale: 1f);
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
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
    }
}
