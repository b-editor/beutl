using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the composite fan-in draw path (execution-plan §C8/§C9): a src-over composite must draw its branches
/// without a full-canvas <c>SaveLayer</c> — the per-branch full-target round trip made a 3×3 SplitTree composite
/// alone cost ~21 ms at 4K, roughly 9× the branch area in layer traffic. Blend modes that do not preserve the
/// destination under a transparent source still need the full-canvas layer for correctness, so they keep it and
/// count <see cref="PipelineDiagnostics.CompositeLayerSaves"/>.
/// </summary>
[NonParallelizable]
[TestFixture]
public class CompositeDrawPathTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void SrcOverComposite_DrawsBranchesWithoutFullCanvasLayer()
    {
        PipelineDiagnosticsSnapshot counters = RunSplitComposite(BlendMode.SrcOver);
        Assert.That(counters.CompositeLayerSaves, Is.Zero,
            "a src-over composite branch must draw directly; a full-canvas SaveLayer per branch re-blends the " +
            "whole composite target once per branch");
    }

    [Test]
    public void FoldedSrcOverComposite_TakesNoFullCanvasLayer()
    {
        // SceneFixtures.SplitTree folds a Saturate run onto the composite's branch draws (C9): the fold filter
        // rides a branch-bounded layer, never a full-canvas one.
        PipelineDiagnosticsSnapshot counters = RenderAndSnapshot(
            SceneFixtures.Build("SplitTree", SceneFixtures.ReferenceSize));
        Assert.That(counters.CompositeLayerSaves, Is.Zero,
            "the C9 color-filter fold must ride a branch-bounded layer, not a full-canvas SaveLayer per branch");
    }

    [Test]
    public void NonSrcOverComposite_KeepsFullCanvasLayerPerBranch()
    {
        // Multiply zeroes the destination where the source is transparent, so limiting the layer to the branch
        // bounds would change every pixel outside them: the full-canvas layer is semantically load-bearing.
        PipelineDiagnosticsSnapshot counters = RunSplitComposite(BlendMode.Multiply);
        Assert.That(counters.CompositeLayerSaves, Is.EqualTo(2),
            "a non-dst-preserving blend mode must keep one full-canvas layer per branch");
    }

    private static PipelineDiagnosticsSnapshot RunSplitComposite(BlendMode blendMode)
    {
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
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
                structuralToken: "composite-draw-path-split"));
            builder.Composite(CompositeNodeDescriptor.Create(blendMode, structuralToken: "composite-draw-path"));

            using EffectGraph graph = builder.Build();
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, frame, [Input()], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: diagnostics, pool: pool);
            Assert.That(outputs, Has.Length.EqualTo(1), "the composite fans the two branches into one output");
            RenderNodeOperation.DisposeAll(outputs);
            return diagnostics.Snapshot();
        });
    }

    private static RenderNodeOperation Input()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds.Deflate(16), Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
    }

    private static PipelineDiagnosticsSnapshot RenderAndSnapshot(Drawable.Resource resource)
    {
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PixelSize size = SceneFixtures.ReferenceSize;
            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                        ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using var canvas = new ImmediateCanvas(target, 1f, logicalSize: size.ToSize(1));
            canvas.Clear(Colors.Black);

            using var node = new DrawableRenderNode(resource);
            using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
            {
                resource.GetOriginal().Render(ctx, resource);
            }

            var processor = new RenderNodeProcessor(node, useRenderCache: false, outputScale: 1f);
            RenderNodeOperation[] ops = processor.PullToRoot();
            foreach (RenderNodeOperation op in ops)
            {
                op.Render(canvas);
                op.Dispose();
            }

            return processor.Diagnostics.Snapshot();
        });
    }
}
