using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RecordingAndPlanningFailureTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void RecordingFailure_RollsBackOwnedResourcesAndInvalidatesEveryFacadeBeforeAllocation()
    {
        var resource = new FailureTestDisposable();
        var failure = new InvalidOperationException("recording-primary");
        using var node = new RecordingFailureNode(resource, failure);
        var factory = new FailureTestTargetFactory();
        using var renderer = FailureTestSupport.CreateRenderer(node, factory, useRenderCache: false);

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() => renderer.Measure());

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(resource.DisposeCalls, Is.EqualTo(1));
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(node.RetainedContext, Is.Not.Null);
            Assert.That(node.RetainedHandle, Is.Not.Null);
            Assert.That(() => _ = node.RetainedContext!.Inputs, Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => node.RetainedHandle!.TryGetMetadata(out _),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [TestCase(ResourceConflict.DuplicateOwn)]
    [TestCase(ResourceConflict.OwnThenBorrow)]
    [TestCase(ResourceConflict.BorrowThenOwn)]
    [TestCase(ResourceConflict.BorrowKey)]
    [TestCase(ResourceConflict.BorrowVersion)]
    public void RecordingOwnershipConflict_FailsAtomicallyThroughTheProductionRecorder(ResourceConflict conflict)
    {
        var resource = new FailureTestDisposable();
        using var node = new ResourceConflictNode(resource, conflict);
        var factory = new FailureTestTargetFactory();
        using var renderer = FailureTestSupport.CreateRenderer(node, factory, useRenderCache: false);

        Assert.That(() => renderer.Measure(), Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(
                resource.DisposeCalls,
                Is.EqualTo(conflict is ResourceConflict.DuplicateOwn or ResourceConflict.OwnThenBorrow ? 1 : 0));
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void ApplyToCheckpoint_RestoresItemsBoundsAndResourcesWithoutReplacingThePrimaryFailure()
    {
        var resource = new FailureTestDisposable();
        var failure = new InvalidOperationException("apply-primary");
        using var context = new FilterEffectContext(s_bounds);
        context.Shader(ShaderDescription.CurrentPixel("half4 apply(half4 color) { return color; }"));
        Rect checkpointBounds = context.Bounds;
        int checkpointItems = context.CountItems();

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() =>
            context.ApplyTransactional(() =>
            {
                _ = context.Own(resource, "apply-owned", 1);
                context.Geometry(GeometryDescription.Create(
                    static _ => { },
                    RenderBoundsContract.Create(
                        static bounds => bounds.Inflate(new Thickness(3)),
                        static bounds => bounds.Inflate(new Thickness(3)),
                        "apply-geometry-bounds"),
                    RenderHitTestContract.AnyInput,
                    structuralKey: "apply-geometry"));
                throw failure;
            }));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(context.Bounds, Is.EqualTo(checkpointBounds));
            Assert.That(context.CountItems(), Is.EqualTo(checkpointItems));
            Assert.That(resource.DisposeCalls, Is.EqualTo(1));
        });
    }

    [TestCase(BoundsFailure.Forward)]
    [TestCase(BoundsFailure.BackwardRoi)]
    public void BoundsAndRoiMappingFailure_IsPlanningAtomicAndNeverExecutesOrAllocates(BoundsFailure failurePoint)
    {
        using var node = new BoundsFailureNode(failurePoint);
        var factory = new FailureTestTargetFactory();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                RequestedRegion = new Rect(2, 2, 2, 2),
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
                TargetFactory = factory,
            });

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Is.EqualTo(
                failurePoint == BoundsFailure.Forward ? "forward-bounds-failure" : "backward-roi-failure"));
            Assert.That(node.ExecuteCalls, Is.Zero);
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [TestCase(RecordingRecursion.DirectRecordNode)]
    [TestCase(RecordingRecursion.DirectRecordSubtree)]
    [TestCase(RecordingRecursion.IndirectRecordNode)]
    [TestCase(RecordingRecursion.SeparateTarget)]
    public void EveryRecordingRecursionShape_FailsWithAPathBeforeAllocation(RecordingRecursion shape)
    {
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner);
        var resource = new FailureTestDisposable();
        using var node = RecursionNode.Create(shape, options, resource);
        using var request = new RenderRequest(options);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestRecorder(request).Record(node));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("cycle"));
            Assert.That(failure.Message, Does.Contain("->"));
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(failure));
            Assert.That(owner.SecondaryFailures, Is.Empty,
                "One recording failure rethrown through nested catch boundaries is not a secondary failure.");
            Assert.That(owner.CleanupFailures, Is.Empty);
            Assert.That(resource.DisposeCalls, Is.EqualTo(1));
            Assert.That(
                () => node.RetainedContext!.DisableRenderCache(),
                Throws.InvalidOperationException,
                "The failed parent recording context must be invalidated after rollback.");
        });
    }

    [Test]
    public void CacheLookupFailure_RemainsTheCompilerPrimaryAndCleansTheRecordedRequest()
    {
        using var node = new CacheableSourceNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var request = FailureTestSupport.CreateFrameRequest(useRenderCache: true);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var compiler = new RenderRequestCompiler(
            renderCacheContext: FailureTestSupport.CacheResolutionContext,
            renderCacheLookup: new ThrowingCacheLookup("cache-lookup-failure"));

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => compiler.Compile(request, graph));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Is.EqualTo("cache-lookup-failure"));
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(node.ExecuteCalls, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void CacheHitWithInvalidSubstitutionPayload_FailsExecutionWithoutPublishingAnything()
    {
        using var node = new CacheableSourceNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var request = FailureTestSupport.CreateFrameRequest(useRenderCache: true);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var compiler = new RenderRequestCompiler(
            renderCacheContext: FailureTestSupport.CacheResolutionContext,
            renderCacheLookup: new InvalidPayloadCacheLookup());
        using CompiledRenderRequest compiled = compiler.Compile(request, graph);
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget(8, 8);
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("node-cache output payload"));
            Assert.That(node.ExecuteCalls, Is.Zero, "A hit substitution must not execute its producer.");
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void CachePublicationFailure_RejectsTheStagedCaptureAndReturnsEveryLease()
    {
        using var node = new CacheableSourceNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var request = FailureTestSupport.CreateFrameRequest(useRenderCache: true);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
                renderCacheContext: FailureTestSupport.CacheResolutionContext,
                renderCacheLookup: RenderNodeCacheLookup.Instance)
            .Compile(request, graph);
        node.Cache.Dispose();
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget(8, 8);
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        Assert.That(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas),
            Throws.TypeOf<ObjectDisposedException>());

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCalls, Is.EqualTo(1));
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    private static Rect ThrowForward(Rect _)
        => throw new InvalidOperationException("forward-bounds-failure");

    private static Rect ThrowBackward(Rect _)
        => throw new InvalidOperationException("backward-roi-failure");

    public enum ResourceConflict
    {
        DuplicateOwn,
        OwnThenBorrow,
        BorrowThenOwn,
        BorrowKey,
        BorrowVersion,
    }

    public enum BoundsFailure
    {
        Forward,
        BackwardRoi,
    }

    public enum RecordingRecursion
    {
        DirectRecordNode,
        DirectRecordSubtree,
        IndirectRecordNode,
        SeparateTarget,
    }

    private sealed class RecordingFailureNode(
        FailureTestDisposable resource,
        InvalidOperationException failure) : RenderNode
    {
        public RenderNodeContext? RetainedContext { get; private set; }

        public RenderFragmentHandle? RetainedHandle { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RetainedContext = context;
            _ = context.Own(resource, "recording-owned", 1);
            RetainedHandle = context.OpaqueSource(FailureTestSupport.SourceDescription());
            context.Publish(RetainedHandle);
            throw failure;
        }
    }

    private sealed class ResourceConflictNode(
        FailureTestDisposable resource,
        ResourceConflict conflict) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            switch (conflict)
            {
                case ResourceConflict.DuplicateOwn:
                    _ = context.Own(resource, "owned", 0);
                    _ = context.Own(resource, "owned", 0);
                    break;
                case ResourceConflict.OwnThenBorrow:
                    _ = context.Own(resource, "owned", 0);
                    _ = context.Borrow(resource, "borrowed", 0);
                    break;
                case ResourceConflict.BorrowThenOwn:
                    _ = context.Borrow(resource, "borrowed", 0);
                    _ = context.Own(resource, "owned", 0);
                    break;
                case ResourceConflict.BorrowKey:
                    _ = context.Borrow(resource, "first", 0);
                    _ = context.Borrow(resource, "second", 0);
                    break;
                case ResourceConflict.BorrowVersion:
                    _ = context.Borrow(resource, "borrowed", 0);
                    _ = context.Borrow(resource, "borrowed", 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private sealed class BoundsFailureNode(BoundsFailure failurePoint) : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(FailureTestSupport.SourceDescription(
                _ => ExecuteCalls++,
                structuralKey: "bounds-failure-source"));
            RenderBoundsContract bounds = failurePoint == BoundsFailure.Forward
                ? RenderBoundsContract.Create(ThrowForward, static value => value, "throw-forward")
                : RenderBoundsContract.Create(static value => value, ThrowBackward, "throw-backward");
            GeometryDescription geometry = GeometryDescription.Create(
                static _ => { },
                bounds,
                RenderHitTestContract.AnyInput,
                structuralKey: "bounds-failure-geometry");
            context.Publish(context.Geometry(source, geometry));
        }
    }

    private sealed class RecursionNode : RenderNode
    {
        private readonly RecordingRecursion _shape;
        private readonly RenderRequestOptions _options;
        private readonly FailureTestDisposable? _resource;
        private RecursionNode? _other;

        private RecursionNode(
            RecordingRecursion shape,
            RenderRequestOptions options,
            FailureTestDisposable? resource = null)
        {
            _shape = shape;
            _options = options;
            _resource = resource;
        }

        public RenderNodeContext? RetainedContext { get; private set; }

        public static RecursionNode Create(
            RecordingRecursion shape,
            RenderRequestOptions options,
            FailureTestDisposable? resource = null)
        {
            var result = new RecursionNode(shape, options, resource);
            if (shape == RecordingRecursion.IndirectRecordNode)
            {
                result._other = new RecursionNode(shape, options) { _other = result };
            }

            return result;
        }

        public override void Process(RenderNodeContext context)
        {
            RetainedContext = context;
            if (_resource is not null)
                _ = context.Own(_resource, "recursion-owned", 1);

            switch (_shape)
            {
                case RecordingRecursion.DirectRecordNode:
                    _ = context.RecordNode(this, []);
                    break;
                case RecordingRecursion.DirectRecordSubtree:
                    _ = context.RecordSubtree(this);
                    break;
                case RecordingRecursion.IndirectRecordNode:
                    _ = context.RecordNode(_other!, []);
                    break;
                case RecordingRecursion.SeparateTarget:
                    _ = context.RecordNestedTarget(this, s_bounds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void OnDispose(bool disposing)
        {
            RecursionNode? other = Interlocked.Exchange(ref _other, null);
            if (other is not null)
            {
                other._other = null;
                other.Dispose();
            }
        }
    }

    private sealed class CacheableSourceNode : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    ExecuteCalls++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                structuralKey: "cacheable-failure-source")));
        }
    }

    private sealed class ThrowingCacheLookup(string message) : IRenderCacheLookup
    {
        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? entry)
            => throw new InvalidOperationException(message);
    }

    private sealed class InvalidPayloadCacheLookup : IRenderCacheLookup
    {
        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? entry)
        {
            entry = new RenderCacheEntry(identity, new object());
            return true;
        }
    }
}

