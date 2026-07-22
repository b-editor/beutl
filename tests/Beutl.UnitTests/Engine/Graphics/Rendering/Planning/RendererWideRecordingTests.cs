using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RendererWideRecordingTests
{
    [Test]
    [Category("GpuPassFusionGpu")]
    public void ProductionFrameRenderer_PreservesPlanLifetimeAcrossFrames()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(8, 8);
            var frame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));

            renderer.Render(frame);
            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Misses, Is.EqualTo(1));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Hits, Is.EqualTo(1));
            });
        });
    }

    [Test]
    public void CompleteTarget_RecordsEveryOrderedRootBeforeAnyExecution()
    {
        bool[] recorded = new bool[3];
        var executed = new List<int>();
        using var first = new DeferredProbeNode(0, recorded, executed);
        using var second = new DeferredProbeNode(1, recorded, executed);
        using var third = new DeferredProbeNode(2, recorded, executed);
        using var completeTarget = new CompleteTargetRenderNode(first, [second, third]);
        using var destination = new CpuRenderTarget(8, 8);
        using var canvas = new ImmediateCanvas(destination);
        using var renderer = new RenderNodeRenderer(
            completeTarget,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 8, 8),
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

        renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.All.True);
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    [Test]
    public void CompleteTarget_RecordsClearCommandsCaptureAndPainterOrderBeforeExecution()
    {
        bool[] recorded = new bool[4];
        var executed = new List<string>();
        using var clear = new RecordingRootNode(0, recorded, new ClearRenderNode(Colors.Transparent));
        using var source = new OrderedSourceNode(1, recorded, executed);
        using var command = new OrderedTargetCommandNode(2, recorded, executed);
        using var capture = new OrderedCaptureNode(3, recorded, executed);
        using var completeTarget = new CompleteTargetRenderNode(clear, [source, command, capture]);
        using var destination = new CpuRenderTarget(8, 8);
        using var canvas = new ImmediateCanvas(destination);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new RenderNodeRenderer(
            completeTarget,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 8, 8),
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
                Diagnostics = diagnostics,
            });

        renderer.Render(canvas);

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        int lastRecorded = snapshot.Events.ToList().FindLastIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.FragmentRecorded);
        int firstExecutedPass = snapshot.Events.ToList().FindIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.PassExecuted);
        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.All.True);
            Assert.That(executed, Is.EqualTo(new[] { "source", "command", "capture" }));
            Assert.That(snapshot[RenderPipelineCounter.RecordedTargetCommands], Is.EqualTo(2),
                "The complete request must include both the root clear and authored target command.");
            Assert.That(snapshot[RenderPipelineCounter.RecordedTargetCaptures], Is.EqualTo(1));
            Assert.That(firstExecutedPass, Is.GreaterThan(lastRecorded),
                "Every complete-target fragment must be committed before planner-controlled execution starts.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ProductionRenderer_BuildsRecordsAndCommitsEveryTreeAsOneRequest()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var state = new RendererWideTreeState(2);
            var first = new RendererWideProbeDrawable(0, state);
            var second = new RendererWideProbeDrawable(1, state);
            var resources = ImmutableArray.Create<EngineObject.Resource>(
                (Drawable.Resource)first.ToResource(CompositionContext.Default),
                (Drawable.Resource)second.ToResource(CompositionContext.Default));
            var frame = new CompositionFrame(
                resources,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));
            using var renderer = new Renderer(8, 8);

            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(state.BuildCalls, Is.EqualTo(new[] { 1, 1 }));
                Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 2 }),
                    "Each tree participates once in request-wide bounds analysis and once in frame recording.");
                Assert.That(state.ExecutionOrder, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(state.Nodes, Has.All.Not.Null);
                Assert.That(state.Nodes, Has.All.Matches<ProductionTreeProbeNode>(
                    static node => !node.HasChanges && node.Cache.CanCache()),
                    "Successful complete-request execution must commit every tree's render-count/cache state.");
            });
        });
    }

    private sealed class RecordingRootNode(
        int index,
        bool[] recorded,
        RenderNode child) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            context.PublishRange(context.RecordNode(child, []));
        }

        protected override void OnDispose(bool disposing)
        {
            child.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class OrderedSourceNode(
        int index,
        bool[] recorded,
        ICollection<string> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            context.Publish(context.OpaqueSource(CreateDescription(
                "source",
                recorded,
                executed,
                static (session, output) =>
                    output.Canvas.Use(canvas => canvas.Clear(new Color(255, 40, 80, 120))))));
        }
    }

    private sealed class OrderedTargetCommandNode(
        int index,
        bool[] recorded,
        ICollection<string> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            TargetCommandDescription description = TargetCommandDescription.Create(
                session =>
                {
                    Assert.That(recorded, Is.All.True);
                    executed.Add("command");
                    session.Canvas.Use(canvas => canvas.Clear(new Color(255, 24, 48, 72)));
                },
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None,
                TargetAccess.ReadWrite,
                structuralKey: typeof(OrderedTargetCommandNode),
                runtimeIdentity: new RenderRuntimeIdentity("ordered-command"));
            context.Publish(context.TargetCommand([], description));
        }
    }

    private sealed class OrderedCaptureNode(
        int index,
        bool[] recorded,
        ICollection<string> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            Rect bounds = new(0, 0, 8, 8);
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Full,
                bounds,
                RenderHitTestContract.None,
                RenderScaleContract.MaterializeAtWorkingScale));
            OpaqueRenderDescription replay = OpaqueRenderDescription.Create(
                session =>
                {
                    Assert.That(recorded, Is.All.True);
                    executed.Add("capture");
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(session.Inputs.Single().Draw);
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: typeof(OrderedCaptureNode),
                runtimeIdentity: new RenderRuntimeIdentity("ordered-capture"));
            context.Publish(context.ContributeValues(context.OpaqueMap(capture, replay)));
        }
    }

    private static OpaqueRenderDescription CreateDescription(
        string name,
        bool[] recorded,
        ICollection<string> executed,
        Action<OpaqueRenderSession, OpaqueRenderOutput> draw)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                Assert.That(recorded, Is.All.True);
                executed.Add(name);
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                draw(session, output);
                session.Publish(output);
            },
            RenderOperationBoundsContract.Source(new Rect(0, 0, 8, 8)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: (typeof(RendererWideRecordingTests), name),
            runtimeIdentity: new RenderRuntimeIdentity(name));
    }

    private sealed class DeferredProbeNode(
        int index,
        bool[] recorded,
        List<int> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            var description = OpaqueRenderDescription.Create(
                session =>
                {
                    Assert.That(recorded, Is.All.True,
                        "No planner-controlled 2D callback may run until every target root is recorded.");
                    executed.Add(index);
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(new Color(255, 32, 64, 96)));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 8, 8)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(DeferredProbeNode), index),
                runtimeIdentity: new RenderRuntimeIdentity(index));
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}

