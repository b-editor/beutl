using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
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
    public void TargetCommandScopeAndRawFailures_DischargeAllStateAndSealTheSession(
        TargetCallbackFailure failurePoint)
    {
        using var node = new TargetCallbackFailureNode(failurePoint);
        using var renderer = FailureTestSupport.CreateRenderer(node, useRenderCache: false);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain(ExpectedTargetCallbackFailureMessage(failurePoint)));
            Assert.That(node.CallbackEntries, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
        });
        Action verifyRetainedSession = node.VerifyRetainedSession
            ?? throw new AssertionException("The deferred callback did not expose its retained-session probe.");
        Assert.That(verifyRetainedSession, Throws.TypeOf<InvalidOperationException>());
    }

    private static string ExpectedTargetCallbackFailureMessage(TargetCallbackFailure failurePoint)
        => failurePoint switch
        {
            TargetCallbackFailure.CommandCallback => "target-command-primary",
            TargetCallbackFailure.UndeclaredTargetReadback => "did not declare target readback",
            TargetCallbackFailure.MissingTargetReadback => "consume its snapshot exactly once",
            TargetCallbackFailure.UndeclaredInputReadback => "CPU readback was not declared",
            TargetCallbackFailure.DuplicateInputReadback => "snapshot is a one-shot lease",
            TargetCallbackFailure.ScopeCallback => "target-scope-primary",
            TargetCallbackFailure.ScopeMissingReplay or TargetCallbackFailure.ScopeDoubleReplay
                => "A target scope input must be replayed exactly once",
            TargetCallbackFailure.RawCommandCallback => "raw-command-primary",
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint), failurePoint, null),
        };

    [TestCase(TransientMaterializationKind.Layer)]
    [TestCase(TransientMaterializationKind.TargetLayerScope)]
    public void TransientMaterializationFailure_ReleasesItsValueBeforePropagating(
        TransientMaterializationKind kind)
    {
        using FailureTestExecution execution = FailureTestSupport.BeginExecution();
        using var node = new TransientMaterializationFailureNode(
            kind,
            () => execution.Registry.Statistics.LeasedTargets);
        using RenderRequest request = FailureTestSupport.CreateFrameRequest(useRenderCache: false);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => execution.Execute(compiled));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(node.PrimaryFailure));
            Assert.That(node.LeasedTargetsObservedBySource, Is.EqualTo(1),
                "The failure must occur after the transient target has been acquired.");
            Assert.That(node.LeasedTargetsObservedByCaller, Is.Zero,
                "The transient target must be released before control returns to the replaying caller.");
            Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void NestedChildRegionAnalysisFailure_FailsTheFamilyBeforeAllocationAndPreservesThePrimary()
    {
        using var owner = new RenderRequestOwner();
        using var child = new NestedRegionAnalysisFailureNode();
        using var parent = new NestedPlanningFailureParentNode(child);
        child.Cache.RecordStableRequests();
        parent.Cache.RecordStableRequests();
        using var request = new RenderRequest(
            FailureTestSupport.CreateOptions(RenderRequestPurpose.Frame, useRenderCache: true, owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderRequest nested = graph.NestedRequests.Single();
        var factory = new FailureTestTargetFactory();
        using FailureTestExecution execution = FailureTestSupport.BeginExecution(factory);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
            execution.Execute(compiled);
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
            Assert.That(execution.Registry.Statistics.OwnedTargets, Is.Zero);
            Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
            Assert.That(child.Cache.IsCached, Is.False);
            Assert.That(parent.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void NestedRequest_ExecutesAndCompletesWithinTheParentRequestFamily()
    {
        using var owner = new RenderRequestOwner();
        using var child = new NestedExecutionNode("child", []);
        using var parent = new NestedExecutionNode("parent", [], nestedRoot: child);
        using var request = new RenderRequest(
            FailureTestSupport.CreateOptions(RenderRequestPurpose.Auxiliary, owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderRequest nested = graph.NestedRequests.Single();
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        using FailureTestExecution execution = FailureTestSupport.BeginExecution();

        execution.Execute(compiled);

        Assert.Multiple(() =>
        {
            Assert.That(child.ExecuteCalls, Is.EqualTo(1),
                "A declared separate-target nested request must execute as part of the parent plan.");
            Assert.That(nested.Request.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void NestedTarget_MetadataQueriesRecurseWithoutExecutingOrAllocating()
    {
        using var child = new NestedExecutionNode("child", []);
        using var parent = new NestedExecutionNode("parent", [], nestedRoot: child);
        var factory = new FailureTestTargetFactory();
        using var renderer = FailureTestSupport.CreateRenderer(parent, factory);

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
        var primary = new InvalidOperationException("nested-child-primary");
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child", executionOrder, failure: primary);
        using var parent = new NestedExecutionNode("parent", executionOrder, nestedRoot: child);
        using var request = new RenderRequest(
            FailureTestSupport.CreateOptions(RenderRequestPurpose.Auxiliary, owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Recording.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        var factory = new FailureTestTargetFactory();
        using FailureTestExecution execution = FailureTestSupport.BeginExecution(factory);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => execution.Execute(compiled));

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
            Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
            Assert.That(parent.NestedRequest!.Target.IsDisposed, Is.True,
                "Family failure must reject and discharge the staged child target.");
        });
    }

    // Preview drops a nested target it cannot allocate and still renders the parent; Delivery fails (next test).
    [Test]
    public void NestedTargetAllocationFailureInPreview_DropsTheNestedValueAndStillRendersTheParent()
    {
        using var owner = new RenderRequestOwner();
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child", executionOrder);
        using var parent = new NestedExecutionNode("parent", executionOrder, nestedRoot: child);
        using var request = new RenderRequest(
            FailureTestSupport.CreateOptions(RenderRequestPurpose.Auxiliary, owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Recording.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        var factory = new FailureTestTargetFactory(failAt: 0);
        using FailureTestExecution execution = FailureTestSupport.BeginExecution(factory);

        Assert.DoesNotThrow(() => execution.Execute(compiled));

        Assert.Multiple(() =>
        {
            Assert.That(owner.PrimaryFailure, Is.Null);
            Assert.That(executionOrder, Is.EqualTo(new[] { "parent" }),
                "The nested subtree is dropped; the parent still renders.");
            Assert.That(child.ExecuteCalls, Is.Zero);
            Assert.That(parent.ExecuteCalls, Is.EqualTo(1));
            Assert.That(nested.Target.IsReady, Is.False);
            Assert.That(nestedRequest.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void NestedTargetAllocationFailureInDelivery_StillFailsFast()
    {
        using var owner = new RenderRequestOwner();
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child", executionOrder);
        using var parent = new NestedExecutionNode("parent", executionOrder, nestedRoot: child);
        using var request = new RenderRequest(FailureTestSupport.CreateOptions(
            RenderRequestPurpose.Auxiliary,
            owner: owner,
            intent: RenderIntent.Delivery));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Recording.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        var factory = new FailureTestTargetFactory(failAt: 0);
        using FailureTestExecution execution = FailureTestSupport.BeginExecution(factory, RenderIntent.Delivery);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => execution.Execute(compiled));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("could not allocate"));
            Assert.That(parent.ExecuteCalls, Is.Zero);
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
        });
    }

    [Test]
    public void ParentFailureAfterNestedCacheStaging_RejectsEveryFamilyCaptureAndFailsBothRequests()
    {
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Enabled,
            owner: owner);
        var primary = new InvalidOperationException("nested-parent-primary");
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child-cache", executionOrder);
        child.Cache.RecordStableRequests();
        var parentCache = new NestedExecutionNode("parent-cache", executionOrder);
        parentCache.Cache.RecordStableRequests();
        var parentFailure = new NestedExecutionNode("parent-failure", executionOrder, failure: primary);
        using var parent = new NestedContainerNode(child, parentCache, parentFailure);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Recording.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
            renderCacheContext: FailureTestSupport.CacheResolutionContext).Compile(request, graph);
        using FailureTestExecution execution = FailureTestSupport.BeginExecution();

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => execution.Execute(compiled));

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
            Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void TwoLevelNestedRequests_ExecuteDepthFirstAndCompleteEveryRequest()
    {
        using var owner = new RenderRequestOwner();
        var executionOrder = new List<string>();
        using var grandchild = new NestedExecutionNode("grandchild", executionOrder);
        using var child = new NestedExecutionNode("child", executionOrder, nestedRoot: grandchild);
        using var parent = new NestedExecutionNode("parent", executionOrder, nestedRoot: child);
        using var request = new RenderRequest(
            FailureTestSupport.CreateOptions(RenderRequestPurpose.Auxiliary, owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget childRecording = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested child request.");
        RecordedNestedRenderTarget grandchildRecording = child.NestedRequest
            ?? throw new AssertionException("The child did not record its nested grandchild request.");
        using RenderRequest childRequest = childRecording.Recording.Request;
        using RenderRequest grandchildRequest = grandchildRecording.Recording.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        using FailureTestExecution execution = FailureTestSupport.BeginExecution();

        execution.Execute(compiled);

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
            Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
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
        node.Cache.RecordStableRequests();

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
    public void SameProducerHitAndMultipleMisses_PublishEveryCapture()
    {
        RunSameProducerHitAndMultipleMisses((producer, compiled, execution) =>
        {
            var decisions = compiled.CacheResolution.Decisions;
            RenderNodeCache firstMissCache = decisions[1].Candidate.Cache;
            RenderNodeCache secondMissCache = decisions[2].Candidate.Cache;

            execution.Execute(compiled);

            Assert.Multiple(() =>
            {
                Assert.That(
                    decisions.Select(static decision => decision.Kind),
                    Is.EqualTo(new[]
                    {
                        RenderCacheResolutionKind.Hit,
                        RenderCacheResolutionKind.MissCapture,
                        RenderCacheResolutionKind.MissCapture,
                    }));
                Assert.That(decisions.Select(static decision => decision.Candidate.FragmentId), Is.All.EqualTo(
                    decisions[0].Candidate.FragmentId));
                Assert.That(firstMissCache, Is.Not.SameAs(secondMissCache));
                Assert.That(producer.ExecuteCalls, Is.Zero);
                Assert.That(producer.Cache.IsCached, Is.False);
                Assert.That(
                    firstMissCache.TryGetCachedOutput(decisions[1].Identity!, out RenderNodeCachedOutput? firstOutput),
                    Is.True);
                Assert.That(firstOutput?.Values, Has.Count.EqualTo(1));
                Assert.That(firstMissCache.TryGetCachedOutput(decisions[2].Identity!, out _), Is.False);
                Assert.That(
                    secondMissCache.TryGetCachedOutput(decisions[2].Identity!, out RenderNodeCachedOutput? secondOutput),
                    Is.True);
                Assert.That(secondOutput?.Values, Has.Count.EqualTo(1));
                Assert.That(secondMissCache.TryGetCachedOutput(decisions[1].Identity!, out _), Is.False);
                Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
            });
        });
    }

    [Test]
    public void SameProducerHitAndMultipleMisses_RollBackWhenOneCacheIsDisposed()
    {
        RunSameProducerHitAndMultipleMisses((producer, compiled, execution) =>
        {
            var decisions = compiled.CacheResolution.Decisions;
            RenderNodeCache firstMissCache = decisions[1].Candidate.Cache;
            RenderNodeCache secondMissCache = decisions[2].Candidate.Cache;
            secondMissCache.Dispose();

            Assert.Throws<ObjectDisposedException>(() => execution.Execute(compiled));

            Assert.Multiple(() =>
            {
                Assert.That(
                    decisions.Select(static decision => decision.Kind),
                    Is.EqualTo(new[]
                    {
                        RenderCacheResolutionKind.Hit,
                        RenderCacheResolutionKind.MissCapture,
                        RenderCacheResolutionKind.MissCapture,
                    }));
                Assert.That(producer.ExecuteCalls, Is.Zero);
                Assert.That(firstMissCache.IsCached, Is.False);
                Assert.That(secondMissCache.IsCached, Is.False);
                Assert.That(execution.Registry.Statistics.LeasedTargets, Is.Zero);
            });
        });
    }

    // Compiles one producer behind two capture owners against a lookup that hits only the first candidate,
    // so the plan holds a hit and two misses of the same producer.
    private static void RunSameProducerHitAndMultipleMisses(
        Action<CacheableChildNode, CompiledRenderRequest, FailureTestExecution> test)
    {
        using var producer = new CacheableChildNode(throwOnExecute: false);
        using var firstCaptureOwner = new ReferencesChildRenderNode(producer);
        using var secondCaptureOwner = new ReferencesChildRenderNode(firstCaptureOwner);
        producer.Cache.RecordStableRequests();
        firstCaptureOwner.Cache.RecordStableRequests();
        secondCaptureOwner.Cache.RecordStableRequests();
        secondCaptureOwner.SettleConstruction();
        using RenderTarget cachedTarget = FailureTestSupport.CreateCpuTarget();
        using RenderRequest request = FailureTestSupport.CreateFrameRequest(useRenderCache: true);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(secondCaptureOwner);
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
            renderCacheContext: FailureTestSupport.CacheResolutionContext,
            renderCacheLookup: new FirstCandidateHitLookup(cachedTarget)).Compile(request, graph);
        using FailureTestExecution execution = FailureTestSupport.BeginExecution();

        test(producer, compiled, execution);
    }

    [Test]
    public void LaterExecutionFailure_RejectsAnEarlierStagedCacheCaptureWithoutPartialPublication()
    {
        using var successful = new CacheableChildNode(throwOnExecute: false);
        using var faulting = new CacheableChildNode(throwOnExecute: true);
        successful.Cache.RecordStableRequests();
        faulting.Cache.RecordStableRequests();
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
        node.Cache.RecordStableRequests();
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
                FusionMode.Enabled,
                new RenderCacheDeviceContextIdentity("stale-device", "stale-context"));
            RenderNodeCache.PublishAtomically(
            [
                new RenderNodeCachePublication(
                    node.Cache,
                    seedIdentity,
                    [new RenderNodeCachedValue(
                        new FailureTestRenderTarget(new PixelSize(8, 8), oldDisposalFailure),
                        s_bounds,
                        EffectiveScale.At(1),
                        PixelRect.FromRect(s_bounds, 1))]),
            ]);
        }
        node.Cache.RecordStableRequests();
        using var renderer = FailureTestSupport.CreateRenderer(
            node,
            useRenderCache: true,
            purpose: RenderRequestPurpose.Frame);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(oldDisposalFailure));
            Assert.That(node.ExecuteCalls, Is.EqualTo(1));
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.TryGetCachedOutput(node.Cache.CurrentCacheIdentity(), out RenderNodeCachedOutput? cached));
            Assert.That(cached?.Values, Has.Count.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void NestedPostCommitCleanupFailure_ReconcilesEveryFamilyDiagnosticScope()
    {
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Enabled,
            owner: owner);
        var oldDisposalFailure = new InvalidOperationException("nested-old-cache-dispose-primary");
        var executionOrder = new List<string>();
        using var child = new NestedExecutionNode("child-cache-cleanup", executionOrder);
        SeedThrowingStaleCache(child, oldDisposalFailure);
        child.Cache.RecordStableRequests();
        using var parent = new NestedExecutionNode("parent-cache-cleanup", executionOrder, nestedRoot: child);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        RecordedNestedRenderTarget nested = parent.NestedRequest
            ?? throw new AssertionException("The parent did not record its nested request.");
        using RenderRequest nestedRequest = nested.Recording.Request;
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
            renderCacheContext: FailureTestSupport.CacheResolutionContext).Compile(request, graph);
        using FailureTestExecution execution = FailureTestSupport.BeginExecution();

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => execution.Execute(compiled));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(oldDisposalFailure));
            Assert.That(child.Cache.IsCached, Is.True);
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
            FusionMode.Enabled,
            new RenderCacheDeviceContextIdentity("nested-stale-device", "nested-stale-context"));
        RenderNodeCache.PublishAtomically(
        [
            new RenderNodeCachePublication(
                node.Cache,
                seedIdentity,
                [new RenderNodeCachedValue(
                    new FailureTestRenderTarget(new PixelSize(8, 8), disposalFailure),
                    s_bounds,
                    EffectiveScale.At(1),
                    PixelRect.FromRect(s_bounds, 1))]),
        ]);
    }

    private sealed class FirstCandidateHitLookup(RenderTarget target) : IRenderCacheLookup
    {
        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? entry)
        {
            if (candidate.Id.Value != 1)
            {
                entry = null;
                return false;
            }

            entry = new RenderCacheEntry(
                identity,
                new RenderNodeCachedOutput(
                [
                    new RenderNodeCachedValue(
                        target,
                        identity.Bounds,
                        EffectiveScale.At(identity.Density),
                        PixelRect.FromRect(identity.Bounds, identity.Density)),
                ]));
            return true;
        }
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
    }

    public enum TransientMaterializationKind
    {
        Layer,
        TargetLayerScope,
    }

    private sealed class TransientMaterializationFailureNode(
        TransientMaterializationKind kind,
        Func<int> getLeasedTargetCount) : RenderNode
    {
        public InvalidOperationException PrimaryFailure { get; } =
            new($"transient-{kind}-failure");

        public int? LeasedTargetsObservedBySource { get; private set; }

        public int? LeasedTargetsObservedByCaller { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(FailureTestSupport.SourceDescription(
                execute: _ =>
                {
                    LeasedTargetsObservedBySource = getLeasedTargetCount();
                    throw PrimaryFailure;
                }));
            RenderFragmentHandle transient = kind switch
            {
                TransientMaterializationKind.Layer => context.Layer([source], s_bounds),
                TransientMaterializationKind.TargetLayerScope => context.TargetLayerScope(
                    [source],
                    TargetRegion.Region(s_bounds)),
                _ => throw new ArgumentOutOfRangeException(),
            };
            TargetScopeDescription caller = TargetScopeDescription.CreateRequestLocal(
                session =>
                {
                    try
                    {
                        session.Canvas.Use(_ => session.ReplayInput());
                    }
                    catch (InvalidOperationException ex) when (ReferenceEquals(ex, PrimaryFailure))
                    {
                        LeasedTargetsObservedByCaller = getLeasedTargetCount();
                        throw;
                    }
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent);
            context.Publish(context.TargetScope(transient, caller));
        }
    }

    private sealed class TargetCallbackFailureNode(TargetCallbackFailure failurePoint) : RenderNode
    {
        public int CallbackEntries { get; private set; }

        public Action? VerifyRetainedSession { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            if (failurePoint is TargetCallbackFailure.RawCommandCallback)
            {
                RawTargetCommandDescription rawCommand = RawTargetCommandDescription.CreateRequestLocal(
                    session =>
                    {
                        CallbackEntries++;
                        VerifyRetainedSession = () => _ = session.Intent;
                        throw new InvalidOperationException("raw-command-primary");
                    },
                    s_bounds,
                    RenderHitTestContract.OutputBounds);
                context.Publish(context.RawTargetCommand(rawCommand));
                return;
            }

            RenderFragmentHandle source = context.OpaqueSource(FailureTestSupport.SourceDescription());
            if (failurePoint is TargetCallbackFailure.CommandCallback
                or TargetCallbackFailure.UndeclaredTargetReadback
                or TargetCallbackFailure.MissingTargetReadback
                or TargetCallbackFailure.UndeclaredInputReadback
                or TargetCallbackFailure.DuplicateInputReadback)
            {
                bool targetReadback = failurePoint == TargetCallbackFailure.MissingTargetReadback;
                bool inputReadback = failurePoint == TargetCallbackFailure.DuplicateInputReadback;
                TargetCommandDescription command = TargetCommandDescription.CreateRequestLocal(
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
                    inputReadbacks: inputReadback ? [RenderInputReadback.Values([0])] : null);
                context.Publish(source);
                context.Publish(context.TargetCommand([source], command));
                return;
            }

            if (failurePoint is TargetCallbackFailure.ScopeCallback
                or TargetCallbackFailure.ScopeMissingReplay
                or TargetCallbackFailure.ScopeDoubleReplay)
            {
                TargetScopeDescription scope = TargetScopeDescription.CreateRequestLocal(
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
                    deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent);
                context.Publish(context.TargetScope(source, scope));
                return;
            }

            throw new ArgumentOutOfRangeException(nameof(failurePoint), failurePoint, null);
        }
    }

    private sealed class NestedPlanningFailureParentNode(RenderNode child) : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            _ = context.RecordNestedTargetAtScale(child, s_bounds, 1);
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                session =>
                {
                    ExecuteCalls++;
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                })));
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
                }));
            RenderBoundsContract bounds = RenderBoundsContract.Create(
                PrimaryFailure,
                static (_, value) => value,
                static (failure, _) => throw failure);
            ShaderDescription shader = ShaderDescription.WholeSource(
                "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
                bounds);
            context.Publish(context.Shader(source, shader));
        }
    }

    private sealed class NestedExecutionNode(
        string name,
        ICollection<string> executionOrder,
        RenderNode? nestedRoot = null,
        Exception? failure = null) : RenderNode
    {
        public int ExecuteCalls { get; private set; }

        public RecordedNestedRenderTarget? NestedRequest { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            if (nestedRoot is not null)
            {
                NestedRequest = context.RecordNestedTargetAtScale(nestedRoot, s_bounds, 1);
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
                })));
        }
    }

    private sealed class NestedContainerNode : ContainerRenderNode
    {
        private readonly RenderNode _nestedRoot;

        public NestedContainerNode(
            RenderNode nestedRoot,
            params RenderNode[] parentChildren)
        {
            _nestedRoot = nestedRoot;
            foreach (RenderNode child in parentChildren)
                AddChild(child);
        }

        public RecordedNestedRenderTarget? NestedRequest { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            NestedRequest = context.RecordNestedTargetAtScale(_nestedRoot, s_bounds, 1);
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
                _ = context.Own(Resources[index]);
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription(
                _ => throw _primary)));
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
                })));
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
                _ = context.Own(Resources[index]);
            context.Publish(context.OpaqueSource(FailureTestSupport.SourceDescription()));
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
