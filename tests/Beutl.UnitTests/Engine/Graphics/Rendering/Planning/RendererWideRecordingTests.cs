using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
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
    [Category("GpuPassFusionGpu")]
    public void ProductionFrameRenderer_DisablesDiagnosticsByDefault()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(8, 8);

            Assert.That(renderer.Diagnostics, Is.Null);
        });
    }

    [Test]
    public void ProductionFrameRenderer_UsesExplicitDiagnostics()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var diagnostics = new RenderPipelineDiagnosticsState();
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                diagnostics: diagnostics,
                surface: new CpuRenderTarget(8, 8));
            var frame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));

            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.Diagnostics, Is.SameAs(diagnostics));
                Assert.That(diagnostics.LatestFrame.Succeeded, Is.True);
                Assert.That(diagnostics.LatestFrame.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
            });
        });
    }

    [Test]
    [NonParallelizable]
    [Category("GpuPassFusionGpu")]
    public void CacheMutations_FromCallerThread_DisposeCachedTreesOnRenderThread()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var state = new CacheMutationThreadState();
        var drawable = new CacheMutationThreadProbeDrawable(state);
        using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(8, 8));
        Renderer renderer = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var result = new Renderer(8, 8);
            result.Render(frame);
            return result;
        });

        try
        {
            Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False);

            renderer.ClearAllCaches();
            Assert.That(state.DisposedOnRenderThread, Is.EqualTo(new[] { true }));

            VulkanTestEnvironment.InvokeOnRenderThread(() => renderer.Render(frame));
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            Assert.Multiple(() =>
            {
                Assert.That(state.DisposedOnRenderThread, Is.EqualTo(new[] { true, true }));
                Assert.That(renderer.CacheOptions, Is.EqualTo(RenderCacheOptions.Disabled));
            });
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(renderer.Dispose);
        }
    }

    [Test]
    [NonParallelizable]
    [Category("GpuPassFusionGpu")]
    public void Detachment_FromCallerThread_DisposesCachedTreeOnRenderThread()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var state = new CacheMutationThreadState();
        var root = new CacheMutationHierarchyRoot();
        var drawable = new CacheMutationThreadProbeDrawable(state);
        root.Attach(drawable);
        using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(8, 8));
        Renderer renderer = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var result = new Renderer(8, 8);
            result.Render(frame);
            return result;
        });

        try
        {
            Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False);

            root.Detach(drawable);
            VulkanTestEnvironment.InvokeOnRenderThread(static () => { });

            Assert.That(state.DisposedOnRenderThread, Is.EqualTo(new[] { true }));
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(renderer.Dispose);
        }
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 8, 8),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 8, 8),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
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
                Assert.That(state.RecordCalls, Is.EqualTo(new[] { 1, 1 }),
                    "Rendering must record each drawable tree only once in the complete frame request.");
                Assert.That(state.FrameRecordCalls, Is.EqualTo(new[] { 1, 1 }));
                Assert.That(state.ExecutionOrder, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(state.Nodes, Has.All.Not.Null);
                Assert.That(state.Nodes, Has.All.Matches<ProductionTreeProbeNode>(
                    static node => !node.HasChanges && node.Cache.CanCache()),
                    "Successful complete-request execution must commit every tree's render-count/cache state.");
            });
        });
    }

    [Test]
    public void ProductionRenderer_LazilyCachesBoundariesForCurrentFrame()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var state = new RendererWideTreeState(2);
            var first = new RendererWideProbeDrawable(0, state);
            var second = new RendererWideProbeDrawable(1, state);
            using Drawable.Resource firstResource =
                (Drawable.Resource)first.ToResource(CompositionContext.Default);
            using Drawable.Resource secondResource =
                (Drawable.Resource)second.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(firstResource, secondResource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                diagnostics: null,
                surface: new CpuRenderTarget(8, 8))
            {
                CacheOptions = RenderCacheOptions.Disabled,
            };
            var expectedBounds = new Rect(0, 0, 8, 8);

            renderer.Render(frame);
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 1, 1 }));

            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 1 }));
            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 1 }),
                "A repeated single-drawable query must reuse the current-frame bounds.");

            Assert.That(renderer.GetBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 2 }));
            Assert.That(renderer.GetBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 2 }),
                "A repeated layer query must reuse every current-frame bound.");

            renderer.UpdateFrame(frame);
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 2 }));
            Assert.That(renderer.GetBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 3, 3 }),
                "Updating the current frame must invalidate every lazy bound.");

            renderer.Render(frame);
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 4, 4 }));
            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 5, 4 }),
                "A successful render must invalidate every lazy bound.");
            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 5, 4 }));

            Assert.That(renderer.RecalculateBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 6, 5 }),
                "Forced recalculation must record every matching drawable even when one bound is cached.");
            Assert.That(state.ExecutionOrder, Is.EqualTo(new[] { 0, 1, 0, 1 }));

            renderer.ClearAllCaches();
            Assert.Multiple(() =>
            {
                Assert.That(renderer.GetBoundaries(0), Is.Empty);
                Assert.That(renderer.GetBoundary(first), Is.Null);
                Assert.That(state.RecordCalls, Is.EqualTo(new[] { 6, 5 }),
                    "Clearing caches must not measure disposed current-frame entries.");
            });
        });
    }

    [Test]
    public void ProductionRenderer_UsesQueryBoundsForAFullTargetDrawable()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(new RectShape
            {
                Width = { CurrentValue = 3 },
                Height = { CurrentValue = 2 },
                AlignmentX = { CurrentValue = AlignmentX.Left },
                AlignmentY = { CurrentValue = AlignmentY.Top },
                Transform = { CurrentValue = new TranslateTransform(2, 1) },
                Fill = { CurrentValue = Brushes.White },
            });
            using Drawable.Resource resource =
                (Drawable.Resource)group.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                diagnostics: null,
                surface: new CpuRenderTarget(8, 8))
            {
                CacheOptions = RenderCacheOptions.Disabled,
            };

            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.GetBoundary(group), Is.EqualTo(new Rect(2, 1, 3, 2)));
                Assert.That(renderer.RecalculateBoundaries(0), Is.EqualTo(new[] { new Rect(2, 1, 3, 2) }));
            });
        });
    }

    [Test]
    public void BoundaryCollectionQueries_RequireRenderThreadAccess()
    {
        Renderer renderer = RenderThread.Dispatcher.Invoke(() => new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            diagnostics: null,
            surface: new CpuRenderTarget(8, 8)));
        try
        {
            Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False);
            Assert.Throws<InvalidOperationException>(() => renderer.GetBoundaries(0));
            Assert.Throws<InvalidOperationException>(() => renderer.RecalculateBoundaries(0));
        }
        finally
        {
            RenderThread.Dispatcher.Invoke(renderer.Dispose);
        }
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
                OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
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
            OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
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
                OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
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
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CacheMutationHierarchyRoot : Hierarchical, IHierarchicalRoot
    {
        public event EventHandler<IHierarchical>? DescendantAttached;

        public event EventHandler<IHierarchical>? DescendantDetached;

        public void Attach(IHierarchical child)
        {
            ((IModifiableHierarchical)this).AddChild(child);
        }

        public void Detach(IHierarchical child)
        {
            ((IModifiableHierarchical)this).RemoveChild(child);
        }

        public void OnDescendantAttached(IHierarchical descendant)
        {
            DescendantAttached?.Invoke(this, descendant);
        }

        public void OnDescendantDetached(IHierarchical descendant)
        {
            DescendantDetached?.Invoke(this, descendant);
        }
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

    public int[] FrameRecordCalls { get; } = new int[count];

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
        if (context.Purpose == RenderRequestPurpose.Frame)
        {
            state.FrameRecordCalls[index]++;
        }

        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            session =>
            {
                int completedFrameRecordCount = state.FrameRecordCalls[index];
                Assert.That(completedFrameRecordCount, Is.GreaterThan(0));
                Assert.That(state.FrameRecordCalls, Is.All.EqualTo(completedFrameRecordCount),
                    "Every top-level tree must be recorded before the first execution callback.");
                state.ExecutionOrder.Add(index);
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(
                    index == 0
                        ? new Color(160, 96, 32, 16)
                        : new Color(160, 16, 64, 128)));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: (typeof(ProductionTreeProbeNode), index),
            runtimeIdentity: new RenderRuntimeIdentity(index));
        context.Publish(context.OpaqueSource(description));
    }
}

internal sealed class CacheMutationThreadState
{
    public List<bool> DisposedOnRenderThread { get; } = [];
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class CacheMutationThreadProbeDrawable(
    CacheMutationThreadState state) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        context.DrawNode(new CacheMutationThreadProbeNode(state));
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(8, 8);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class CacheMutationThreadProbeNode(
    CacheMutationThreadState state) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(new Color(255, 32, 64, 96)));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: typeof(CacheMutationThreadProbeNode),
            runtimeIdentity: new RenderRuntimeIdentity(typeof(CacheMutationThreadProbeNode)));
        context.Publish(context.OpaqueSource(description));
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
            state.DisposedOnRenderThread.Add(RenderThread.Dispatcher.CheckAccess());
        base.OnDispose(disposing);
    }
}
