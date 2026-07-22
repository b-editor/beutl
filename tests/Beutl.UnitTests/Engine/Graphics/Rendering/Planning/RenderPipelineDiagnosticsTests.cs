using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RenderPipelineDiagnosticsTests
{
    [Test]
    public void DiagnosticsTypes_AreInternal()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IRenderPipelineDiagnosticsState).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineDiagnosticsState).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineDiagnosticSnapshot).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineDiagnosticEvent).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineDiagnosticEventKind).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineBoundaryReason).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineOutcome).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineFailurePhase).IsNotPublic, Is.True);
            Assert.That(typeof(RenderPipelineCounter).IsNotPublic, Is.True);
        });
    }

    [Test]
    public void EmptySnapshot_IsStableAndReadsMissingCountersAsZero()
    {
        RenderPipelineDiagnosticSnapshot snapshot = RenderPipelineDiagnosticSnapshot.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(RenderPipelineDiagnosticSnapshot.Empty, Is.SameAs(snapshot));
            Assert.That(snapshot.RequestId, Is.Zero);
            Assert.That(snapshot.ParentRequestId, Is.Null);
            Assert.That(snapshot.RootTargetClass, Is.Empty);
            Assert.That(snapshot.Counters, Is.Empty);
            Assert.That(snapshot.Events, Is.Empty);
            Assert.That(snapshot[RenderPipelineCounter.RecordedFragments], Is.Zero);
        });
    }

    [Test]
    public void Create_CopiesInputsAndExposesAnImmutableSnapshot()
    {
        var counters = CreateCounters();
        var events = CreateDefaultEvents(
            succeeded: true,
            failurePhase: null,
            RenderRequestPurpose.Frame,
            counters);

        RenderPipelineDiagnosticSnapshot snapshot = CreateSnapshot(
            requestId: 7,
            parentRequestId: 3,
            intent: RenderIntent.Delivery,
            purpose: RenderRequestPurpose.Frame,
            rootTargetClass: "Presentation",
            counters: counters,
            events: events);

        counters[RenderPipelineCounter.RecordedFragments] = 99;
        events.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.RequestId, Is.EqualTo(7));
            Assert.That(snapshot.ParentRequestId, Is.EqualTo(3));
            Assert.That(snapshot.Intent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(snapshot.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
            Assert.That(snapshot.Succeeded, Is.True);
            Assert.That(snapshot.HasOpaqueExternalWork, Is.False);
            Assert.That(snapshot.RootTargetClass, Is.EqualTo("Presentation"));
            Assert.That(snapshot.FailurePhase, Is.Null);
            Assert.That(snapshot[RenderPipelineCounter.RecordedFragments], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.ProgramHits], Is.Zero);
            Assert.That(snapshot.Events, Has.Count.EqualTo(4));
            Assert.That(snapshot.Events.Select(item => item.Sequence), Is.EqualTo(new long[] { 0, 1, 2, 3 }));
        });

        if (snapshot.Counters is IDictionary<RenderPipelineCounter, long> mutableCounters)
        {
            Assert.That(
                () => mutableCounters[RenderPipelineCounter.RecordedFragments] = 2,
                Throws.TypeOf<NotSupportedException>());
        }

        if (snapshot.Events is IList<RenderPipelineDiagnosticEvent> mutableEvents)
        {
            Assert.That(
                () => mutableEvents.Clear(),
                Throws.TypeOf<NotSupportedException>());
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_RejectsNonPositiveRequestId(long requestId)
    {
        Assert.That(
            () => CreateSnapshot(requestId: requestId),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(5)]
    public void Create_RejectsInvalidParentRequestId(long parentRequestId)
    {
        Assert.That(
            () => CreateSnapshot(requestId: 5, parentRequestId: parentRequestId),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_RejectsEmptyRootTargetClass(string? rootTargetClass)
    {
        Assert.That(
            () => CreateSnapshot(rootTargetClass: rootTargetClass!),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsNegativeCounter()
    {
        var counters = CreateCounters();
        counters[RenderPipelineCounter.ProgramHits] = -1;

        Assert.That(
            () => CreateSnapshot(counters: counters),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsNonGapFreeEventSequence()
    {
        List<RenderPipelineDiagnosticEvent> events =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(2, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];

        Assert.That(
            () => CreateSnapshot(events: events),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsMissingDuplicateOrMisorderedLifecycleEvents()
    {
        RenderPipelineDiagnosticEvent[] missingStart =
        [
            Event(0, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(1, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(2, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];
        RenderPipelineDiagnosticEvent[] duplicateStart =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(2, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(3, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(4, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];
        RenderPipelineDiagnosticEvent[] eventAfterCompletion =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.RequestCompleted),
            Event(3, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
        ];
        RenderPipelineDiagnosticEvent[] duplicateCompletion =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(3, RenderPipelineDiagnosticEventKind.RequestCompleted),
            Event(4, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];

        Assert.Multiple(() =>
        {
            AssertInvalidEvents(missingStart);
            AssertInvalidEvents(duplicateStart);
            AssertInvalidEvents(eventAfterCompletion);
            AssertInvalidEvents(duplicateCompletion);
        });
    }

    [Test]
    public void Create_RejectsInvalidEventOptionalFields()
    {
        Assert.Multiple(() =>
        {
            AssertInvalidEvents(Event(0, RenderPipelineDiagnosticEventKind.BoundaryPlanned));
            AssertInvalidEvents(Event(0, RenderPipelineDiagnosticEventKind.OutcomeAssigned));
            AssertInvalidEvents(Event(0, RenderPipelineDiagnosticEventKind.Failure));
            AssertInvalidEvents(Event(0, RenderPipelineDiagnosticEventKind.CleanupFailure));
            AssertInvalidEvents(Event(0, RenderPipelineDiagnosticEventKind.NestedRequest));
            AssertInvalidEvents(Event(
                0,
                RenderPipelineDiagnosticEventKind.RequestStarted,
                boundaryReason: RenderPipelineBoundaryReason.Opaque));
            AssertInvalidEvents(Event(
                0,
                RenderPipelineDiagnosticEventKind.FragmentRecorded,
                outcome: RenderPipelineOutcome.Executed));
            AssertInvalidEvents(Event(
                0,
                RenderPipelineDiagnosticEventKind.PassExecuted,
                failurePhase: RenderPipelineFailurePhase.Execution));
            AssertInvalidEvents(Event(
                0,
                RenderPipelineDiagnosticEventKind.CacheDecision,
                relatedRequestId: 2));
        });
    }

    [Test]
    public void Create_AcceptsEachEventSpecificOptionalField()
    {
        List<RenderPipelineDiagnosticEvent> events =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.BoundaryPlanned, 1,
                boundaryReason: RenderPipelineBoundaryReason.Geometry),
            Event(3, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(4, RenderPipelineDiagnosticEventKind.NestedRequest, 1, relatedRequestId: 2),
            Event(5, RenderPipelineDiagnosticEventKind.Failure, 0,
                failurePhase: RenderPipelineFailurePhase.Execution),
            Event(6, RenderPipelineDiagnosticEventKind.CleanupFailure, 0,
                failurePhase: RenderPipelineFailurePhase.Cleanup),
            Event(7, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];

        RenderPipelineDiagnosticSnapshot snapshot = CreateSnapshot(
            succeeded: false,
            failurePhase: RenderPipelineFailurePhase.Execution,
            counters: CreateCounters(failures: 1, cleanupFailures: 1, opaqueBoundaries: 1),
            events: events);

        Assert.That(snapshot.Events, Is.EqualTo(events));
    }

    [TestCase((int)RenderPipelineDiagnosticEventKind.PassExecuted)]
    [TestCase((int)RenderPipelineDiagnosticEventKind.SynchronizationExecuted)]
    [TestCase((int)RenderPipelineDiagnosticEventKind.BackendTransitionExecuted)]
    public void Create_RejectsExecutionBeforeCorrespondingPlan(int executedKindValue)
    {
        var executedKind = (RenderPipelineDiagnosticEventKind)executedKindValue;

        Assert.That(
            () => CreateSnapshot(events: [Event(0, executedKind, 1)]),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsOutcomeReconciliationMismatch()
    {
        var counters = CreateCounters(recordedFragments: 2, executedOutcomes: 1);

        Assert.That(
            () => CreateSnapshot(counters: counters),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsFragmentOutcomeEvidenceMismatch()
    {
        RenderPipelineDiagnosticEvent[] duplicateOutcome =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(3, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(4, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];
        RenderPipelineDiagnosticEvent[] missingOutcome =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.FragmentRecorded, 2),
            Event(3, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(4, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];
        RenderPipelineDiagnosticEvent[] wrongCounter =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(3, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(
                () => CreateSnapshot(events: duplicateOutcome),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    counters: CreateCounters(recordedFragments: 2, executedOutcomes: 2),
                    events: missingOutcome),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    counters: CreateCounters(executedOutcomes: 0, cachedOutcomes: 1),
                    events: wrongCounter),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    purpose: RenderRequestPurpose.Bounds,
                    counters: CreateCounters(),
                    events: wrongCounter),
                Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void Create_RejectsIntermediateOwnershipReconciliationMismatch()
    {
        var counters = CreateCounters(intermediateAcquires: 2, intermediateDischarges: 1);

        Assert.That(
            () => CreateSnapshot(counters: counters),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsInconsistentSuccessFailureState()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => CreateSnapshot(
                    succeeded: true,
                    failurePhase: RenderPipelineFailurePhase.Execution,
                    counters: CreateCounters(failures: 1)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: null,
                    counters: CreateCounters(failures: 1)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: RenderPipelineFailurePhase.Execution,
                    counters: CreateCounters(failures: 0)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: RenderPipelineFailurePhase.Execution,
                    counters: CreateCounters(failures: 2)),
                Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void Create_RejectsInconsistentFailureEvents()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => CreateSnapshot(events: CreateEventStream(
                    primaryFailurePhase: RenderPipelineFailurePhase.Execution)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: RenderPipelineFailurePhase.Execution,
                    counters: CreateCounters(failures: 1),
                    events: CreateEventStream(
                        primaryFailurePhase: RenderPipelineFailurePhase.Allocation)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: RenderPipelineFailurePhase.Execution,
                    counters: CreateCounters(failures: 1, cleanupFailures: 1),
                    events: CreateEventStream(
                        primaryFailurePhase: RenderPipelineFailurePhase.Execution)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: RenderPipelineFailurePhase.Cleanup,
                    counters: CreateCounters(failures: 1, cleanupFailures: 1),
                    events: CreateEventStream(
                        primaryFailurePhase: RenderPipelineFailurePhase.Cleanup,
                        cleanupFailurePhases: [RenderPipelineFailurePhase.Execution])),
                Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void Create_ValidatesCleanupFailureAndCachePublicationState()
    {
        RenderPipelineDiagnosticSnapshot cleanupOnlyFailure = CreateSnapshot(
            succeeded: false,
            failurePhase: RenderPipelineFailurePhase.Cleanup,
            counters: CreateCounters(failures: 1, cleanupFailures: 1));
        RenderPipelineDiagnosticSnapshot secondaryCleanupFailure = CreateSnapshot(
            succeeded: false,
            failurePhase: RenderPipelineFailurePhase.Execution,
            counters: CreateCounters(failures: 1, cleanupFailures: 2));
        RenderPipelineDiagnosticEvent[] acceptedCleanupEvents =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(3, RenderPipelineDiagnosticEventKind.Failure,
                failurePhase: RenderPipelineFailurePhase.Cleanup),
            Event(4, RenderPipelineDiagnosticEventKind.CleanupFailure,
                failurePhase: RenderPipelineFailurePhase.Cleanup),
            Event(5, RenderPipelineDiagnosticEventKind.CacheCapturePublished, 1),
            Event(6, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];
        RenderPipelineDiagnosticSnapshot acceptedPostCommitCleanup = CreateSnapshot(
            succeeded: false,
            failurePhase: RenderPipelineFailurePhase.Cleanup,
            counters: CreateCounters(failures: 1, cleanupFailures: 1, renderCacheCaptures: 1),
            events: acceptedCleanupEvents);

        Assert.Multiple(() =>
        {
            Assert.That(cleanupOnlyFailure.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Cleanup));
            Assert.That(secondaryCleanupFailure.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Execution));
            Assert.That(acceptedPostCommitCleanup[RenderPipelineCounter.RenderCacheCaptures], Is.EqualTo(1));
            Assert.That(
                () => CreateSnapshot(
                    succeeded: true,
                    counters: CreateCounters(cleanupFailures: 1)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: RenderPipelineFailurePhase.Cleanup,
                    counters: CreateCounters(failures: 1)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    succeeded: false,
                    failurePhase: RenderPipelineFailurePhase.Execution,
                    counters: CreateCounters(failures: 1, cleanupFailures: 1, renderCacheCaptures: 1),
                    events: acceptedCleanupEvents),
                Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void Create_ReconcilesSuccessfulCachePublicationEvidence()
    {
        RenderPipelineDiagnosticEvent[] publishedEvents =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
            Event(2, RenderPipelineDiagnosticEventKind.OutcomeAssigned, 1,
                outcome: RenderPipelineOutcome.Executed),
            Event(3, RenderPipelineDiagnosticEventKind.CacheCapturePublished, 1),
            Event(4, RenderPipelineDiagnosticEventKind.RequestCompleted),
        ];
        RenderPipelineDiagnosticSnapshot snapshot = CreateSnapshot(
            counters: CreateCounters(renderCacheCaptures: 1),
            events: publishedEvents);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot[RenderPipelineCounter.RenderCacheCaptures], Is.EqualTo(1));
            Assert.That(
                () => CreateSnapshot(counters: CreateCounters(renderCacheCaptures: 1)),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(events: publishedEvents),
                Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void Create_ValidatesOpaqueExternalExecutionState()
    {
        List<RenderPipelineDiagnosticEvent> opaqueBoundary = CreateEventStream(
            boundaryReason: RenderPipelineBoundaryReason.LegacyRawCanvas);
        RenderPipelineDiagnosticSnapshot skippedOpaqueWork = CreateSnapshot(
            hasOpaqueExternalWork: true,
            counters: CreateCounters(opaqueBoundaries: 1),
            events: opaqueBoundary);
        RenderPipelineDiagnosticSnapshot executedOpaqueWork = CreateSnapshot(
            hasOpaqueExternalWork: true,
            counters: CreateCounters(opaqueExternalExecutions: 1, opaqueBoundaries: 1),
            events: opaqueBoundary);

        Assert.Multiple(() =>
        {
            Assert.That(skippedOpaqueWork.HasOpaqueExternalWork, Is.True);
            Assert.That(skippedOpaqueWork[RenderPipelineCounter.OpaqueExternalExecutions], Is.Zero);
            Assert.That(executedOpaqueWork[RenderPipelineCounter.OpaqueExternalExecutions], Is.EqualTo(1));
            Assert.That(
                () => CreateSnapshot(hasOpaqueExternalWork: true),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    hasOpaqueExternalWork: false,
                    counters: CreateCounters(opaqueExternalExecutions: 1, opaqueBoundaries: 1),
                    events: opaqueBoundary),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    hasOpaqueExternalWork: true,
                    counters: CreateCounters(),
                    events: opaqueBoundary),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(
                () => CreateSnapshot(
                    hasOpaqueExternalWork: true,
                    counters: CreateCounters(opaqueExternalExecutions: 1)),
                Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void State_TracksLatestAndLatestFrameIndependently()
    {
        var state = new RenderPipelineDiagnosticsState();
        RenderPipelineDiagnosticSnapshot firstFrame = CreateSnapshot(requestId: 1);
        RenderPipelineDiagnosticSnapshot bounds = CreateSnapshot(
            requestId: 2,
            purpose: RenderRequestPurpose.Bounds);
        RenderPipelineDiagnosticSnapshot secondFrame = CreateSnapshot(requestId: 3);

        Assert.Multiple(() =>
        {
            Assert.That(state.Latest, Is.SameAs(RenderPipelineDiagnosticSnapshot.Empty));
            Assert.That(state.LatestFrame, Is.SameAs(RenderPipelineDiagnosticSnapshot.Empty));
        });

        state.Complete(firstFrame);
        state.Complete(bounds);

        Assert.Multiple(() =>
        {
            Assert.That(state.Latest, Is.SameAs(bounds));
            Assert.That(state.LatestFrame, Is.SameAs(firstFrame));
        });

        state.Complete(secondFrame);

        Assert.Multiple(() =>
        {
            Assert.That(state.Latest, Is.SameAs(secondFrame));
            Assert.That(state.LatestFrame, Is.SameAs(secondFrame));
        });
    }

    [Test]
    public void Reset_ClearsBothSnapshotsButKeepsSubscribers()
    {
        var state = new RenderPipelineDiagnosticsState();
        var observed = new List<long>();
        state.RequestCompleted += snapshot => observed.Add(snapshot.RequestId);
        state.Complete(CreateSnapshot(requestId: 1));

        state.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(state.Latest, Is.SameAs(RenderPipelineDiagnosticSnapshot.Empty));
            Assert.That(state.LatestFrame, Is.SameAs(RenderPipelineDiagnosticSnapshot.Empty));
        });

        RenderPipelineDiagnosticSnapshot bounds = CreateSnapshot(
            requestId: 2,
            purpose: RenderRequestPurpose.Bounds);
        state.Complete(bounds);

        Assert.Multiple(() =>
        {
            Assert.That(state.Latest, Is.SameAs(bounds));
            Assert.That(state.LatestFrame, Is.SameAs(RenderPipelineDiagnosticSnapshot.Empty));
            Assert.That(observed, Is.EqualTo(new long[] { 1, 2 }));
        });
    }

    [Test]
    public void Complete_IsolatesObserverFailuresAndStillPublishesState()
    {
        var state = new RenderPipelineDiagnosticsState();
        RenderPipelineDiagnosticSnapshot snapshot = CreateSnapshot(requestId: 9);
        var observed = new List<RenderPipelineDiagnosticSnapshot>();
        state.RequestCompleted += _ => throw new InvalidOperationException("observer failure");
        state.RequestCompleted += observed.Add;

        Assert.That(() => state.Complete(snapshot), Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(state.Latest, Is.SameAs(snapshot));
            Assert.That(state.LatestFrame, Is.SameAs(snapshot));
            Assert.That(observed, Is.EqualTo(new[] { snapshot }));
        });
    }

    [Test]
    public void Recorder_CleanupOnlyFailureBecomesPrimaryAndReconciles()
    {
        var state = new RenderPipelineDiagnosticsState();
        RenderPipelineDiagnosticRecorder recorder = RenderPipelineDiagnosticRecorder.Start(
            state,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            "Root")!;
        long subjectId = recorder.RecordFragments(1, RenderPipelineOutcome.Executed).Single();

        recorder.RecordCleanupFailure(subjectId);
        recorder.Complete();

        RenderPipelineDiagnosticSnapshot snapshot = state.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Succeeded, Is.False);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Cleanup));
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.FailedOutcomes], Is.EqualTo(1));
            Assert.That(
                snapshot.Events.Count(item => item.Kind == RenderPipelineDiagnosticEventKind.Failure),
                Is.EqualTo(1));
            Assert.That(
                snapshot.Events.Count(item => item.Kind == RenderPipelineDiagnosticEventKind.CleanupFailure),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void Recorder_InvalidSnapshotCannotEscapeIntoRendering()
    {
        var state = new RenderPipelineDiagnosticsState();
        RenderPipelineDiagnosticRecorder recorder = RenderPipelineDiagnosticRecorder.Start(
            state,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            rootTargetClass: " ")!;
        long subjectId = recorder.RecordFragments(1, RenderPipelineOutcome.Executed).Single();
        recorder.RecordOutcome(subjectId, RenderPipelineOutcome.Executed);

        Assert.That(recorder.Complete, Throws.Nothing);
        Assert.That(state.Latest, Is.SameAs(RenderPipelineDiagnosticSnapshot.Empty));
    }

    [Test]
    public void State_AllowsConcurrentCompletionAndReset()
    {
        var state = new RenderPipelineDiagnosticsState();
        int observed = 0;
        state.RequestCompleted += _ => Interlocked.Increment(ref observed);
        RenderPipelineDiagnosticSnapshot[] snapshots = Enumerable.Range(1, 64)
            .Select(index => CreateSnapshot(
                requestId: index,
                purpose: index % 3 == 0 ? RenderRequestPurpose.Bounds : RenderRequestPurpose.Frame))
            .ToArray();

        Parallel.ForEach(snapshots, snapshot =>
        {
            state.Complete(snapshot);
            if (snapshot.RequestId % 8 == 0)
            {
                state.Reset();
            }

            _ = state.Latest;
            _ = state.LatestFrame;
        });

        state.Complete(CreateSnapshot(requestId: 65));

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.EqualTo(65));
            Assert.That(state.Latest.RequestId, Is.EqualTo(65));
            Assert.That(state.LatestFrame.RequestId, Is.EqualTo(65));
        });
    }

    private static RenderPipelineDiagnosticSnapshot CreateSnapshot(
        long requestId = 1,
        long? parentRequestId = null,
        RenderIntent intent = RenderIntent.Preview,
        RenderRequestPurpose purpose = RenderRequestPurpose.Frame,
        bool succeeded = true,
        bool hasOpaqueExternalWork = false,
        string rootTargetClass = "Root",
        RenderPipelineFailurePhase? failurePhase = null,
        IReadOnlyDictionary<RenderPipelineCounter, long>? counters = null,
        IEnumerable<RenderPipelineDiagnosticEvent>? events = null)
    {
        IReadOnlyDictionary<RenderPipelineCounter, long> effectiveCounters = counters
            ?? (purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest
                ? CreateCounters(executedOutcomes: 0, metadataOutcomes: 1)
                : CreateCounters());
        return RenderPipelineDiagnosticSnapshot.Create(
            requestId,
            parentRequestId,
            intent,
            purpose,
            succeeded,
            hasOpaqueExternalWork,
            rootTargetClass,
            failurePhase,
            effectiveCounters,
            events ?? CreateDefaultEvents(succeeded, failurePhase, purpose, effectiveCounters));
    }

    private static Dictionary<RenderPipelineCounter, long> CreateCounters(
        long recordedFragments = 1,
        long executedOutcomes = 1,
        long cachedOutcomes = 0,
        long metadataOutcomes = 0,
        long skippedOutcomes = 0,
        long failedOutcomes = 0,
        long intermediateAcquires = 1,
        long intermediateDischarges = 1,
        long failures = 0,
        long cleanupFailures = 0,
        long opaqueExternalExecutions = 0,
        long opaqueBoundaries = 0,
        long opaque3DBoundaries = 0,
        long renderCacheCaptures = 0)
    {
        return new Dictionary<RenderPipelineCounter, long>
        {
            [RenderPipelineCounter.RecordedFragments] = recordedFragments,
            [RenderPipelineCounter.ExecutedOutcomes] = executedOutcomes,
            [RenderPipelineCounter.CachedOutcomes] = cachedOutcomes,
            [RenderPipelineCounter.MetadataOutcomes] = metadataOutcomes,
            [RenderPipelineCounter.SkippedOutcomes] = skippedOutcomes,
            [RenderPipelineCounter.FailedOutcomes] = failedOutcomes,
            [RenderPipelineCounter.IntermediateAcquires] = intermediateAcquires,
            [RenderPipelineCounter.IntermediateDischarges] = intermediateDischarges,
            [RenderPipelineCounter.Failures] = failures,
            [RenderPipelineCounter.CleanupFailures] = cleanupFailures,
            [RenderPipelineCounter.OpaqueExternalExecutions] = opaqueExternalExecutions,
            [RenderPipelineCounter.OpaqueBoundaries] = opaqueBoundaries,
            [RenderPipelineCounter.Opaque3DBoundaries] = opaque3DBoundaries,
            [RenderPipelineCounter.RenderCacheCaptures] = renderCacheCaptures,
        };
    }

    private static List<RenderPipelineDiagnosticEvent> CreateDefaultEvents(
        bool succeeded,
        RenderPipelineFailurePhase? failurePhase,
        RenderRequestPurpose purpose,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters)
    {
        counters.TryGetValue(RenderPipelineCounter.CleanupFailures, out long cleanupFailures);
        var cleanupFailurePhases = new List<RenderPipelineFailurePhase>();
        for (long i = 0; i < cleanupFailures; i++)
        {
            cleanupFailurePhases.Add(RenderPipelineFailurePhase.Cleanup);
        }

        return CreateEventStream(
            outcome: purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest
                ? RenderPipelineOutcome.Metadata
                : RenderPipelineOutcome.Executed,
            primaryFailurePhase: succeeded ? null : failurePhase,
            cleanupFailurePhases: cleanupFailurePhases);
    }

    private static List<RenderPipelineDiagnosticEvent> CreateEventStream(
        RenderPipelineOutcome outcome = RenderPipelineOutcome.Executed,
        RenderPipelineFailurePhase? primaryFailurePhase = null,
        IReadOnlyList<RenderPipelineFailurePhase>? cleanupFailurePhases = null,
        RenderPipelineBoundaryReason? boundaryReason = null)
    {
        List<RenderPipelineDiagnosticEvent> result =
        [
            Event(0, RenderPipelineDiagnosticEventKind.RequestStarted),
            Event(1, RenderPipelineDiagnosticEventKind.FragmentRecorded, 1),
        ];

        if (boundaryReason is { } reason)
        {
            result.Add(Event(
                result.Count,
                RenderPipelineDiagnosticEventKind.BoundaryPlanned,
                1,
                boundaryReason: reason));
        }

        result.Add(Event(
            result.Count,
            RenderPipelineDiagnosticEventKind.OutcomeAssigned,
            1,
            outcome: outcome));

        if (primaryFailurePhase is { } primaryPhase)
        {
            result.Add(Event(
                result.Count,
                RenderPipelineDiagnosticEventKind.Failure,
                failurePhase: primaryPhase));
        }

        if (cleanupFailurePhases is not null)
        {
            foreach (RenderPipelineFailurePhase cleanupPhase in cleanupFailurePhases)
            {
                result.Add(Event(
                    result.Count,
                    RenderPipelineDiagnosticEventKind.CleanupFailure,
                    failurePhase: cleanupPhase));
            }
        }

        result.Add(Event(result.Count, RenderPipelineDiagnosticEventKind.RequestCompleted));
        return result;
    }

    private static RenderPipelineDiagnosticEvent Event(
        long sequence,
        RenderPipelineDiagnosticEventKind kind,
        long subjectId = 0,
        long? relatedRequestId = null,
        RenderPipelineBoundaryReason? boundaryReason = null,
        RenderPipelineOutcome? outcome = null,
        RenderPipelineFailurePhase? failurePhase = null)
    {
        return new RenderPipelineDiagnosticEvent(
            sequence,
            kind,
            subjectId,
            relatedRequestId,
            boundaryReason,
            outcome,
            failurePhase);
    }

    private static void AssertInvalidEvents(params RenderPipelineDiagnosticEvent[] events)
    {
        Assert.That(
            () => CreateSnapshot(events: events),
            Throws.InstanceOf<ArgumentException>());
    }
}
