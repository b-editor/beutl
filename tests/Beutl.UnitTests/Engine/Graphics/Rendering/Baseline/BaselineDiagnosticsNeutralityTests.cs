using System.Collections.Immutable;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

[TestFixture]
public sealed class BaselineDiagnosticsNeutralityTests
{
    [Test]
    public void RendererFrame_InstrumentationPreservesOutputAndRecordedSequence()
    {
        FrameRun disabled = RunFrame(instrumentationEnabled: false);
        FrameRun enabled = RunFrame(instrumentationEnabled: true);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.Pixels, Is.EqualTo(disabled.Pixels));
            Assert.That(enabled.Trace, Is.EqualTo(disabled.Trace));
            Assert.That(disabled.Snapshot, Is.Null);
            Assert.That(enabled.Snapshot, Is.Not.Null);
            Assert.That(enabled.Snapshot!.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
            Assert.That(enabled.Snapshot.Succeeded, Is.True);
            Assert.That(enabled.Snapshot[RenderPipelineCounter.RecordedFragments], Is.GreaterThan(0));
            Assert.That(enabled.Snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.GreaterThan(0));
        });
    }

    [Test]
    public void Rasterize_InstrumentationPreservesOutputAllocationAndRecordedSequence()
    {
        RasterRun disabled = RunRasterize(instrumentationEnabled: false);
        RasterRun enabled = RunRasterize(instrumentationEnabled: true);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.Pixels, Is.EqualTo(disabled.Pixels));
            Assert.That(enabled.Allocations, Is.EqualTo(disabled.Allocations));
            Assert.That(enabled.Trace, Is.EqualTo(disabled.Trace));
            Assert.That(disabled.Snapshot, Is.Null);
            Assert.That(enabled.Snapshot, Is.Not.Null);
            Assert.That(enabled.Snapshot!.Purpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
            Assert.That(enabled.Snapshot.Succeeded, Is.True);
            Assert.That(enabled.Snapshot[RenderPipelineCounter.IntermediateAcquires], Is.GreaterThan(0));
            Assert.That(
                enabled.Snapshot[RenderPipelineCounter.IntermediateDischarges],
                Is.EqualTo(enabled.Snapshot[RenderPipelineCounter.IntermediateAcquires]));
            Assert.That(enabled.Snapshot[RenderPipelineCounter.IntermediateCreates], Is.GreaterThan(0));
        });
    }

    [Test]
    public void RenderFailure_InstrumentationPreservesExceptionAllocationAndCleanup()
    {
        FailureRun disabled = RunFailure(instrumentationEnabled: false, allocationFailure: false);
        FailureRun enabled = RunFailure(instrumentationEnabled: true, allocationFailure: false);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.ExceptionType, Is.Not.Null);
            Assert.That(enabled.ExceptionType, Is.EqualTo(disabled.ExceptionType));
            Assert.That(enabled.ExceptionMessage, Is.EqualTo(disabled.ExceptionMessage));
            Assert.That(enabled.Allocations, Is.EqualTo(disabled.Allocations));
            Assert.That(enabled.Trace, Is.EqualTo(disabled.Trace));
            Assert.That(disabled.Snapshot, Is.Null);
            Assert.That(enabled.Snapshot, Is.Not.Null);
            Assert.That(enabled.Snapshot!.Succeeded, Is.False);
            Assert.That(enabled.Snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Execution));
            Assert.That(enabled.Snapshot[RenderPipelineCounter.FailedOutcomes], Is.EqualTo(1));
            Assert.That(enabled.Snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
        });
    }

    [Test]
    public void FrameDiagnostics_DisposeFailureIsRecordedAsCleanup()
    {
        var state = new RenderPipelineDiagnosticsState();
        var trace = new List<string>();
        using var node = new FixedOpsNode(
            [new RecordedOperationSpec("dispose-fault", ThrowOnDispose: true)],
            trace);
        using var renderer = CreateRenderer(
            node,
            new TrackingTargetFactory(),
            diagnostics: state,
            purpose: RenderRequestPurpose.Frame);

        Assert.Throws<AggregateException>(() => renderer.Rasterize());
        RenderPipelineDiagnosticSnapshot snapshot = state.LatestFrame;

        Assert.Multiple(() =>
        {
            Assert.That(trace, Is.EqualTo(new[] { "dispose-fault" }));
            Assert.That(snapshot.Succeeded, Is.False);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Cleanup));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
        });
    }

    [Test]
    public void RendererConstructor_DisposesTransferredSurfaceOnFailure()
    {
        var target = new CpuRenderTarget(4, 4);

        AggregateException exception = Assert.Throws<AggregateException>(() => new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            diagnostics: null,
            surface: target))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(target.IsDisposed, Is.True);
        });
    }

    [Test]
    public void AllocationFailure_InstrumentationPreservesExceptionAttemptAndCleanup()
    {
        FailureRun disabled = RunFailure(instrumentationEnabled: false, allocationFailure: true);
        FailureRun enabled = RunFailure(instrumentationEnabled: true, allocationFailure: true);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.ExceptionType, Is.Not.Null);
            Assert.That(enabled.ExceptionType, Is.EqualTo(disabled.ExceptionType));
            Assert.That(enabled.ExceptionMessage, Is.EqualTo(disabled.ExceptionMessage));
            Assert.That(enabled.Allocations, Is.EqualTo(disabled.Allocations));
            Assert.That(enabled.Trace, Is.EqualTo(disabled.Trace));
            Assert.That(disabled.Snapshot, Is.Null);
            Assert.That(enabled.Snapshot, Is.Not.Null);
            Assert.That(enabled.Snapshot!.Succeeded, Is.False);
            Assert.That(enabled.Snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Allocation));
            Assert.That(enabled.Snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(enabled.Snapshot[RenderPipelineCounter.FailedOutcomes], Is.Zero,
                "Root-target acquisition fails before any fragment becomes the active failure subject.");
            Assert.That(
                enabled.Snapshot[RenderPipelineCounter.SkippedOutcomes],
                Is.EqualTo(enabled.Snapshot[RenderPipelineCounter.RecordedFragments]));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PersistentCache_InstrumentationPreservesDecisionAndOutput(bool cacheHit)
    {
        PullRun disabled = RunPull(instrumentationEnabled: false, cacheHit);
        PullRun enabled = RunPull(instrumentationEnabled: true, cacheHit);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.ProcessCalls, Is.EqualTo(disabled.ProcessCalls));
            Assert.That(enabled.OutputBounds, Is.EqualTo(disabled.OutputBounds));
            Assert.That(enabled.CacheCount, Is.EqualTo(disabled.CacheCount));
            Assert.That(enabled.Pixels, Is.EqualTo(disabled.Pixels));
            Assert.That(disabled.Snapshot, Is.Null);
            Assert.That(enabled.Snapshot, Is.Not.Null);
            Assert.That(
                enabled.Snapshot![cacheHit
                    ? RenderPipelineCounter.RenderCacheHits
                    : RenderPipelineCounter.RenderCacheMisses],
                Is.EqualTo(1));
            Assert.That(
                enabled.Snapshot[cacheHit
                    ? RenderPipelineCounter.CachedOutcomes
                    : RenderPipelineCounter.ExecutedOutcomes],
                Is.EqualTo(1));
        });
    }

    private static FrameRun RunFrame(bool instrumentationEnabled)
    {
        return RenderThread.Dispatcher.Invoke(() =>
        {
            var trace = new List<string>();
            var state = instrumentationEnabled ? new RenderPipelineDiagnosticsState() : null;
            var target = new CpuRenderTarget(8, 8);
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                diagnostics: state,
                surface: target);
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            var drawable = new global::Beutl.UnitTests.Engine.Graphics.Rendering.FaultingDrawable(
                [new RecordedOperationSpec("frame")])
            {
                Discharged = trace,
            };
            var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));

            renderer.Render(frame);
            using Bitmap bitmap = renderer.Snapshot();
            return new FrameRun(
                bitmap.GetPixelSpan().ToArray(),
                [.. trace],
                state?.LatestFrame);
        });
    }

    private static RasterRun RunRasterize(bool instrumentationEnabled)
    {
        var trace = new List<string>();
        var state = instrumentationEnabled ? new RenderPipelineDiagnosticsState() : null;
        using var node = new FixedOpsNode(
            () => [new RecordedOperationSpec("raster")],
            trace,
            () => trace.Add("process"));
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(node, factory, diagnostics: state);

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        return new RasterRun(
            rasterization.Bitmap!.GetPixelSpan().ToArray(),
            [.. factory.Allocations],
            [.. trace],
            state?.Latest);
    }

    private static FailureRun RunFailure(bool instrumentationEnabled, bool allocationFailure)
    {
        var trace = new List<string>();
        var state = instrumentationEnabled ? new RenderPipelineDiagnosticsState() : null;
        using var node = new FixedOpsNode(
            () => [new RecordedOperationSpec("render-fault", ThrowOnExecute: !allocationFailure)],
            trace,
            () => trace.Add("process"));
        var factory = new TrackingTargetFactory(throwOnAllocation: allocationFailure);
        using var renderer = CreateRenderer(node, factory, diagnostics: state);

        Exception? failure = null;
        try
        {
            using RenderNodeRasterization unexpected = renderer.Rasterize();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        return new FailureRun(
            failure?.GetType(),
            failure?.Message,
            [.. factory.Allocations],
            [.. trace],
            state?.Latest);
    }

    private static PullRun RunPull(bool instrumentationEnabled, bool cacheHit)
    {
        var trace = new List<string>();
        var state = instrumentationEnabled ? new RenderPipelineDiagnosticsState() : null;
        using var node = new FixedOpsNode(
            () => [new RecordedOperationSpec("pull")],
            trace,
            () => trace.Add("process"));
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            node,
            factory,
            useRenderCache: true,
            diagnostics: state,
            purpose: RenderRequestPurpose.Frame);
        if (cacheHit)
        {
            using RenderNodeRasterization warmup = renderer.Rasterize();
            state?.Reset();
        }

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        return new PullRun(
            node.ProcessCalls,
            rasterization.Bounds,
            node.Cache.CacheCount,
            rasterization.Bitmap!.GetPixelSpan().ToArray(),
            state?.LatestFrame);
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode root,
        IRenderTargetFactory? targetFactory,
        bool useRenderCache = false,
        IRenderPipelineDiagnosticsState? diagnostics = null,
        RenderRequestPurpose purpose = RenderRequestPurpose.Auxiliary)
        => new(root, new RenderNodeRendererOptions
        {
            OutputScale = 1,
            MaxWorkingScale = float.PositiveInfinity,
            UseRenderCache = useRenderCache,
            TargetFactory = targetFactory,
            RenderPurpose = purpose,
            Diagnostics = diagnostics,
        });

    private sealed class TrackingTargetFactory(bool throwOnAllocation = false) : IRenderTargetFactory
    {
        public List<string> Allocations { get; } = [];

        public RenderTarget Create(PixelSize deviceSize)
        {
            Allocations.Add($"{deviceSize.Width}x{deviceSize.Height}");
            if (throwOnAllocation)
                throw new InvalidOperationException("allocation-fault");

            return new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height)
    {
        private static SKSurface CreateSurface(int width, int height)
        {
            return SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create a CPU render target.");
        }
    }

    private sealed record FrameRun(
        byte[] Pixels,
        string[] Trace,
        RenderPipelineDiagnosticSnapshot? Snapshot);

    private sealed record RasterRun(
        byte[] Pixels,
        string[] Allocations,
        string[] Trace,
        RenderPipelineDiagnosticSnapshot? Snapshot);

    private sealed record FailureRun(
        Type? ExceptionType,
        string? ExceptionMessage,
        string[] Allocations,
        string[] Trace,
        RenderPipelineDiagnosticSnapshot? Snapshot);

    private sealed record PullRun(
        int ProcessCalls,
        Rect OutputBounds,
        int CacheCount,
        byte[] Pixels,
        RenderPipelineDiagnosticSnapshot? Snapshot);
}