internal static class FailureTestSupport
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    public static RenderCacheResolutionContext CacheResolutionContext { get; } = new(
        RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
        new RenderCacheDeviceContextIdentity("failure-device", "failure-context"),
        allowPersistentLookup: true,
        allowCapturePublication: true);

    public static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        IRenderTargetFactory? factory = null,
        bool useRenderCache = false,
        RenderRequestPurpose purpose = RenderRequestPurpose.Auxiliary,
        IRenderPipelineDiagnosticsState? diagnostics = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = useRenderCache,
                TargetFactory = factory,
                RenderPurpose = purpose,
                Diagnostics = diagnostics,
            });

    public static RenderRequest CreateFrameRequest(bool useRenderCache)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: useRenderCache ? RenderCacheOptions.Default : RenderCacheOptions.Disabled));

    public static OpaqueRenderDescription SourceDescription(
        Action<OpaqueRenderSession>? execute = null,
        object? structuralKey = null,
        RenderValueCardinality? cardinality = null,
        RenderBackendBoundary backendBoundary = RenderBackendBoundary.None)
    {
        execute ??= static session =>
        {
            using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
            output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
            session.Publish(output);
        };

        return backendBoundary == RenderBackendBoundary.None
            ? OpaqueRenderDescription.Create(
                execute,
                RenderOperationBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                cardinality ?? RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: structuralKey ?? "failure-test-source",
                runtimeIdentity: new RenderRuntimeIdentity(structuralKey ?? "failure-test-source-runtime"))
            : OpaqueRenderDescription.CreateBackendBoundary(
                backendBoundary,
                execute,
                RenderOperationBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                cardinality ?? RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey ?? "failure-test-backend-source",
                new RenderRuntimeIdentity(structuralKey ?? "failure-test-backend-runtime"));
    }

    public static RenderTarget CreateCpuTarget(int width = 8, int height = 8)
        => new FailureTestRenderTarget(new PixelSize(width, height));
}

