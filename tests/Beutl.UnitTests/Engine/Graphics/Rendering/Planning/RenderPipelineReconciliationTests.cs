using System.Collections.Immutable;

using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RenderPipelineReconciliationTests
{
    [Test]
    public void MetadataPipeline_PublishesMetadataOnlySnapshotFromRecorderAndCompilerHooks()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new ClearRenderNode(new Color(255, 12, 34, 56));
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 32, 24),
            Diagnostics = diagnostics,
            UseRenderCache = false,
        });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 32, 24)));
            Assert.That(diagnostics.Latest.Purpose, Is.EqualTo(RenderRequestPurpose.Bounds));
            Assert.That(diagnostics.Latest.Succeeded, Is.True);
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RecordedFragments], Is.EqualTo(1));
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RecordedTargetCommands], Is.EqualTo(1));
            Assert.That(diagnostics.Latest[RenderPipelineCounter.MetadataOutcomes], Is.EqualTo(1));
            Assert.That(diagnostics.LatestFrame, Is.SameAs(RenderPipelineDiagnosticSnapshot.Empty));
        });
    }

    [Test]
    public void ExecutionPipeline_PublishesAfterExecutionAndIsolatesObserverFailures()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        var observed = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += _ => throw new InvalidOperationException("observer-fault");
        diagnostics.RequestCompleted += observed.Add;
        using var node = new ClearRenderNode(new Color(255, 12, 34, 56));
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 16, 16),
            Diagnostics = diagnostics,
            UseRenderCache = false,
        });
        using RenderTarget target = RenderTarget.CreateNull(16, 16);
        using var canvas = new ImmediateCanvas(target);

        Assert.That(() => renderer.Render(canvas), Throws.Nothing);

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.EqualTo(new[] { snapshot }));
            Assert.That(snapshot.Purpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
            Assert.That(snapshot.Succeeded, Is.True);
            Assert.That(snapshot[RenderPipelineCounter.RecordedFragments], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExternalRootResources], Is.EqualTo(1));
            Assert.That(snapshot.Events[^1].Kind, Is.EqualTo(RenderPipelineDiagnosticEventKind.RequestCompleted));
        });
    }

    [Test]
    public void WarmedExecution_ClassifiesPoolCreateThenExactSizeHit()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new CacheableWorkNode();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 16, 16),
            Diagnostics = diagnostics,
            UseRenderCache = false,
        });
        using RenderTarget target = RenderTarget.CreateNull(16, 16);
        using var canvas = new ImmediateCanvas(target);

        renderer.Render(canvas);
        RenderPipelineDiagnosticSnapshot first = diagnostics.Latest;
        renderer.Render(canvas);
        RenderPipelineDiagnosticSnapshot warmed = diagnostics.Latest;

        Assert.Multiple(() =>
        {
            Assert.That(first[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(1));
            Assert.That(first[RenderPipelineCounter.IntermediateCreates], Is.EqualTo(1));
            Assert.That(first[RenderPipelineCounter.PoolMisses], Is.EqualTo(1));
            Assert.That(first[RenderPipelineCounter.PoolHits], Is.Zero);
            Assert.That(warmed[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(1));
            Assert.That(warmed[RenderPipelineCounter.IntermediateCreates], Is.Zero);
            Assert.That(warmed[RenderPipelineCounter.PoolMisses], Is.Zero);
            Assert.That(warmed[RenderPipelineCounter.PoolHits], Is.EqualTo(1));
            Assert.That(warmed[RenderPipelineCounter.IntermediateDischarges], Is.EqualTo(1));
        });
    }

    [Test]
    public void WarmedRenderCache_ReconcilesTheProducerAsCachedWithoutExecutingItAgain()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new CacheableWorkNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 16, 16),
            Diagnostics = diagnostics,
            UseRenderCache = true,
            RenderPurpose = RenderRequestPurpose.Frame,
        });

        using RenderNodeRasterization cold = renderer.Rasterize();
        RenderPipelineDiagnosticSnapshot coldSnapshot = diagnostics.Latest;
        using RenderNodeRasterization warmed = renderer.Rasterize();
        RenderPipelineDiagnosticSnapshot warmedSnapshot = diagnostics.Latest;

        long terminalOutcomes =
            warmedSnapshot[RenderPipelineCounter.ExecutedOutcomes]
            + warmedSnapshot[RenderPipelineCounter.CachedOutcomes]
            + warmedSnapshot[RenderPipelineCounter.MetadataOutcomes]
            + warmedSnapshot[RenderPipelineCounter.SkippedOutcomes]
            + warmedSnapshot[RenderPipelineCounter.FailedOutcomes];
        Assert.Multiple(() =>
        {
            Assert.That(cold.IsEmpty, Is.False);
            Assert.That(warmed.IsEmpty, Is.False);
            Assert.That(coldSnapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(coldSnapshot[RenderPipelineCounter.RenderCacheCaptures], Is.EqualTo(1));
            Assert.That(coldSnapshot[RenderPipelineCounter.RejectedRenderCacheCaptures], Is.Zero);
            Assert.That(warmedSnapshot[RenderPipelineCounter.CachedOutcomes], Is.EqualTo(1));
            Assert.That(warmedSnapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.Zero);
            Assert.That(node.ExecuteCount, Is.EqualTo(1),
                "A selected warmed cache entry must not execute its producer again.");
            Assert.That(
                terminalOutcomes,
                Is.EqualTo(warmedSnapshot[RenderPipelineCounter.RecordedFragments]));
        });
    }

    [Test]
    public void OpaqueCallbackWithoutOutput_ExecutesWithoutInventingAGpuPass()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new NoOutputOpaqueNode();
        using var renderer = CreateRenderer(node, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot.Events, Has.None.Matches<RenderPipelineDiagnosticEvent>(static item =>
                item.Kind == RenderPipelineDiagnosticEventKind.PassExecuted));
        });
    }

    [Test]
    public void TargetCommandWithNoOpCanvasUse_ExecutesWithoutInventingAGpuPass()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new NoDrawTargetCommandNode();
        using var renderer = CreateRenderer(node, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot.Events, Has.None.Matches<RenderPipelineDiagnosticEvent>(static item =>
                item.Kind == RenderPipelineDiagnosticEventKind.PassExecuted));
        });
    }

    [Test]
    public void EmptyTargetCommand_ExecutesForOrderingWithoutPlanningOrExecutingAGpuPass()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new EmptyTargetCommandNode();
        using var renderer = CreateRenderer(node, diagnostics);
        using RenderTarget target = RenderTarget.CreateNull(16, 16);
        using var canvas = new ImmediateCanvas(target);

        renderer.Render(canvas);

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
        });
    }

    [Test]
    public void OpaqueAllocatedThenUnpublished_RecordsTheAllocationPass()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new UnpublishedOutputOpaqueNode();
        using var renderer = CreateRenderer(node, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
        });
    }

    [Test]
    public void GeometryDiscardAfterAllocation_RecordsTheAllocationPass()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new DiscardingGeometryNode();
        using var renderer = CreateRenderer(node, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.GeometryExecutions, Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(2));
        });
    }

    [Test]
    public void GeometryWithNoRuntimeInput_IsSkippedWithoutInventingAGpuPass()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new EmptyInputGeometryNode();
        using var renderer = CreateRenderer(node, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.SourceExecutions, Is.EqualTo(1));
            Assert.That(node.GeometryExecutions, Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.SkippedOutcomes], Is.EqualTo(1));
        });
    }

    [Test]
    public void DegenerateCompiledShaderRun_IsSkippedWithoutPassOrFusedStageEvidence()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new DegenerateShaderRunNode();
        using var renderer = CreateRenderer(node, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.SourceExecutions, Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(4));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.FusedStages], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.SkippedOutcomes], Is.EqualTo(3));
        });
    }

    [Test]
    public void DeclaredReadbackWithoutSnapshot_PlansButDoesNotExecuteSynchronization()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new UnusedReadbackNode();
        using var renderer = CreateRenderer(node, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(node.GeometryExecutions, Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.Synchronizations], Is.Zero);
            Assert.That(snapshot.Events, Has.Some.Matches<RenderPipelineDiagnosticEvent>(static item =>
                item.Kind == RenderPipelineDiagnosticEventKind.SynchronizationPlanned));
            Assert.That(snapshot.Events, Has.None.Matches<RenderPipelineDiagnosticEvent>(static item =>
                item.Kind == RenderPipelineDiagnosticEventKind.SynchronizationExecuted));
        });
    }

    [Test]
    public void CacheableProducerFailureBeforeStaging_DoesNotRejectASelectedOnlyCapture()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new ThrowingCacheableWorkNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 16, 16),
            Diagnostics = diagnostics,
            UseRenderCache = true,
            RenderPurpose = RenderRequestPurpose.Frame,
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize())!;

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("producer-fault"));
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RenderCacheMisses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RenderCacheCaptures], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.RejectedRenderCacheCaptures], Is.Zero);
        });
    }

    [Test]
    public void AllocationFailure_RecordsPoolMissWithoutInventingOwnedLease()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new CacheableWorkNode();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 16, 16),
            Diagnostics = diagnostics,
            TargetFactory = new ThrowingTargetFactory(),
            UseRenderCache = false,
        });
        using RenderTarget target = RenderTarget.CreateNull(16, 16);
        using var canvas = new ImmediateCanvas(target);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => renderer.Render(canvas))!;

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("allocation-fault"));
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Allocation));
            Assert.That(snapshot[RenderPipelineCounter.PoolMisses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateAcquires], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.IntermediateCreates], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.IntermediateDischarges], Is.Zero);
        });
    }

    [Test]
    public void RecordingFailure_PreservesOriginalExceptionAndPublishesRecordingPhase()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new ThrowingNode();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            Diagnostics = diagnostics,
            UseRenderCache = false,
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => renderer.Measure())!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("recording-fault"));
            Assert.That(diagnostics.Latest.Succeeded, Is.False);
            Assert.That(diagnostics.Latest.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Recording));
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RecordedFragments], Is.Zero);
            Assert.That(diagnostics.Latest[RenderPipelineCounter.Failures], Is.EqualTo(1));
        });
    }

    [Test]
    public void CompleteRequest_ReconcilesOverlappingClassificationsAndLeaseOwnership()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var request = CreateRequest(diagnostics);
        RenderPipelineDiagnosticRecorder recorder = Start(request);
        RenderFragmentReference[] fragments =
        [
            CreateFragment(request, 1, RenderFragmentKind.MaterializedInput, hasValue: true),
            CreateFragment(request, 2, RenderFragmentKind.TargetCommand, hasValue: false),
            CreateFragment(request, 3, RenderFragmentKind.TargetCapture, hasValue: true),
            CreateFragment(request, 4, RenderFragmentKind.TargetScope, hasValue: false),
            CreateFragment(request, 5, RenderFragmentKind.Layer, hasValue: true),
        ];
        recorder.RecordCommittedFragments(ToEntries(fragments));

        recorder.RecordExternalRootResource();
        recorder.RecordIntermediateAcquired(created: true, poolHit: false);
        foreach (RenderFragmentReference fragment in fragments)
            recorder.RecordFragmentExecuted(fragment.Id!.Value.Value);
        recorder.RecordIntermediateDischarged();
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Succeeded, Is.True);
            Assert.That(snapshot[RenderPipelineCounter.RecordedFragments], Is.EqualTo(5));
            Assert.That(snapshot[RenderPipelineCounter.RecordedMaterializableValues], Is.EqualTo(3));
            Assert.That(snapshot[RenderPipelineCounter.RecordedTargetCommands], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RecordedTargetCaptures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RecordedTargetScopes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RecordedLayers], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(5));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateCreates], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.PoolMisses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateDischarges], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.PeakLiveIntermediates], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExternalRootResources], Is.EqualTo(1));
            Assert.That(snapshot.Events.Select(static item => item.Sequence),
                Is.EqualTo(Enumerable.Range(0, snapshot.Events.Count).Select(static item => (long)item)));
        });
    }

    [Test]
    public void PlannedAndExecutedWork_EmitsExactPassSynchronizationBoundaryAndProgramEvidence()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var request = CreateRequest(diagnostics);
        RenderPipelineDiagnosticRecorder recorder = Start(request);
        RenderFragmentReference pass = CreateFragment(request, 1, RenderFragmentKind.Geometry, hasValue: true);
        RenderFragmentReference readback = CreateFragment(request, 2, RenderFragmentKind.TargetCommand, hasValue: false);
        recorder.RecordCommittedFragments(ToEntries([pass, readback]));

        var plan = new ExecutionIslandPlan(
            [
                new ExecutionIsland(
                    new ExecutionIslandId(1),
                    ExecutionIslandKind.Compatibility,
                    [pass.Id!.Value],
                    plansGpuPass: true),
                new ExecutionIsland(
                    new ExecutionIslandId(2),
                    ExecutionIslandKind.Readback,
                    [readback.Id!.Value],
                    plansGpuPass: true),
            ],
            [
                new ExecutionIslandBoundary(
                    null,
                    pass.Id,
                    ExecutionIslandBoundaryReason.Geometry,
                    []),
                new ExecutionIslandBoundary(
                    pass.Id,
                    readback.Id,
                    ExecutionIslandBoundaryReason.Readback,
                    []),
            ]);
        recorder.RecordPlan(plan);
        recorder.RecordProgramCacheDecision(pass.Id.Value.Value, cacheHit: false);
        recorder.RecordGpuPassExecuted(pass.Id.Value.Value);
        recorder.RecordFragmentExecuted(pass.Id.Value.Value);
        recorder.RecordSynchronizationExecuted(readback.Id.Value.Value);
        recorder.RecordFragmentExecuted(readback.Id.Value.Value);
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot[RenderPipelineCounter.ExecutionIslands], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.Synchronizations], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ProgramMisses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ProgramCreations], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.OpaqueBoundaries], Is.EqualTo(2));
            Assert.That(snapshot.Events.Count(static item => item.Kind == RenderPipelineDiagnosticEventKind.PassPlanned),
                Is.EqualTo(2));
            Assert.That(snapshot.Events.Count(static item => item.Kind == RenderPipelineDiagnosticEventKind.PassExecuted),
                Is.EqualTo(1));
            Assert.That(snapshot.Events.Count(static item =>
                    item.Kind == RenderPipelineDiagnosticEventKind.SynchronizationPlanned),
                Is.EqualTo(1));
            Assert.That(snapshot.Events.Count(static item =>
                    item.Kind == RenderPipelineDiagnosticEventKind.SynchronizationExecuted),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void ExecutedOutcome_DoesNotInferGpuPassExecution()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var request = CreateRequest(diagnostics);
        RenderPipelineDiagnosticRecorder recorder = Start(request);
        RenderFragmentReference fragment = CreateFragment(
            request,
            1,
            RenderFragmentKind.Geometry,
            hasValue: true);
        recorder.RecordCommittedFragments(ToEntries([fragment]));
        recorder.RecordPlan(new ExecutionIslandPlan(
            [new ExecutionIsland(
                new ExecutionIslandId(1),
                ExecutionIslandKind.Compatibility,
                [fragment.Id!.Value],
                plansGpuPass: true)],
            [new ExecutionIslandBoundary(
                null,
                fragment.Id,
                ExecutionIslandBoundaryReason.Geometry,
                [])]));

        recorder.RecordFragmentExecuted(fragment.Id.Value.Value);
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot.Events, Has.None.Matches<RenderPipelineDiagnosticEvent>(static item =>
                item.Kind == RenderPipelineDiagnosticEventKind.PassExecuted));
        });
    }

    [Test]
    public void Failure_PreservesPrimaryPhaseSkipsDependentsRejectsCaptureAndDischargesLease()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var request = CreateRequest(diagnostics);
        RenderPipelineDiagnosticRecorder recorder = Start(request);
        RenderFragmentReference[] fragments =
        [
            CreateFragment(request, 1, RenderFragmentKind.MaterializedInput, hasValue: true),
            CreateFragment(request, 2, RenderFragmentKind.Geometry, hasValue: true),
            CreateFragment(request, 3, RenderFragmentKind.ContributeValues, hasValue: true),
        ];
        recorder.RecordCommittedFragments(ToEntries(fragments));
        recorder.RecordIntermediateAcquired(created: true, poolHit: false);
        recorder.RecordFragmentExecuted(1);
        recorder.RecordFailure(RenderPipelineFailurePhase.Allocation, 2);
        recorder.RecordCacheCaptureStaged(1);
        recorder.RecordCleanupFailure();
        recorder.RecordIntermediateDischarged();
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        int failureIndex = snapshot.Events.ToList().FindIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.Failure);
        int cleanupIndex = snapshot.Events.ToList().FindIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.CleanupFailure);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Succeeded, Is.False);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Allocation));
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.FailedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.SkippedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.IntermediateDischarges], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.RenderCacheCaptures], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.RejectedRenderCacheCaptures], Is.EqualTo(1));
            Assert.That(failureIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(cleanupIndex, Is.GreaterThan(failureIndex));
        });
    }

    [Test]
    public void NestedAndMetadataRequests_PublishIndependentScopesWithoutReplacingLatestFrame()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        var observed = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += observed.Add;
        using var parent = CreateRequest(diagnostics);
        using var binding = new NestedRenderTargetBinding();
        using var child = new RenderRequest(parent.Options.CreateNested(binding), parent);
        RenderPipelineDiagnosticRecorder parentRecorder = Start(parent);
        RenderPipelineDiagnosticRecorder childRecorder = Start(child);
        parentRecorder.RecordCommittedFragments(ToEntries(
            [CreateFragment(parent, 1, RenderFragmentKind.Layer, hasValue: true)]));
        childRecorder.RecordCommittedFragments(ToEntries(
            [CreateFragment(child, 1, RenderFragmentKind.Geometry, hasValue: true)]));
        parentRecorder.RecordNestedRequest(child.Id);

        childRecorder.RecordFragmentExecuted(1);
        childRecorder.Complete();
        parentRecorder.RecordFragmentExecuted(1);
        parentRecorder.Complete();
        RenderPipelineDiagnosticSnapshot frame = diagnostics.LatestFrame;

        using var bounds = CreateRequest(diagnostics, RenderRequestPurpose.Bounds);
        RenderPipelineDiagnosticRecorder boundsRecorder = Start(bounds);
        boundsRecorder.RecordCommittedFragments(ToEntries(
            [CreateFragment(bounds, 1, RenderFragmentKind.Geometry, hasValue: true)]));
        boundsRecorder.RecordAllOutcomes(RenderPipelineOutcome.Metadata);
        boundsRecorder.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(observed, Has.Count.EqualTo(3));
            Assert.That(observed[0].ParentRequestId, Is.EqualTo(parent.Id.Value));
            Assert.That(observed[1].Events.Single(static item =>
                    item.Kind == RenderPipelineDiagnosticEventKind.NestedRequest).RelatedRequestId,
                Is.EqualTo(child.Id.Value));
            Assert.That(diagnostics.Latest.Purpose, Is.EqualTo(RenderRequestPurpose.Bounds));
            Assert.That(diagnostics.Latest[RenderPipelineCounter.MetadataOutcomes], Is.EqualTo(1));
            Assert.That(diagnostics.LatestFrame, Is.SameAs(frame));
            Assert.That(frame.RequestId, Is.EqualTo(parent.Id.Value));
        });
    }

    [Test]
    public void OpaqueExternalWork_DistinguishesRecordedBoundaryFromCallbackEntry()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var request = CreateRequest(diagnostics);
        RenderPipelineDiagnosticRecorder recorder = Start(request);
        RenderFragmentReference fragment = CreateFragment(
            request,
            1,
            RenderFragmentKind.LegacyFilterEffect,
            hasValue: true);
        recorder.RecordCommittedFragments(ToEntries([fragment]));
        recorder.RecordPlan(new ExecutionIslandPlan(
            [new ExecutionIsland(
                new ExecutionIslandId(1),
                ExecutionIslandKind.Compatibility,
                [fragment.Id!.Value],
                plansGpuPass: false)],
            [new ExecutionIslandBoundary(
                null,
                fragment.Id,
                ExecutionIslandBoundaryReason.LegacyCustomEffect,
                [])]));
        recorder.RecordOpaqueExecution(1);
        recorder.RecordFragmentExecuted(1);
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HasOpaqueExternalWork, Is.True);
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.ExecutedGpuPasses], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.OpaqueBoundaries], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.OpaqueExternalExecutions], Is.EqualTo(1));
            Assert.That(snapshot.Events, Has.None.Matches<RenderPipelineDiagnosticEvent>(static item =>
                item.Kind is RenderPipelineDiagnosticEventKind.PassPlanned
                    or RenderPipelineDiagnosticEventKind.PassExecuted));
            Assert.That(snapshot.Events.Single(static item =>
                    item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned).BoundaryReason,
                Is.EqualTo(RenderPipelineBoundaryReason.LegacyCustomEffect));
        });
    }

    [Test]
    public void OpaqueExternalBoundary_WithoutCallbackEntryReconcilesAsSkipped()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var request = CreateRequest(diagnostics);
        RenderPipelineDiagnosticRecorder recorder = Start(request);
        RenderFragmentReference fragment = CreateFragment(
            request,
            1,
            RenderFragmentKind.LegacyFilterEffect,
            hasValue: true);
        recorder.RecordCommittedFragments(ToEntries([fragment]));
        recorder.RecordPlan(new ExecutionIslandPlan(
            [new ExecutionIsland(
                new ExecutionIslandId(1),
                ExecutionIslandKind.Compatibility,
                [fragment.Id!.Value],
                plansGpuPass: false)],
            [new ExecutionIslandBoundary(
                null,
                fragment.Id,
                ExecutionIslandBoundaryReason.LegacyCustomEffect,
                [])]));
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HasOpaqueExternalWork, Is.True);
            Assert.That(snapshot[RenderPipelineCounter.OpaqueBoundaries], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.OpaqueExternalExecutions], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.SkippedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.Zero);
            Assert.That(snapshot.Events.Single(static item =>
                    item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned).BoundaryReason,
                Is.EqualTo(RenderPipelineBoundaryReason.LegacyCustomEffect));
        });
    }

    [Test]
    public void LegacyFilterSetupFailureBeforeCallbackEntry_DoesNotRecordOpaqueExecution()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: new Rect(0, 0, 16, 16),
            owner: owner,
            diagnostics: diagnostics);
        using var request = new RenderRequest(options);
        using var node = new LegacyFilterSetupFailureNode();
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        RenderFragmentReference legacy = graph.Fragments
            .Select(static item => (RenderFragmentReference)item.Payload!)
            .Single(static item => item.Kind == RenderFragmentKind.LegacyFilterEffect);
        var payload = (LegacyFilterEffectRenderFragmentPayload)legacy.Payload!;
        owner.ResourceRegistry.Release(payload.Context);
        using RenderTarget destination = RenderTarget.CreateNull(16, 16);
        using var canvas = new ImmediateCanvas(destination);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas))!;

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("not committed"));
            Assert.That(snapshot.HasOpaqueExternalWork, Is.True);
            Assert.That(snapshot[RenderPipelineCounter.OpaqueExternalExecutions], Is.Zero);
        });
    }

    [Test]
    public void UnpublishedFragment_IsSkippedWithoutPlanningOrExecutingItsCallback()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new UnpublishedWorkNode();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 16, 16),
            Diagnostics = diagnostics,
            UseRenderCache = false,
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(node.PublishedExecutions, Is.EqualTo(1));
            Assert.That(node.UnpublishedExecutions, Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.RecordedFragments], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ExecutedOutcomes], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.SkippedOutcomes], Is.EqualTo(1));
        });
    }

    private static RenderRequest CreateRequest(
        IRenderPipelineDiagnosticsState diagnostics,
        RenderRequestPurpose purpose = RenderRequestPurpose.Frame)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            purpose,
            targetDomain: new Rect(0, 0, 64, 64),
            diagnostics: diagnostics));

    private static RenderPipelineDiagnosticRecorder Start(RenderRequest request)
        => RenderPipelineDiagnosticRecorder.Start(request, "ReconciliationRoot")!;

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        IRenderPipelineDiagnosticsState diagnostics)
        => new(node, new RenderNodeRendererOptions
        {
            TargetDomain = new Rect(0, 0, 16, 16),
            Diagnostics = diagnostics,
            UseRenderCache = false,
        });

    private static RenderFragmentReference CreateFragment(
        RenderRequest request,
        long id,
        RenderFragmentKind kind,
        bool hasValue)
    {
        var result = new RenderFragmentReference(
            kind,
            new Rect(0, 0, 16, 16),
            EffectiveScale.At(1),
            hasValue ? RenderValueCardinality.Single : RenderValueCardinality.None,
            contributesValuesToTarget: hasValue,
            canBeUsedAsValueInput: hasValue,
            hasTargetEffects: !hasValue,
            hasOpaqueExternalWork: kind is RenderFragmentKind.LegacyFilterEffect
                or RenderFragmentKind.RawTargetCommand
                or RenderFragmentKind.RawTargetScope,
            inputs: null,
            payload: null,
            hitTest: null)
        {
            Id = new RenderFragmentId(request.Id, id),
            ValueIds = hasValue ? [new RenderValueId(request.Id, id)] : [],
        };
        return result;
    }

    private static ImmutableArray<RecordedRenderFragmentEntry> ToEntries(
        IEnumerable<RenderFragmentReference> fragments)
        => [.. fragments.Select(static item => new RecordedRenderFragmentEntry(item, item, "Test"))];

    private sealed class ThrowingNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => throw new InvalidOperationException("recording-fault");
    }

    private sealed class CacheableWorkNode : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    ExecuteCount++;
                    using OpaqueRenderOutput output = session.CreateOutput(new Rect(0, 0, 16, 16));
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(CacheableWorkNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(CacheableWorkNode)))));
        }
    }

    private sealed class NoOutputOpaqueNode : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                _ => ExecuteCount++,
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.ZeroOrOne,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(NoOutputOpaqueNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(NoOutputOpaqueNode)))));
        }
    }

    private sealed class EmptyInputGeometryNode : RenderNode
    {
        public int SourceExecutions { get; private set; }

        public int GeometryExecutions { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle empty = context.OpaqueSource(OpaqueRenderDescription.Create(
                _ => SourceExecutions++,
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.ZeroOrOne,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(EmptyInputGeometryNode), "source"),
                runtimeIdentity: new RenderRuntimeIdentity("empty-source")));
            context.Publish(context.Geometry(empty, GeometryDescription.Create(
                session =>
                {
                    GeometryExecutions++;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: (typeof(EmptyInputGeometryNode), "geometry"),
                runtimeIdentity: new RenderRuntimeIdentity("empty-input-geometry"))));
        }
    }

    private sealed class NoDrawTargetCommandNode : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.TargetCommand([], TargetCommandDescription.Create(
                session =>
                {
                    ExecuteCount++;
                    session.Canvas.Use(_ => { });
                },
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None,
                TargetAccess.ReadWrite,
                structuralKey: typeof(NoDrawTargetCommandNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(NoDrawTargetCommandNode)))));
        }
    }

    private sealed class EmptyTargetCommandNode : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.TargetCommand([], TargetCommandDescription.Create(
                _ => ExecuteCount++,
                TargetRegion.Empty,
                Rect.Empty,
                RenderHitTestContract.None,
                TargetAccess.ReadWrite,
                structuralKey: typeof(EmptyTargetCommandNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(EmptyTargetCommandNode)))));
        }
    }

    private sealed class UnpublishedOutputOpaqueNode : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    ExecuteCount++;
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.ZeroOrOne,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(UnpublishedOutputOpaqueNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(UnpublishedOutputOpaqueNode)))));
        }
    }

    private sealed class DiscardingGeometryNode : RenderNode
    {
        public int GeometryExecutions { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(DiscardingGeometryNode), "source"),
                runtimeIdentity: new RenderRuntimeIdentity("discarding-geometry-source")));
            context.Publish(context.Geometry(source, GeometryDescription.Create(
                session =>
                {
                    GeometryExecutions++;
                    session.DiscardOutput();
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: (typeof(DiscardingGeometryNode), "geometry"),
                runtimeIdentity: new RenderRuntimeIdentity("discarding-geometry"))));
        }
    }

    private sealed class DegenerateShaderRunNode : RenderNode
    {
        private static readonly ShaderDescription s_first = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        private static readonly ShaderDescription s_second = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.bgr, color.a); }");

        public int SourceExecutions { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                _ => SourceExecutions++,
                RenderOperationBoundsContract.Source(new Rect(4, 5, 0, 6)),
                RenderHitTestContract.None,
                RenderValueCardinality.ZeroOrOne,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(DegenerateShaderRunNode), "source"),
                runtimeIdentity: new RenderRuntimeIdentity("degenerate-source")));
            RenderFragmentHandle current = context.Shader(source, s_first);
            current = context.Shader(current, s_second);
            context.Publish(current);
            context.Publish(context.TargetCommand([], TargetCommandDescription.Create(
                session => session.Canvas.Use(canvas => canvas.Clear(Colors.Transparent)),
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None,
                TargetAccess.ReadWrite,
                structuralKey: (typeof(DegenerateShaderRunNode), "clear"),
                runtimeIdentity: new RenderRuntimeIdentity("degenerate-clear"))));
        }
    }

    private sealed class UnusedReadbackNode : RenderNode
    {
        public int GeometryExecutions { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(UnusedReadbackNode), "source"),
                runtimeIdentity: new RenderRuntimeIdentity("unused-readback-source")));
            context.Publish(context.Geometry(source, GeometryDescription.Create(
                session =>
                {
                    GeometryExecutions++;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: (typeof(UnusedReadbackNode), "geometry"),
                runtimeIdentity: new RenderRuntimeIdentity("unused-readback-geometry"),
                requiresReadback: true)));
        }
    }

    private sealed class ThrowingCacheableWorkNode : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                _ =>
                {
                    ExecuteCount++;
                    throw new InvalidOperationException("producer-fault");
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(ThrowingCacheableWorkNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(ThrowingCacheableWorkNode)))));
        }
    }

    private sealed class LegacyFilterSetupFailureNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(LegacyFilterSetupFailureNode), "source"),
                runtimeIdentity: new RenderRuntimeIdentity("legacy-setup-source")));
            FilterEffectContext? effectContext = new(new Rect(0, 0, 16, 16));
            try
            {
                RenderResource<FilterEffectContext> resource = context.Own(
                    effectContext,
                    (typeof(LegacyFilterSetupFailureNode), "effect"),
                    version: 1);
                effectContext = null;
                context.Publish(context.LegacyFilterEffect(
                    [source],
                    resource,
                    new Rect(0, 0, 16, 16)));
            }
            finally
            {
                effectContext?.Dispose();
            }
        }
    }

    private sealed class UnpublishedWorkNode : RenderNode
    {
        public int PublishedExecutions { get; private set; }

        public int UnpublishedExecutions { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle published = context.OpaqueSource(CreateDescription(
                "published",
                () => PublishedExecutions++));
            _ = context.OpaqueSource(CreateDescription(
                "unpublished",
                () => UnpublishedExecutions++));
            context.Publish(published);
        }

        private static OpaqueRenderDescription CreateDescription(string key, Action onExecute)
            => OpaqueRenderDescription.Create(
                session =>
                {
                    onExecute();
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 16, 16)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: key,
                runtimeIdentity: new RenderRuntimeIdentity(key));
    }

    private sealed class ThrowingTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => throw new InvalidOperationException("allocation-fault");
    }
}
