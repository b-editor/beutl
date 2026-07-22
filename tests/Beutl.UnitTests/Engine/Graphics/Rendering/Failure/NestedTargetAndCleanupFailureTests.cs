using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class NestedTargetAndCleanupFailureTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [TestCase(TargetCallbackFailure.CommandCallback)]
    [TestCase(TargetCallbackFailure.UndeclaredTargetReadback)]
    [TestCase(TargetCallbackFailure.MissingTargetReadback)]
    [TestCase(TargetCallbackFailure.UndeclaredInputReadback)]
    [TestCase(TargetCallbackFailure.DuplicateInputReadback)]
    [TestCase(TargetCallbackFailure.ScopeCallback)]
    [TestCase(TargetCallbackFailure.ScopeMissingReplay)]
    [TestCase(TargetCallbackFailure.ScopeDoubleReplay)]
    [TestCase(TargetCallbackFailure.RawCommandCallback)]
    [TestCase(TargetCallbackFailure.RawScopeCallback)]
    [TestCase(TargetCallbackFailure.RawScopeMissingReplay)]
    [TestCase(TargetCallbackFailure.RawScopeDoubleReplay)]
    public void TargetCommandScopeAndRawFailures_DischargeAllStateAndSealTheSession(
        TargetCallbackFailure failurePoint)
    {
        using var node = new TargetCallbackFailureNode(failurePoint);
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        Exception? failure = Assert.Catch(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(node.CallbackEntries, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(node.VerifyRetainedSession is not null, Is.True);
        });
        Action verifyRetainedSession = node.VerifyRetainedSession
            ?? throw new AssertionException("The deferred callback did not expose its retained-session probe.");
        Assert.That(verifyRetainedSession, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void TargetCaptureAllocationFailure_DischargesTheRootAndPublishesNoCapture()
    {
        using var node = new TargetCaptureNode();
        var factory = new FailureTestTargetFactory(failAt: 1);
        using var renderer = FailureTestSupport.CreateRenderer(node, factory, useRenderCache: false);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("could not allocate"));
            Assert.That(factory.CreateCalls, Is.EqualTo(2));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void Graphics3DBackendBoundaryFailure_RemainsPrimaryAndPublishesNoOutput()
    {
        var primary = new InvalidOperationException("graphics3d-backend-primary");
        using var node = new BackendBoundaryFailureNode(primary);
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(node.ExecuteCalls, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void NestedChildRegionAnalysisFailure_FailsTheFamilyBeforeAllocationAndPreservesThePrimary()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        var snapshots = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += snapshots.Add;
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Default,
            owner: owner,
            diagnostics: diagnostics);
        using var child = new NestedRegionAnalysisFailureNode();
        using var parent = new NestedPlanningFailureParentNode(child);
        child.Cache.ReportRenderCount(RenderNodeCache.Count);
        parent.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderRequest nested = graph.NestedRequests.Single();
        var factory = new FailureTestTargetFactory();
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget();
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
            new RenderRequestExecutor(targets).Execute(compiled, canvas);
        });

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(NestedRegionAnalysisFailureNode.PrimaryFailure));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(failure));
            Assert.That(owner.SecondaryFailures, Is.Empty);
            Assert.That(owner.CleanupFailures, Is.Empty);
            Assert.That(owner.IsCleanedUp, Is.True);
            Assert.That(nested.Request.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(child.ExecuteCalls, Is.Zero);
            Assert.That(parent.ExecuteCalls, Is.Zero);
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(registry.Statistics.OwnedTargets, Is.Zero);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
            Assert.That(child.Cache.IsCached, Is.False);
            Assert.That(parent.Cache.IsCached, Is.False);
        });

        Assert.That(
            snapshots.Select(static snapshot => snapshot.RequestId),
            Is.EqualTo(new[] { nested.Request.Id.Value, request.Id.Value }));
        Assert.Multiple(() =>
        {
            Assert.That(snapshots[0].ParentRequestId, Is.EqualTo(request.Id.Value));
            Assert.That(snapshots[1].ParentRequestId, Is.Null);
            Assert.That(snapshots[1].Events, Has.Some.Matches<RenderPipelineDiagnosticEvent>(item =>
                item.Kind == RenderPipelineDiagnosticEventKind.NestedRequest
                && item.RelatedRequestId == nested.Request.Id.Value));
            Assert.That(snapshots, Has.All.Matches<RenderPipelineDiagnosticSnapshot>(snapshot =>
                !snapshot.Succeeded
                && snapshot.FailurePhase == RenderPipelineFailurePhase.RegionAnalysis
                && snapshot[RenderPipelineCounter.Failures] == 1
                && snapshot[RenderPipelineCounter.ExecutedOutcomes] == 0
                && snapshot[RenderPipelineCounter.ExecutedGpuPasses] == 0
                && snapshot[RenderPipelineCounter.RenderCacheCaptures] == 0
                && snapshot[RenderPipelineCounter.RejectedRenderCacheCaptures] == 0
                && snapshot[RenderPipelineCounter.ExternalRootResources] == 0
                && snapshot[RenderPipelineCounter.IntermediateAcquires]
                == snapshot[RenderPipelineCounter.IntermediateDischarges]));
            Assert.That(snapshots, Has.All.Matches<RenderPipelineDiagnosticSnapshot>(snapshot =>
                snapshot[RenderPipelineCounter.RecordedFragments]
                == snapshot[RenderPipelineCounter.ExecutedOutcomes]
                + snapshot[RenderPipelineCounter.CachedOutcomes]
                + snapshot[RenderPipelineCounter.MetadataOutcomes]
                + snapshot[RenderPipelineCounter.SkippedOutcomes]
                + snapshot[RenderPipelineCounter.FailedOutcomes]));
        });
    }

    [Test]
    public void NestedRequest_ExecutesAndCompletesWithinTheParentRequestFamily()
    {
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner);
        using var child = new NestedChildNode();
        using var parent = new NestedParentNode(child);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderRequest nested = graph.NestedRequests.Single();
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget();
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        new RenderRequestExecutor(targets).Execute(compiled, canvas);

        Assert.Multiple(() =>
        {
            Assert.That(child.ExecuteCalls, Is.EqualTo(1),
                "A declared separate-target nested request must execute as part of the parent plan.");
            Assert.That(nested.Request.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void NestedTarget_ParentReadsThePreparedFullDomainWithShiftedChildPixels()
    {
        var fullDomain = new Rect(0, 0, 10, 7);
        var childBounds = new Rect(2, 1, 5, 4);
        using var child = new ShiftedNestedChildNode(childBounds);
        using var parent = new NestedOutputConsumerNode(child, fullDomain);
        using var renderer = new RenderNodeRenderer(
            parent,
            new RenderNodeRendererOptions
            {
                TargetDomain = fullDomain,
                RequestedRegion = fullDomain,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The parent did not publish its nested target output.");
        var inside = bitmap.SKBitmap.GetPixel(4, 3);
        var outside = bitmap.SKBitmap.GetPixel(0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(inside.Red, Is.GreaterThan(240));
            Assert.That(inside.Alpha, Is.GreaterThan(240));
            Assert.That(outside.Alpha, Is.Zero,
                "The child must preserve its shifted origin inside the full transparent target domain.");
            Assert.That(parent.NestedTarget, Is.Not.Null);
            Assert.That(parent.NestedTarget!.Target.LogicalBounds, Is.EqualTo(fullDomain));
            Assert.That(parent.NestedTarget.Target.DeviceBounds, Is.EqualTo(new PixelRect(0, 0, 10, 7)));
            Assert.That(parent.NestedTarget.Target.IsDisposed, Is.True,
                "The prepared child lease must remain live through the parent callback and discharge afterward.");
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void NestedTarget_MetadataQueriesRecurseWithoutExecutingOrAllocating()
    {
        using var child = new NestedChildNode();
        using var parent = new NestedParentNode(child);
        var factory = new FailureTestTargetFactory();
        using var renderer = new RenderNodeRenderer(
            parent,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
                TargetFactory = factory,
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        bool hit = renderer.HitTest(new Point(1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(hit, Is.True);
            Assert.That(child.ExecuteCalls, Is.Zero);
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.OwnedTargets, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void NestedChildExecutionFailure_FailsTheWholeFamilyWithOnePrimaryAndSkipsParentGpuWork()
    {
        using var owner = new RenderRequestOwner();
        var diagnostics = new RenderPipelineDiagnosticsState();
        var snapshots = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += snapshots.Add;
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner,
            diagnostics: diagnostics);
        var primary = new InvalidOperationException("nested-child-primary");
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child", executionOrder, failure: primary);
        using var parent = new NestedExecutionNode(
            "parent",
            executionOrder,
            nestedRoot: child,
            parentOptions: options);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget();
        using var canvas = new ImmediateCanvas(destination);
        var factory = new FailureTestTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(primary));
            Assert.That(owner.SecondaryFailures, Is.Empty,
                "Propagating one child failure to the parent must not record it twice.");
            Assert.That(executionOrder, Is.EqualTo(new[] { "child" }));
            Assert.That(child.ExecuteCalls, Is.EqualTo(1));
            Assert.That(parent.ExecuteCalls, Is.Zero,
                "Parent GPU work must not start after a nested child has failed.");
            Assert.That(factory.CreateCalls, Is.GreaterThan(0),
                "The child must fail after acquiring execution storage so lease cleanup is exercised.");
            Assert.That(nestedRequest.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(owner.IsCleanedUp, Is.True);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
            Assert.That(parent.NestedRequest!.Target.IsDisposed, Is.True,
                "Family failure must reject and discharge the staged child target.");
        });

        Assert.That(
            snapshots.Select(static snapshot => snapshot.RequestId),
            Is.EqualTo(new[] { nestedRequest.Id.Value, request.Id.Value }),
            "Nested completion must be reconciled before its parent completion.");
        Assert.Multiple(() =>
        {
            Assert.That(snapshots[0].ParentRequestId, Is.EqualTo(request.Id.Value));
            Assert.That(snapshots[0].Succeeded, Is.False);
            Assert.That(snapshots[0].FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Execution));
            Assert.That(snapshots[0][RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(snapshots[0][RenderPipelineCounter.FailedOutcomes], Is.EqualTo(1));
            Assert.That(snapshots[1].ParentRequestId, Is.Null);
            Assert.That(snapshots[1].Succeeded, Is.False);
            Assert.That(snapshots[1].FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Execution));
            Assert.That(snapshots[1][RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(snapshots[1][RenderPipelineCounter.SkippedOutcomes], Is.EqualTo(1));
            Assert.That(
                snapshots,
                Has.All.Matches<RenderPipelineDiagnosticSnapshot>(snapshot =>
                    snapshot[RenderPipelineCounter.IntermediateAcquires]
                    == snapshot[RenderPipelineCounter.IntermediateDischarges]));
        });
    }

    [Test]
    public void ParentFailureAfterNestedCacheStaging_RejectsEveryFamilyCaptureAndFailsBothRequests()
    {
        using var owner = new RenderRequestOwner();
        var diagnostics = new RenderPipelineDiagnosticsState();
        var snapshots = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += snapshots.Add;
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Default,
            owner: owner,
            diagnostics: diagnostics);
        var primary = new InvalidOperationException("nested-parent-primary");
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child-cache", executionOrder);
        child.Cache.ReportRenderCount(RenderNodeCache.Count);
        var parentCache = new NestedExecutionNode("parent-cache", executionOrder);
        parentCache.Cache.ReportRenderCount(RenderNodeCache.Count);
        var parentFailure = new NestedExecutionNode("parent-failure", executionOrder, failure: primary);
        using var parent = new NestedContainerNode(child, options, parentCache, parentFailure);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
            renderCacheContext: FailureTestSupport.CacheResolutionContext).Compile(request, graph);
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget();
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(primary));
            Assert.That(owner.SecondaryFailures, Is.Empty,
                "A successful child body is not a secondary failure when its family later aborts.");
            Assert.That(
                executionOrder,
                Is.EqualTo(new[] { "child-cache", "parent-cache", "parent-failure" }));
            Assert.That(child.ExecuteCalls, Is.EqualTo(1));
            Assert.That(parentCache.ExecuteCalls, Is.EqualTo(1));
            Assert.That(parentFailure.ExecuteCalls, Is.EqualTo(1));
            Assert.That(nestedRequest.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(child.Cache.IsCached, Is.False,
                "A nested capture must remain staged until the whole family commits.");
            Assert.That(parentCache.Cache.IsCached, Is.False,
                "A parent capture must not publish when later parent execution fails.");
            Assert.That(owner.IsCleanedUp, Is.True);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });

        Assert.That(
            snapshots.Select(static snapshot => snapshot.RequestId),
            Is.EqualTo(new[] { nestedRequest.Id.Value, request.Id.Value }));
        Assert.Multiple(() =>
        {
            Assert.That(snapshots, Has.All.Matches<RenderPipelineDiagnosticSnapshot>(snapshot =>
                !snapshot.Succeeded
                && snapshot.FailurePhase == RenderPipelineFailurePhase.Execution
                && snapshot[RenderPipelineCounter.Failures] == 1
                && snapshot[RenderPipelineCounter.RenderCacheCaptures] == 0
                && snapshot[RenderPipelineCounter.RejectedRenderCacheCaptures] == 1
                && snapshot[RenderPipelineCounter.IntermediateAcquires]
                == snapshot[RenderPipelineCounter.IntermediateDischarges]));
            Assert.That(snapshots[0].ParentRequestId, Is.EqualTo(request.Id.Value));
            Assert.That(snapshots[0][RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshots[0][RenderPipelineCounter.FailedOutcomes], Is.Zero,
                "The successfully executed child body becomes a dependent request failure, not a failed fragment.");
            Assert.That(snapshots[1].ParentRequestId, Is.Null);
            Assert.That(snapshots[1][RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshots[1][RenderPipelineCounter.FailedOutcomes], Is.EqualTo(1));
        });
    }

    [Test]
    public void TwoLevelNestedRequests_ExecuteDepthFirstAndCompleteEveryRequest()
    {
        using var owner = new RenderRequestOwner();
        var diagnostics = new RenderPipelineDiagnosticsState();
        var snapshots = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += snapshots.Add;
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner,
            diagnostics: diagnostics);
        var executionOrder = new List<string>();
        using var grandchild = new NestedExecutionNode("grandchild", executionOrder);
        using var child = new NestedExecutionNode(
            "child",
            executionOrder,
            nestedRoot: grandchild,
            parentOptions: options);
        using var parent = new NestedExecutionNode(
            "parent",
            executionOrder,
            nestedRoot: child,
            parentOptions: options);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget childRecording = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested child request.");
        RecordedNestedRenderTarget grandchildRecording = child.NestedRequest
            ?? throw new AssertionException("The child did not record its nested grandchild request.");
        using RenderRequest childRequest = childRecording.Request;
        using RenderRequest grandchildRequest = grandchildRecording.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget();
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        new RenderRequestExecutor(targets).Execute(compiled, canvas);

        Assert.Multiple(() =>
        {
            Assert.That(executionOrder, Is.EqualTo(new[] { "grandchild", "child", "parent" }));
            Assert.That(grandchild.ExecuteCalls, Is.EqualTo(1));
            Assert.That(child.ExecuteCalls, Is.EqualTo(1));
            Assert.That(parent.ExecuteCalls, Is.EqualTo(1));
            Assert.That(grandchildRequest.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(childRequest.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(owner.PrimaryFailure, Is.Null);
            Assert.That(owner.IsCleanedUp, Is.True);
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });

        Assert.That(
            snapshots.Select(static snapshot => snapshot.RequestId),
            Is.EqualTo(new[]
            {
                grandchildRequest.Id.Value,
                childRequest.Id.Value,
                request.Id.Value,
            }));
        Assert.Multiple(() =>
        {
            Assert.That(snapshots, Has.All.Matches<RenderPipelineDiagnosticSnapshot>(snapshot =>
                snapshot.Succeeded
                && snapshot[RenderPipelineCounter.IntermediateAcquires]
                == snapshot[RenderPipelineCounter.IntermediateDischarges]));
            Assert.That(snapshots[0].ParentRequestId, Is.EqualTo(childRequest.Id.Value));
            Assert.That(snapshots[1].ParentRequestId, Is.EqualTo(request.Id.Value));
            Assert.That(snapshots[2].ParentRequestId, Is.Null);
            Assert.That(snapshots[1].Events, Has.Some.Matches<RenderPipelineDiagnosticEvent>(item =>
                item.Kind == RenderPipelineDiagnosticEventKind.NestedRequest
                && item.RelatedRequestId == grandchildRequest.Id.Value));
            Assert.That(snapshots[2].Events, Has.Some.Matches<RenderPipelineDiagnosticEvent>(item =>
                item.Kind == RenderPipelineDiagnosticEventKind.NestedRequest
                && item.RelatedRequestId == childRequest.Id.Value));
        });
    }

    [Test]
    public void ExecutionPrimary_SurvivesCleanupFaultAndCleanupContinuesInStrictLifoOrder()
    {
        var order = new List<string>();
        var primary = new InvalidOperationException("execution-primary");
        var cleanup = new InvalidOperationException("cleanup-secondary");
        using var node = new PrimaryAndCleanupFailureNode(order, primary, cleanup);
        using var renderer = FailureTestSupport.CreateRenderer(
            node,
            useRenderCache: true,
            purpose: RenderRequestPurpose.Frame);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(order, Is.EqualTo(new[] { "last", "throwing", "first" }));
            Assert.That(node.Resources, Has.All.Matches<OrderedDisposable>(resource => resource.DisposeCalls == 1));
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void LaterExecutionFailure_RejectsAnEarlierStagedCacheCaptureWithoutPartialPublication()
    {
        using var successful = new CacheableChildNode(throwOnExecute: false);
        using var faulting = new CacheableChildNode(throwOnExecute: true);
        successful.Cache.ReportRenderCount(RenderNodeCache.Count);
        faulting.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var root = new ContainerRenderNode();
        root.AddChild(successful);
        root.AddChild(faulting);
        using var renderer = FailureTestSupport.CreateRenderer(
            root,
            useRenderCache: true,
            purpose: RenderRequestPurpose.Frame);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Is.EqualTo("later-cache-candidate-failure"));
            Assert.That(successful.ExecuteCalls, Is.EqualTo(1));
            Assert.That(faulting.ExecuteCalls, Is.EqualTo(1));
            Assert.That(successful.Cache.IsCached, Is.False);
            Assert.That(faulting.Cache.IsCached, Is.False);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });

        root.RemoveChild(successful);
        root.RemoveChild(faulting);
    }

    [Test]
    public void CleanupFaultBeforeCachePublication_RejectsCaptureAndDischargesEveryResource()
    {
        var order = new List<string>();
        var cleanup = new InvalidOperationException("pre-publication-cleanup");
        using var node = new CleanupFailureCacheableNode(order, cleanup);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var renderer = FailureTestSupport.CreateRenderer(
            node,
            useRenderCache: true,
            purpose: RenderRequestPurpose.Frame);

        AggregateException? failure = Assert.Throws<AggregateException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Flatten().InnerExceptions, Does.Contain(cleanup));
            Assert.That(order, Is.EqualTo(new[] { "last", "throwing", "first" }));
            Assert.That(node.Resources, Has.All.Matches<OrderedDisposable>(resource => resource.DisposeCalls == 1));
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void AtomicCacheTransfer_CommitsTheWholeReplacementBeforeOldStorageCleanupFailure()
    {
        var oldDisposalFailure = new InvalidOperationException("old-cache-dispose-primary");
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new CacheableChildNode(throwOnExecute: false);
        using (var seedRequest = FailureTestSupport.CreateFrameRequest(useRenderCache: false))
        {
            RecordedRenderGraph seedGraph = new RenderRequestRecorder(seedRequest).Record(node);
            RenderFragmentReference seedRoot = RenderRequestCompiler.ResolveRoots(seedGraph).Single();
            var seedIdentity = new RenderOutputCacheIdentity(
                "deliberately-stale-cache-identity",
                RenderFragmentOutputIdentity.Create(seedRoot, seedGraph.RequestId),
                s_bounds,
                RequiredRegion.Region(s_bounds),
                density: 1,
                RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                RenderIntent.Preview,
                RenderRequestPurpose.Frame,
                new RenderCacheDeviceContextIdentity("stale-device", "stale-context"));
            RenderNodeCache.PublishAtomically(
            [
                new RenderNodeCachePublication(
                    node.Cache,
                    seedIdentity,
                    [new RenderNodeCachedValue(
                        new FailureTestRenderTarget(new PixelSize(8, 8), oldDisposalFailure),
                        s_bounds,
                        EffectiveScale.At(1))]),
            ]);
        }
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var renderer = FailureTestSupport.CreateRenderer(
            node,
            useRenderCache: true,
            purpose: RenderRequestPurpose.Frame,
            diagnostics: diagnostics);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());
        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        int cleanupIndex = snapshot.Events.ToList().FindIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.CleanupFailure);
        int publicationIndex = snapshot.Events.ToList().FindIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.CacheCapturePublished);
        int completionIndex = snapshot.Events.ToList().FindIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.RequestCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(oldDisposalFailure));
            Assert.That(node.ExecuteCalls, Is.EqualTo(1));
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.CacheCount, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Cleanup));
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RenderCacheCaptures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RejectedRenderCacheCaptures], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.IntermediateAcquires],
                Is.EqualTo(snapshot[RenderPipelineCounter.IntermediateDischarges]));
            Assert.That(cleanupIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(publicationIndex, Is.GreaterThan(cleanupIndex));
            Assert.That(completionIndex, Is.GreaterThan(publicationIndex));
        });
    }

    [Test]
    public void NestedPostCommitCleanupFailure_ReconcilesEveryFamilyDiagnosticScope()
    {
        using var owner = new RenderRequestOwner();
        var diagnostics = new RenderPipelineDiagnosticsState();
        var snapshots = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += snapshots.Add;
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Default,
            owner: owner,
            diagnostics: diagnostics);
        var oldDisposalFailure = new InvalidOperationException("nested-old-cache-dispose-primary");
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child-cache-cleanup", executionOrder);
        SeedThrowingStaleCache(child, oldDisposalFailure);
        child.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var parent = new NestedExecutionNode(
            "parent-cache-cleanup",
            executionOrder,
            nestedRoot: child,
            parentOptions: options);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
            renderCacheContext: FailureTestSupport.CacheResolutionContext).Compile(request, graph);
        using RenderTarget destination = FailureTestSupport.CreateCpuTarget();
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(oldDisposalFailure));
            Assert.That(child.Cache.IsCached, Is.True);
            Assert.That(snapshots.Select(static snapshot => snapshot.RequestId),
                Is.EqualTo(new[] { nestedRequest.Id.Value, request.Id.Value }));
            Assert.That(snapshots,
                Has.All.Matches<RenderPipelineDiagnosticSnapshot>(snapshot =>
                    !snapshot.Succeeded
                    && snapshot.FailurePhase == RenderPipelineFailurePhase.Cleanup
                    && snapshot[RenderPipelineCounter.Failures] == 1
                    && snapshot[RenderPipelineCounter.CleanupFailures] == 1
                    && snapshot[RenderPipelineCounter.IntermediateAcquires]
                    == snapshot[RenderPipelineCounter.IntermediateDischarges]));
            Assert.That(snapshots[0][RenderPipelineCounter.RenderCacheCaptures], Is.EqualTo(1));
            Assert.That(snapshots[0][RenderPipelineCounter.RejectedRenderCacheCaptures], Is.Zero);
            Assert.That(snapshots[1][RenderPipelineCounter.RenderCacheCaptures], Is.Zero);
        });
    }

    private static void SeedThrowingStaleCache(RenderNode node, Exception disposalFailure)
    {
        using var seedRequest = FailureTestSupport.CreateFrameRequest(useRenderCache: false);
        RecordedRenderGraph seedGraph = new RenderRequestRecorder(seedRequest).Record(node);
        RenderFragmentReference seedRoot = RenderRequestCompiler.ResolveRoots(seedGraph).Single();
        var seedIdentity = new RenderOutputCacheIdentity(
            "nested-deliberately-stale-cache-identity",
            RenderFragmentOutputIdentity.Create(seedRoot, seedGraph.RequestId),
            s_bounds,
            RequiredRegion.Region(s_bounds),
            density: 1,
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            new RenderCacheDeviceContextIdentity("nested-stale-device", "nested-stale-context"));
        RenderNodeCache.PublishAtomically(
        [
            new RenderNodeCachePublication(
                node.Cache,
                seedIdentity,
                [new RenderNodeCachedValue(
                    new FailureTestRenderTarget(new PixelSize(8, 8), disposalFailure),
                    s_bounds,
                    EffectiveScale.At(1))]),
        ]);
    }

    public enum TargetCallbackFailure
    {
        CommandCallback,
        UndeclaredTargetReadback,
        MissingTargetReadback,
        UndeclaredInputReadback,
        DuplicateInputReadback,
        ScopeCallback,
        ScopeMissingReplay,
        ScopeDoubleReplay,
        RawCommandCallback,
        RawScopeCallback,
        RawScopeMissingReplay,
        RawScopeDoubleReplay,
    }

    private sealed class TargetCallbackFailureNode(TargetCallbackFailure failurePoint) : RenderNode
    {
        public int CallbackEntries { get; private set; }

        public Action? VerifyRetainedSession { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            if (failurePoint is TargetCallbackFailure.RawCommandCallback)
            {
                RawTargetCommandDescription rawCommand = RawTargetCommandDescription.Create(
                    session =>
                    {
                        CallbackEntries++;
                        VerifyRetainedSession = () => _ = session.Intent;
                        throw new InvalidOperationException("raw-command-primary");
                    },
                    s_bounds,
                    RenderHitTestContract.OutputBounds,
                    structuralKey: "raw-command-failure");
                context.Publish(context.RawTargetCommand(rawCommand));
                return;
            }

            RenderFragmentHandle source = context.OpaqueSource(FailureTestSupport.SourceDescription(
                structuralKey: $"target-callback-source-{failurePoint}"));
            if (failurePoint is TargetCallbackFailure.CommandCallback
                or TargetCallbackFailure.UndeclaredTargetReadback
                or TargetCallbackFailure.MissingTargetReadback
                or TargetCallbackFailure.UndeclaredInputReadback
                or TargetCallbackFailure.DuplicateInputReadback)
            {
                bool targetReadback = failurePoint == TargetCallbackFailure.MissingTargetReadback;
                bool inputReadback = failurePoint == TargetCallbackFailure.DuplicateInputReadback;
                TargetCommandDescription command = TargetCommandDescription.Create(
                    session =>
                    {
                        CallbackEntries++;
                        VerifyRetainedSession = () => _ = session.Intent;
                        switch (failurePoint)
                        {
                            case TargetCallbackFailure.CommandCallback:
                                throw new InvalidOperationException("target-command-primary");
                            case TargetCallbackFailure.UndeclaredTargetReadback:
                                session.UseSnapshot(static _ => { });
                                break;
                            case TargetCallbackFailure.MissingTargetReadback:
                                break;
                            case TargetCallbackFailure.UndeclaredInputReadback:
                                session.Inputs.Single().UseSnapshot(static _ => { });
                                break;
                            case TargetCallbackFailure.DuplicateInputReadback:
                                session.Inputs.Single().UseSnapshot(static _ => { });
                                session.Inputs.Single().UseSnapshot(static _ => { });
                                break;
                        }
                    },
                    TargetRegion.Region(s_bounds),
                    s_bounds,
                    RenderHitTestContract.OutputBounds,
                    targetReadback ? TargetAccess.Readback : TargetAccess.ReadWrite,
                    requiresInputReadback: inputReadback,
                    structuralKey: $"target-command-{failurePoint}");
                context.Publish(source);
                context.Publish(context.TargetCommand([source], command));
                return;
            }

            if (failurePoint is TargetCallbackFailure.ScopeCallback
                or TargetCallbackFailure.ScopeMissingReplay
                or TargetCallbackFailure.ScopeDoubleReplay)
            {
                TargetScopeDescription scope = TargetScopeDescription.Create(
                    session =>
                    {
                        CallbackEntries++;
                        VerifyRetainedSession = () => _ = session.Intent;
                        switch (failurePoint)
                        {
                            case TargetCallbackFailure.ScopeCallback:
                                throw new InvalidOperationException("target-scope-primary");
                            case TargetCallbackFailure.ScopeMissingReplay:
                                break;
                            case TargetCallbackFailure.ScopeDoubleReplay:
                                session.Canvas.Use(_ =>
                                {
                                    session.ReplayInput();
                                    session.ReplayInput();
                                });
                                break;
                        }
                    },
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: $"target-scope-{failurePoint}");
                context.Publish(context.TargetScope(source, scope));
                return;
            }

            RawTargetScopeDescription rawScope = RawTargetScopeDescription.Create(
                session =>
                {
                    CallbackEntries++;
                    VerifyRetainedSession = () => _ = session.Intent;
                    switch (failurePoint)
                    {
                        case TargetCallbackFailure.RawScopeCallback:
                            throw new InvalidOperationException("raw-scope-primary");
                        case TargetCallbackFailure.RawScopeMissingReplay:
                            break;
                        case TargetCallbackFailure.RawScopeDoubleReplay:
                            session.ReplayInput();
                            session.ReplayInput();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: $"raw-target-scope-{failurePoint}");
            context.Publish(context.RawTargetScope(source, rawScope));
        }
    }

    private sealed class TargetCaptureNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Region(s_bounds),
                s_bounds,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.MaterializeAtWorkingScale));
            context.Publish(context.ContributeValues(capture));
        }
    }

    private sealed class BackendBoundaryFailureNode(InvalidOperationException failure) : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                _ =>
                {
                    ExecuteCalls++;
                    throw failure;
                },
                structuralKey: "graphics3d-backend-failure",
                backendBoundary: RenderBackendBoundary.Graphics3D)));
        }
    }

    private sealed class NestedPlanningFailureParentNode(RenderNode child) : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            _ = context.RecordNestedTarget(child, s_bounds);
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    ExecuteCalls++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                structuralKey: "nested-planning-parent")));
        }
    }

    private sealed class NestedRegionAnalysisFailureNode : RenderNode
    {
        public static InvalidOperationException PrimaryFailure { get; } =
            new("nested-region-analysis-primary");

        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    ExecuteCalls++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                structuralKey: "nested-planning-child"));
            RenderBoundsContract bounds = RenderBoundsContract.Create(
                static value => value,
                ThrowDuringRequiredInputBounds,
                structuralKey: "nested-region-analysis-failure");
            GeometryDescription geometry = GeometryDescription.Create(
                static _ => { },
                bounds,
                RenderHitTestContract.AnyInput,
                structuralKey: "nested-region-analysis-geometry");
            context.Publish(context.Geometry(source, geometry));
        }

        private static Rect ThrowDuringRequiredInputBounds(Rect _)
            => throw PrimaryFailure;
    }

    private sealed class NestedParentNode(RenderNode child) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            _ = context.RecordNestedTarget(child, s_bounds);
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                structuralKey: "nested-parent-source")));
        }
    }

    private sealed class NestedChildNode : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    ExecuteCalls++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.Green));
                    session.Publish(output);
                },
                structuralKey: "nested-child-source")));
        }
    }

    private sealed class ShiftedNestedChildNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.Red));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "shifted-nested-child");
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class NestedOutputConsumerNode(
        RenderNode child,
        Rect targetDomain) : RenderNode
    {
        public RecordedNestedRenderTarget? NestedTarget { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            NestedTarget = context.RecordNestedTarget(child, targetDomain);
            RecordedNestedRenderTarget nested = NestedTarget;
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session => session.UseNestedTarget(
                    nested.Binding,
                    image =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(targetDomain);
                        output.Canvas.Use(canvas =>
                        {
                            canvas.Clear(Colors.Transparent);
                            image.Draw(canvas);
                        });
                        session.Publish(output);
                    }),
                RenderOperationBoundsContract.Source(targetDomain),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "nested-output-consumer",
                resources: [nested.Binding]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class NestedExecutionNode(
        string name,
        ICollection<string> executionOrder,
        RenderNode? nestedRoot = null,
        RenderRequestOptions? parentOptions = null,
        Exception? failure = null) : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public RecordedNestedRenderTarget? NestedRequest { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            if ((nestedRoot is null) != (parentOptions is null))
            {
                throw new InvalidOperationException(
                    "A nested root and its inherited parent options must be supplied together.");
            }

            if (nestedRoot is not null)
            {
                NestedRequest = context.RecordNestedTarget(nestedRoot, s_bounds);
            }

            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    ExecuteCalls++;
                    executionOrder.Add(name);
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.Cyan));
                    if (failure is not null)
                        throw failure;
                    session.Publish(output);
                },
                structuralKey: $"nested-family-{name}")));
        }
    }

    private sealed class NestedContainerNode : ContainerRenderNode
    {
        private readonly RenderNode _nestedRoot;
        private readonly RenderRequestOptions _parentOptions;

        public NestedContainerNode(
            RenderNode nestedRoot,
            RenderRequestOptions parentOptions,
            params RenderNode[] parentChildren)
        {
            _nestedRoot = nestedRoot;
            _parentOptions = parentOptions;
            foreach (RenderNode child in parentChildren)
                AddChild(child);
        }

        public RecordedNestedRenderTarget? NestedRequest { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            NestedRequest = context.RecordNestedTarget(_nestedRoot, s_bounds);
            context.PassThrough();
        }
    }

    private sealed class PrimaryAndCleanupFailureNode : RenderNode
    {
        private readonly List<string> _order;
        private readonly InvalidOperationException _primary;

        public PrimaryAndCleanupFailureNode(
            List<string> order,
            InvalidOperationException primary,
            InvalidOperationException cleanup)
        {
            _order = order;
            _primary = primary;
            Resources =
            [
                new OrderedDisposable("first", order),
                new OrderedDisposable("throwing", order, cleanup),
                new OrderedDisposable("last", order),
            ];
        }

        public OrderedDisposable[] Resources { get; }

        public override void Process(RenderNodeContext context)
        {
            for (int index = 0; index < Resources.Length; index++)
                _ = context.Own(Resources[index], $"primary-cleanup-{index}", 0);
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                _ => throw _primary,
                structuralKey: "primary-and-cleanup-source")));
        }
    }

    private sealed class CacheableChildNode(bool throwOnExecute) : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    ExecuteCalls++;
                    if (throwOnExecute)
                        throw new InvalidOperationException("later-cache-candidate-failure");
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.Purple));
                    session.Publish(output);
                },
                structuralKey: throwOnExecute ? "faulting-cache-child" : "successful-cache-child")));
        }
    }

    private sealed class CleanupFailureCacheableNode : RenderNode
    {
        public CleanupFailureCacheableNode(List<string> order, InvalidOperationException cleanup)
        {
            Resources =
            [
                new OrderedDisposable("first", order),
                new OrderedDisposable("throwing", order, cleanup),
                new OrderedDisposable("last", order),
            ];
        }

        public OrderedDisposable[] Resources { get; }

        public override void Process(RenderNodeContext context)
        {
            for (int index = 0; index < Resources.Length; index++)
                _ = context.Own(Resources[index], $"cleanup-cacheable-{index}", 0);
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                structuralKey: "cleanup-cacheable-source")));
        }
    }

    private sealed class OrderedDisposable(
        string name,
        ICollection<string> order,
        Exception? failure = null) : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
            order.Add(name);
            if (failure is not null)
                throw failure;
        }
    }
}
