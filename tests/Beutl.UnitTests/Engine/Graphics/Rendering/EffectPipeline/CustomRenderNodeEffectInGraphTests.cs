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
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);

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
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);

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
            Assert.That(custom.IsRenderTimeResolved, Is.True, "a custom node cannot lay out until execution");
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

    // A throwing child render node must not leak the ops handed to it: the executor's catch disposes the inputs and
    // the whole plan execution unwinds (C7). Drives it through the executor directly so the throw is observable.
    [Test]
    public void Execute_CustomRenderNodeChildThrows_PropagatesAndReleasesInputs()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 1.5f } });
        group.Children.Add(new ThrowingCustomNodeEffect());

        CompiledPlan plan = CompileGroup(group);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        var input = MakeInput(s_bounds);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null));
        Assert.That(input.IsDisposed, Is.True, "the executor released the op it fed to the throwing child (no leak)");
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
            var context = new RenderNodeContext([MakeInput(s_bounds)]) { Diagnostics = diagnostics, Pool = pool };
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
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
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
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    private static StructuralKey KeyOf(FilterEffectGroup group, FilterEffect.Resource resource)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return StructuralKey.Compute(graph);
    }

    private static Bitmap RenderGroupRaster(FilterEffectGroup group)
    {
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        group.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, res, [MakeInput(s_bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
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
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: s_bounds.Size))
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
                using var canvas = new ImmediateCanvas(target, 1f, logicalSize: size.ToSize(1));
                canvas.Clear(Colors.Black);

                Drawable.Resource resource = makeScene();
                using var node = new DrawableRenderNode(resource);
                using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
                {
                    resource.GetOriginal().Render(ctx, resource);
                }

                var processor = new RenderNodeProcessor(
                    node, useRenderCache: false, outputScale: 1f, diagnostics: diagnostics, pool: pool);
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
internal sealed partial class ProbeCustomNodeEffect(int[] callCount) : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(callCount);
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(int[] callCount) : FilterEffect.Resource
    {
        public int[] CallCount => callCount;

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of(static r => new ProbeRenderNode((Resource)r));
    }
}

internal sealed class ProbeRenderNode(ProbeCustomNodeEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (FilterEffect?.Resource is ProbeCustomNodeEffect.Resource probe)
            probe.CallCount[0]++;

        return context.Input;
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

    public new sealed class Resource : FilterEffect.Resource
    {
        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of(static r => new ThrowingRenderNode(r));
    }
}

internal sealed class ThrowingRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
        => throw new InvalidOperationException("custom child render node failed");
}
