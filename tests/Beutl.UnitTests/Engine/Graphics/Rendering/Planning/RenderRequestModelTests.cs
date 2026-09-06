using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RenderRequestModelTests
{
    [Test]
    public void Options_SanitizeScalesSnapshotCacheAndValidateRegions()
    {
        var cache = new RenderCacheOptions(true, new RenderCacheRules(400, 4));
        var requestedRegion = new Rect(17, 19, 0, 23);
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: new Rect(0, 0, 100, 80),
            requestedRegion: requestedRegion,
            outputScale: float.NaN,
            maxWorkingScale: 0,
            cachePolicy: cache);

        Assert.Multiple(() =>
        {
            Assert.That(options.OutputScale, Is.EqualTo(1));
            Assert.That(options.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
            Assert.That(options.RequestedRegion, Is.EqualTo(requestedRegion));
            Assert.That(options.CachePolicy, Is.Not.SameAs(cache));
            Assert.That(options.CachePolicy, Is.EqualTo(cache));
            Assert.That(
                () => new RenderRequestOptions(
                    RenderIntent.Preview,
                    RenderRequestPurpose.Auxiliary,
                    targetDomain: Rect.Empty),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new RenderRequestOptions(
                    RenderIntent.Preview,
                    RenderRequestPurpose.Auxiliary,
                    requestedRegion: new Rect(0, 0, -1, 2)),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void NestedOptions_InheritSharedPolicyOwnerDiagnosticsAndFusionMode()
    {
        using var owner = new RenderRequestOwner();

        using var binding = new NestedRenderTargetBinding();
        var parent = new RenderRequestOptions(
            RenderIntent.Delivery,
            RenderRequestPurpose.Frame,
            outputScale: 2,
            maxWorkingScale: 3,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Disabled,
            owner: owner);
        RenderRequestOptions nested = parent.CreateNested(binding);

        Assert.Multiple(() =>
        {
            Assert.That(nested.Intent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(nested.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
            Assert.That(nested.OutputScale, Is.EqualTo(2));
            Assert.That(nested.MaxWorkingScale, Is.EqualTo(3));
            Assert.That(nested.CachePolicy, Is.EqualTo(RenderCacheOptions.Disabled));
            Assert.That(nested.FusionMode, Is.EqualTo(FusionMode.Disabled));
            Assert.That(nested.Owner, Is.SameAs(owner));
            Assert.That(nested.TargetBinding, Is.SameAs(binding));
            Assert.That(nested.PlanIdentity, Is.EqualTo(parent.PlanIdentity));
        });
    }

    [Test]
    public void NestedOptions_AllowAnExplicitConcreteTargetScaleWithoutPolicyDrift()
    {
        using var owner = new RenderRequestOwner();
        var parentOptions = new RenderRequestOptions(
            RenderIntent.Delivery,
            RenderRequestPurpose.Frame,
            outputScale: 1.75f,
            maxWorkingScale: 0.75f,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Disabled,
            owner: owner);
        using var parent = new RenderRequest(parentOptions);
        using var binding = new NestedRenderTargetBinding();
        RenderRequestOptions nestedOptions = parentOptions.CreateNestedAtScale(binding, 0.5f);
        using var nested = new RenderRequest(nestedOptions, parent);

        Assert.Multiple(() =>
        {
            Assert.That(nestedOptions.OutputScale, Is.EqualTo(0.5f));
            Assert.That(nestedOptions.MaxWorkingScale, Is.EqualTo(0.5f));
            Assert.That(nestedOptions.Owner, Is.SameAs(owner));
            Assert.That(
                () => parentOptions.CreateNestedAtScale(binding, float.PositiveInfinity),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void NestedOptions_DoNotInheritParentRequestedRegionWithoutAnExplicitMapping()
    {
        var parentRegion = new Rect(10, 20, 30, 40);
        var mappedChildRegion = new Rect(1, 2, 3, 4);
        var parent = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            requestedRegion: parentRegion);
        using var binding = new NestedRenderTargetBinding();

        RenderRequestOptions implicitScale = parent.CreateNested(binding);
        RenderRequestOptions explicitScale = parent.CreateNestedAtScale(binding, 0.5f);
        RenderRequestOptions mapped = parent.CreateNested(binding, requestedRegion: mappedChildRegion);

        Assert.Multiple(() =>
        {
            Assert.That(implicitScale.RequestedRegion, Is.Null);
            Assert.That(explicitScale.RequestedRegion, Is.Null);
            Assert.That(mapped.RequestedRegion, Is.EqualTo(mappedChildRegion));
        });
    }


    [Test]
    public void FusionMode_ParticipatesInPlanIdentityWithoutBecomingPublicRendererPolicy()
    {
        var enabled = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            fusionMode: FusionMode.Enabled);
        var disabled = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            fusionMode: FusionMode.Disabled);

        Assert.That(enabled.PlanIdentity, Is.Not.EqualTo(disabled.PlanIdentity));
        enabled.Owner.Dispose();
        disabled.Owner.Dispose();
    }

    [Test]
    public void Request_EnforcesLifecycleAndMetadataOnlyShortcut()
    {
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            owner: owner);
        using var request = new RenderRequest(options);

        request.TransitionTo(RenderRequestState.Recording);
        request.TransitionTo(RenderRequestState.Recorded);
        request.TransitionTo(RenderRequestState.TargetDependenciesLowered);
        request.TransitionTo(RenderRequestState.MetadataResolved);
        request.TransitionTo(RenderRequestState.RegionsResolved);
        request.TransitionTo(RenderRequestState.CachesResolved);
        request.TransitionTo(RenderRequestState.Planned);
        request.TransitionTo(RenderRequestState.Executing);
        request.TransitionTo(RenderRequestState.Completed);

        Assert.Multiple(() =>
        {
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Completed));
            Assert.That(request.Id.Value, Is.GreaterThan(0));
            Assert.That(
                () => request.TransitionTo(RenderRequestState.Executing),
                Throws.TypeOf<InvalidOperationException>());
        });

        var queryOptions = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Bounds,
            owner: owner);
        using var query = new RenderRequest(queryOptions);
        query.TransitionTo(RenderRequestState.Recording);
        query.TransitionTo(RenderRequestState.Recorded);
        query.TransitionTo(RenderRequestState.TargetDependenciesLowered);
        query.TransitionTo(RenderRequestState.MetadataResolved);
        query.CompleteMetadataOnly();

        Assert.That(query.State, Is.EqualTo(RenderRequestState.Completed));
    }

    [Test]
    public void Failure_PreservesTheFirstFailureAndAllowsOnlyDisposalAfterward()
    {
        var primary = new InvalidOperationException("primary");
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            owner: owner);
        using var request = new RenderRequest(options);
        request.TransitionTo(RenderRequestState.Recording);

        request.Fail(primary);

        Assert.Multiple(() =>
        {
            Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(primary));
            Assert.That(
                () => request.TransitionTo(RenderRequestState.Recorded),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void GraphBuilder_PreservesCanonicalSemanticDagAndAuthoredMetadataOrder()
    {
        var requestId = new RenderRequestId(42);
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderFragmentReference source = Fragment();
        RenderFragmentReference mapped = Fragment(source);
        builder.AddFragment(source);
        builder.AddFragment(mapped);
        builder.PublishRoot(mapped.Id!.Value);
        RenderCacheCandidateId candidate = builder.AddCacheCandidate(source.Id!.Value, "candidate-key");

        RecordedRenderGraph graph = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(graph.RequestId, Is.EqualTo(requestId));
            Assert.That(source.Id, Is.Not.Null);
            Assert.That(mapped.Id, Is.Not.Null.And.Not.EqualTo(source.Id));
            Assert.That(graph.Fragments, Is.EqualTo(new[] { source, mapped }));
            Assert.That(graph.GetFragment(source.Id!.Value), Is.SameAs(source));
            Assert.That(mapped.Inputs, Is.EqualTo(new[] { source }));
            Assert.That(graph.PublicationRoots, Is.EqualTo(new[] { mapped.Id!.Value }));
            Assert.That(graph.CacheCandidates.Single().Id, Is.EqualTo(candidate));
            Assert.That(graph.CacheCandidates.Single().FragmentId, Is.EqualTo(source.Id!.Value));
            Assert.That(graph.CacheCandidates.Single().AuthoredOrder, Is.Zero);
            Assert.That(() => builder.AddFragment(Fragment()), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void GraphBuilder_RejectsAnInputCommittedToAnotherRequest()
    {
        var first = new RecordedRenderGraphBuilder(new RenderRequestId(1));
        var second = new RecordedRenderGraphBuilder(new RenderRequestId(2));
        RenderFragmentReference foreign = Fragment();
        RenderFragmentReference consumer = Fragment(foreign);
        first.AddFragment(foreign);

        Assert.That(
            () => second.AddFragment(consumer),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(consumer.Id, Is.Null);
    }

    [Test]
    public void GraphBuilder_RejectsSameRequestIdImpostorInput()
    {
        var requestId = new RenderRequestId(1);
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderFragmentReference canonical = Fragment();
        builder.AddFragment(canonical);
        RenderFragmentReference impostor = Fragment();
        impostor.AssignId(canonical.Id!.Value);
        RenderFragmentReference consumer = Fragment(impostor);

        Assert.That(
            () => builder.AddFragment(consumer),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(consumer.Id, Is.Null);
    }

    [Test]
    public void StructuralIdentity_RejectsSameIdImpostorInputInAConstructedGraph()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference canonical = Fragment();
        var canonicalId = new RenderFragmentId(requestId, 1);
        canonical.AssignId(canonicalId);
        RenderFragmentReference impostor = Fragment();
        impostor.AssignId(canonicalId);
        RenderFragmentReference consumer = Fragment(impostor);
        consumer.AssignId(new RenderFragmentId(requestId, 2));
        var graph = new RecordedRenderGraph(requestId, [canonical, consumer], [], [], []);

        Assert.That(
            () => StructuralFragmentIdentity.Create(graph, 1),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contain("not part"));
    }

    [Test]
    public void StructuralIdentity_RejectsAForwardInputInAConstructedGraph()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference forward = Fragment();
        RenderFragmentReference consumer = Fragment(forward);
        consumer.AssignId(new RenderFragmentId(requestId, 1));
        forward.AssignId(new RenderFragmentId(requestId, 2));
        var graph = new RecordedRenderGraph(requestId, [consumer, forward], [], [], []);

        Assert.That(
            () => StructuralFragmentIdentity.Create(graph, 0),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contain("earlier"));
    }

    [Test]
    public void GraphBuilder_AppendFailureDoesNotAssignAnyFragmentId()
    {
        var requestId = new RenderRequestId(1);
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderFragmentReference first = Fragment();
        RenderFragmentReference foreign = Fragment();
        foreign.AssignId(new RenderFragmentId(new RenderRequestId(2), 1));
        RenderFragmentReference invalid = Fragment(foreign);
        var commit = new NodeRecordingCommit(
            [
                new RecordedRenderFragmentEntry(first, "first", "test"),
                new RecordedRenderFragmentEntry(invalid, "invalid", "test"),
            ],
            [],
            [],
            [],
            [],
            []);

        Assert.That(
            () => builder.Append(commit),
            Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(first.Id, Is.Null);
            Assert.That(invalid.Id, Is.Null);
            Assert.That(builder.Build().Fragments, Is.Empty);
        });
    }

    private static RenderFragmentReference Fragment(params RenderFragmentReference[] inputs)
        => new(
            RenderFragmentKind.ContributeValues,
            new Rect(0, 0, 32, 18),
            EffectiveScale.At(1),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [.. inputs],
            payload: null,
            RenderFragmentHitTest.Bounds);
}