internal sealed class FailureTestDisposable(Exception? failure = null) : IDisposable
{
    public int DisposeCalls { get; private set; }

    public void Dispose()
    {
        DisposeCalls++;
        if (failure is not null)
            throw failure;
    }
}

internal sealed class FailureTestTargetFactory(
    int? failAt = null,
    Exception? createFailure = null,
    Func<int, Exception?>? disposeFailure = null) : IRenderTargetFactory
{
    public List<FailureTestRenderTarget> Targets { get; } = [];

    public int CreateCalls { get; private set; }

    public RenderTarget? Create(PixelSize deviceSize)
    {
        int index = CreateCalls++;
        if (createFailure is not null)
            throw createFailure;
        if (index == failAt)
            return null;

        var target = new FailureTestRenderTarget(deviceSize, disposeFailure?.Invoke(index));
        Targets.Add(target);
        return target;
    }
}

internal sealed class FailureTestRenderTarget : RenderTarget
{
    private readonly Exception? _disposeFailure;

    public FailureTestRenderTarget(PixelSize size, Exception? disposeFailure = null)
        : base(
            SKSurface.Create(new SKImageInfo(
                size.Width,
                size.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            size.Width,
            size.Height)
    {
        _disposeFailure = disposeFailure;
    }

    public int DisposeCalls { get; private set; }

    protected override void Dispose(bool disposing)
    {
        bool shouldThrow = disposing && !IsDisposed && _disposeFailure is not null;
        if (disposing && !IsDisposed)
            DisposeCalls++;
        base.Dispose(disposing);
        if (shouldThrow)
            throw _disposeFailure!;
    }
}
