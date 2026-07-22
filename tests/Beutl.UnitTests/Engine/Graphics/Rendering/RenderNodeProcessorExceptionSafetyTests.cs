using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RenderNodeRendererExceptionSafetyTests
{
    public enum EntryPoint
    {
        Rasterize,
        Render,
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void DischargesFaultingAndUnexecutedResources_WhenExecutionThrows(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec("fault", ThrowOnExecute: true),
            new RecordedOperationSpec("remaining"));
        using var renderer = CreateRenderer(node);

        var ex = Assert.Throws<InvalidOperationException>(() => Execute(entryPoint, renderer));

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        Assert.That(discharged, Is.EqualTo(new[] { "remaining", "fault", "first" }));
    }

    [Test]
    public void CleanupOnlyFailure_DischargesEveryResourceExactlyOnceAndSurfacesFailure()
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec("fault", ThrowOnDispose: true),
            new RecordedOperationSpec("remaining"));
        using var target = CreateCpuTarget(4, 4);
        using var canvas = new ImmediateCanvas(target);

        var ex = Assert.Throws<AggregateException>(() => ExecuteRequestAndSurfaceOwnerFailure(node, canvas));

        Assert.That(ex!.InnerExceptions.Single().Message, Is.EqualTo("fault"));
        Assert.That(discharged, Is.EqualTo(new[] { "remaining", "fault", "first" }));
    }

    [Test]
    public void Measure_CleanupOnlyFailure_DischargesEveryResourceExactlyOnceAndSurfacesFailure()
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first", TrackMetadataDischarge: true),
            new RecordedOperationSpec("fault", ThrowOnDispose: true, TrackMetadataDischarge: true),
            new RecordedOperationSpec("remaining", TrackMetadataDischarge: true));
        using var renderer = CreateRenderer(node);

        var ex = Assert.Throws<AggregateException>(() => renderer.Measure());

        Assert.That(ex!.Flatten().InnerExceptions.Single().Message, Is.EqualTo("fault"));
        Assert.That(discharged, Is.EqualTo(new[] { "remaining", "fault", "first" }));
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void PooledTargetDisposeFailure_SurfacesAtRendererDisposalAfterSuccessfulRequest(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec("second"));
        int throwingTarget = entryPoint == EntryPoint.Rasterize ? 1 : 0;
        var factory = new TrackingTargetFactory(index => index == throwingTarget);
        var renderer = CreateRenderer(node, factory);

        Assert.DoesNotThrow(() => Execute(entryPoint, renderer));
        Assert.That(factory.CreatedTargets, Has.All.Property(nameof(FakeRenderTarget.DisposeWasCalled)).False);
        var ex = Assert.Throws<InvalidOperationException>(renderer.Dispose);

        Assert.That(ex!.Message, Is.EqualTo("rt-dispose-fault"));
        Assert.That(factory.CreatedTargets, Has.Count.EqualTo(entryPoint == EntryPoint.Rasterize ? 2 : 1));
        Assert.That(factory.CreatedTargets, Has.All.Property(nameof(FakeRenderTarget.DisposeWasCalled)).True);
        Assert.That(discharged, Is.EqualTo(new[] { "second", "first" }));
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void ContinuesCleanupAndPreservesOriginalException_WhenRemainingResourcesThrow(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec("render-fault", ThrowOnExecute: true),
            new RecordedOperationSpec("throwing-remaining-1", ThrowOnDispose: true),
            new RecordedOperationSpec("throwing-remaining-2", ThrowOnDispose: true),
            new RecordedOperationSpec("remaining"));
        using var renderer = CreateRenderer(node);

        var ex = Assert.Throws<InvalidOperationException>(() => Execute(entryPoint, renderer));

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(discharged, Is.EqualTo(new[]
        {
            "remaining",
            "throwing-remaining-2",
            "throwing-remaining-1",
            "render-fault",
            "first",
        }));
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void PreservesExecutionException_WhenFaultingResourceAlsoThrowsOnDispose(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec(
                "render-fault",
                ThrowOnExecute: true,
                ThrowOnDispose: true,
                DisposeFaultMessage: "dispose-fault"),
            new RecordedOperationSpec("remaining"));
        using var renderer = CreateRenderer(node);

        var ex = Assert.Throws<InvalidOperationException>(() => Execute(entryPoint, renderer));

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(discharged, Is.EqualTo(new[] { "remaining", "render-fault", "first" }));
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void PreservesExecutionException_WhilePooledTargetFailureWaitsForRendererDisposal(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("render-fault", ThrowOnExecute: true, AllocateBeforeThrow: true));
        int throwingTarget = entryPoint == EntryPoint.Rasterize ? 1 : 0;
        var factory = new TrackingTargetFactory(index => index == throwingTarget);
        var renderer = CreateRenderer(node, factory);

        var ex = Assert.Throws<InvalidOperationException>(() => Execute(entryPoint, renderer));

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(factory.CreatedTargets, Has.Count.EqualTo(entryPoint == EntryPoint.Rasterize ? 2 : 1));
        Assert.That(factory.CreatedTargets, Has.All.Property(nameof(FakeRenderTarget.DisposeWasCalled)).False);
        Assert.That(discharged, Is.EqualTo(new[] { "render-fault" }));
        var cleanup = Assert.Throws<InvalidOperationException>(renderer.Dispose);
        Assert.That(cleanup!.Message, Is.EqualTo("rt-dispose-fault"));
        Assert.That(factory.CreatedTargets, Has.All.Property(nameof(FakeRenderTarget.DisposeWasCalled)).True);
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void DischargesRecordedResources_WhenTargetFactoryThrows(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec("second"));
        var factory = new TrackingTargetFactory(_ => false, throwOnCreate: true);
        using var renderer = CreateRenderer(node, factory);

        var ex = Assert.Throws<InvalidOperationException>(() => Execute(entryPoint, renderer));

        Assert.That(ex!.Message, Is.EqualTo("rt-create-fault"));
        Assert.That(discharged, Is.EqualTo(new[] { "second", "first" }));
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void DischargesRecordedResources_WhenTargetFactoryReturnsNull(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec("second"));
        var factory = new TrackingTargetFactory(_ => false, returnNullOnCreate: true);
        using var renderer = CreateRenderer(node, factory);

        var ex = Assert.Throws<InvalidOperationException>(() => Execute(entryPoint, renderer));

        Assert.That(
            ex!.Message,
            Does.StartWith("The render-target factory could not allocate 4x4 pixels"));
        Assert.That(discharged, Is.EqualTo(new[] { "second", "first" }));
    }

    [TestCase(EntryPoint.Rasterize)]
    [TestCase(EntryPoint.Render)]
    public void PreservesAllocationFailure_WhenResourceCleanupAlsoThrows(EntryPoint entryPoint)
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first", ThrowOnDispose: true),
            new RecordedOperationSpec("second"));
        var factory = new TrackingTargetFactory(_ => false, returnNullOnCreate: true);
        using var renderer = CreateRenderer(node, factory);

        var ex = Assert.Throws<InvalidOperationException>(() => Execute(entryPoint, renderer));

        Assert.That(
            ex!.Message,
            Does.StartWith("The render-target factory could not allocate 4x4 pixels"));
        Assert.That(discharged, Is.EqualTo(new[] { "second", "first" }));
    }

    [Test]
    public void RequestOwner_CleanupContinuesAfterFaultAndPreservesStrictLifo()
    {
        var discharged = new List<string>();
        using var owner = new RenderRequestOwner();
        Register(owner, new RecordedOperation(new RecordedOperationSpec("first"), discharged, true));
        Register(owner, new RecordedOperation(
            new RecordedOperationSpec("throws", ThrowOnDispose: true),
            discharged,
            true));
        Register(owner, new RecordedOperation(new RecordedOperationSpec("remaining"), discharged, true));

        owner.Cleanup();

        Assert.That(discharged, Is.EqualTo(new[] { "remaining", "throws", "first" }));
        Assert.That(owner.CleanupFailures.Length, Is.EqualTo(1));
        Assert.That(owner.PrimaryFailure!.SourceException, Is.TypeOf<AggregateException>());
    }

    [Test]
    public void Diagnostics_ClassifyResourceDisposeFaultAsCleanup()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        RenderPipelineDiagnosticRecorder recorder = RenderPipelineDiagnosticRecorder.Start(
            diagnostics,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            nameof(RenderNodeRenderer))!;
        long[] subjects = recorder.RecordFragments(2, RenderPipelineOutcome.Executed);
        var discharged = new List<string>();
        using var owner = new RenderRequestOwner();
        Register(owner, new RecordedOperation(new RecordedOperationSpec("remaining"), discharged, true));
        Register(owner, new RecordedOperation(
            new RecordedOperationSpec("fault", ThrowOnDispose: true),
            discharged,
            true));

        owner.Cleanup();
        recorder.RecordCleanupFailure(subjects[0]);
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Cleanup));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.FailedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.SkippedOutcomes], Is.EqualTo(1));
            Assert.That(
                snapshot.Events.Single(item => item.Kind == RenderPipelineDiagnosticEventKind.Failure).SubjectId,
                Is.EqualTo(subjects[0]));
        });
    }

    [Test]
    public void Rasterize_ReturnsBuiltTargetsToPoolAndPreservesExecutionFailure()
    {
        var discharged = new List<string>();
        using var node = CreateNode(
            discharged,
            new RecordedOperationSpec("first"),
            new RecordedOperationSpec("second"),
            new RecordedOperationSpec("render-fault", ThrowOnExecute: true, AllocateBeforeThrow: true));
        var factory = new TrackingTargetFactory(index => index == 1);
        var renderer = CreateRenderer(node, factory);

        var ex = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(factory.CreatedTargets, Has.Count.EqualTo(2));
        Assert.That(factory.CreatedTargets, Has.All.Property(nameof(FakeRenderTarget.DisposeWasCalled)).False);
        Assert.That(discharged, Is.EqualTo(new[] { "render-fault", "second", "first" }));
        var cleanup = Assert.Throws<InvalidOperationException>(renderer.Dispose);
        Assert.That(cleanup!.Message, Is.EqualTo("rt-dispose-fault"));
        Assert.That(factory.CreatedTargets, Has.All.Property(nameof(FakeRenderTarget.DisposeWasCalled)).True);
    }

    [Test]
    public void Rasterize_SurfacesPooledRootTargetDisposeFailureAtRendererDisposal()
    {
        var discharged = new List<string>();
        using var node = CreateNode(discharged, new RecordedOperationSpec("ok"));
        var factory = new TrackingTargetFactory(index => index == 0);
        var renderer = CreateRenderer(node, factory);

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Assert.That(factory.CreatedTargets, Has.All.Property(nameof(FakeRenderTarget.DisposeWasCalled)).False);
        var ex = Assert.Throws<InvalidOperationException>(renderer.Dispose);

        Assert.That(ex!.Message, Is.EqualTo("rt-dispose-fault"));
        Assert.That(factory.CreatedTargets, Has.Count.EqualTo(2));
        Assert.That(factory.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(factory.CreatedTargets[1].DisposeWasCalled, Is.True);
        Assert.That(discharged, Is.EqualTo(new[] { "ok" }));
    }

    [Test]
    public void Diagnostics_ReconcileCleanupFaultAndIntermediateDischarge()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        RenderPipelineDiagnosticRecorder recorder = RenderPipelineDiagnosticRecorder.Start(
            diagnostics,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            nameof(RenderNodeRenderer))!;
        long subject = recorder.RecordFragments(1, RenderPipelineOutcome.Executed).Single();

        recorder.RecordIntermediateCreated();
        recorder.RecordOutcome(subject, RenderPipelineOutcome.Executed);
        recorder.RecordIntermediateDischarged();
        recorder.RecordCleanupFailure();
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Succeeded, Is.False);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Cleanup));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateDischarges], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
        });
    }

    [Test]
    public void Diagnostics_AttributeRequestLevelOutputFailureWithoutReplacingExecutedOutcome()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        RenderPipelineDiagnosticRecorder recorder = RenderPipelineDiagnosticRecorder.Start(
            diagnostics,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            nameof(RenderNodeRenderer))!;
        long subject = recorder.RecordFragments(1, RenderPipelineOutcome.Executed).Single();

        recorder.RecordOutcome(subject, RenderPipelineOutcome.Executed);
        recorder.RecordFailure(RenderPipelineFailurePhase.Execution);
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Succeeded, Is.False);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Execution));
            Assert.That(
                snapshot.Events.Single(item => item.Kind == RenderPipelineDiagnosticEventKind.Failure).SubjectId,
                Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.FailedOutcomes], Is.Zero);
        });
    }

    private static FixedOpsNode CreateNode(
        ICollection<string> discharged,
        params RecordedOperationSpec[] operations)
        => new(operations, discharged);

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        IRenderTargetFactory? targetFactory = null)
        => new(node, new RenderNodeRendererOptions
        {
            OutputScale = 1,
            MaxWorkingScale = float.PositiveInfinity,
            UseRenderCache = false,
            TargetFactory = targetFactory,
        });

    private static void Execute(EntryPoint entryPoint, RenderNodeRenderer renderer)
    {
        if (entryPoint == EntryPoint.Rasterize)
        {
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            return;
        }

        using RenderTarget target = CreateCpuTarget(4, 4);
        using var canvas = new ImmediateCanvas(target);
        renderer.Render(canvas);
    }

    private static void ExecuteRequestAndSurfaceOwnerFailure(RenderNode node, ImmediateCanvas destination)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: destination.Density,
            maxWorkingScale: destination.MaxWorkingScale,
            cachePolicy: Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled));
        RenderRequestOwner owner = request.Options.Owner;
        CompiledRenderRequest? compiled = null;
        Exception? executionFailure = null;
        using var targetRegistry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = targetRegistry.BeginSession(
            RenderIntent.Preview,
            destination._renderTarget);
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            compiled = new RenderRequestCompiler().Compile(request, graph);
            new RenderRequestExecutor(targets).Execute(compiled, destination);
        }
        catch (Exception ex)
        {
            executionFailure = ex;
        }
        finally
        {
            if (compiled is not null)
                compiled.Dispose();
            else
                request.Dispose();
            targets.Dispose();
        }

        owner.ThrowIfFailed();
        targets.ThrowIfCleanupFailed();
        if (executionFailure is not null)
            throw executionFailure;
    }

    private static void Register(RenderRequestOwner owner, RecordedOperation operation)
    {
        RenderResource<RecordedOperation> resource = owner.ResourceRegistry.RegisterOwned(operation);
        owner.ResourceRegistry.Commit(resource);
    }

    private static RenderTarget CreateCpuTarget(int width, int height)
        => new FakeRenderTarget(width, height, throwOnDispose: false);

    private sealed class TrackingTargetFactory(
        Func<int, bool> shouldThrowOnDispose,
        bool throwOnCreate = false,
        bool returnNullOnCreate = false) : IRenderTargetFactory
    {
        public List<FakeRenderTarget> CreatedTargets { get; } = [];

        public RenderTarget? Create(PixelSize deviceSize)
        {
            if (throwOnCreate)
                throw new InvalidOperationException("rt-create-fault");
            if (returnNullOnCreate)
                return null;

            var target = new FakeRenderTarget(
                deviceSize.Width,
                deviceSize.Height,
                shouldThrowOnDispose(CreatedTargets.Count));
            CreatedTargets.Add(target);
            return target;
        }
    }

    private sealed class FakeRenderTarget(int width, int height, bool throwOnDispose)
        : RenderTarget(CreateReadbackSurface(width, height), width, height)
    {
        public bool DisposeWasCalled { get; private set; }

        private static SKSurface CreateReadbackSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear()));

        protected override void Dispose(bool disposing)
        {
            bool shouldThrow = disposing && throwOnDispose;
            if (disposing)
                DisposeWasCalled = true;

            base.Dispose(disposing);
            if (shouldThrow)
                throw new InvalidOperationException("rt-dispose-fault");
        }
    }
}