internal sealed class RendererWideTreeState(int count)
{
    public int[] BuildCalls { get; } = new int[count];

    public int[] RecordCalls { get; } = new int[count];

    public List<int> ExecutionOrder { get; } = [];

    public ProductionTreeProbeNode?[] Nodes { get; } = new ProductionTreeProbeNode[count];
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class RendererWideProbeDrawable(
    int index,
    RendererWideTreeState state) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        state.BuildCalls[index]++;
        var node = new ProductionTreeProbeNode(index, state);
        node.Cache.ReportRenderCount(RenderNodeCache.Count - 1);
        state.Nodes[index] = node;
        context.DrawNode(node);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(8, 8);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class ProductionTreeProbeNode(
    int index,
    RendererWideTreeState state) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        Assert.That(state.BuildCalls, Is.All.EqualTo(1),
            "Every drawable tree must be built before the complete request starts recording.");
        state.RecordCalls[index]++;
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            session =>
            {
                Assert.That(state.RecordCalls, Is.All.EqualTo(2),
                    "Every top-level tree must be recorded before the first execution callback.");
                state.ExecutionOrder.Add(index);
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(
                    index == 0
                        ? new Color(160, 96, 32, 16)
                        : new Color(160, 16, 64, 128)));
                session.Publish(output);
            },
            RenderOperationBoundsContract.Source(new Rect(0, 0, 8, 8)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: (typeof(ProductionTreeProbeNode), index),
            runtimeIdentity: new RenderRuntimeIdentity(index));
        context.Publish(context.OpaqueSource(description));
    }
}
