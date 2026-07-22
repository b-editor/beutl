using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

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
        var diagnostics = new RenderPipelineDiagnosticsState();
        var parent = new RenderRequestOptions(
            RenderIntent.Delivery,
            RenderRequestPurpose.Frame,
            outputScale: 2,
            maxWorkingScale: 3,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Disabled,
            owner: owner,
            diagnostics: diagnostics);

        using var binding = new NestedRenderTargetBinding();
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
            Assert.That(nested.Diagnostics, Is.SameAs(diagnostics));
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
    public void Request_RejectsNestedPolicyDriftOutsideTheNestedFactory()
    {
        using var owner = new RenderRequestOwner();
        var diagnostics = new RenderPipelineDiagnosticsState();
        var parentOptions = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            outputScale: 2,
            maxWorkingScale: 3,
            fusionMode: FusionMode.Disabled,
            owner: owner,
            diagnostics: diagnostics);
        using var parent = new RenderRequest(parentOptions);
        using var binding = new NestedRenderTargetBinding();
        var drifted = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            outputScale: 2,
            maxWorkingScale: 3,
            fusionMode: FusionMode.Enabled,
            owner: owner,
            diagnostics: diagnostics,
            targetBinding: binding);

        Assert.That(
            () => new RenderRequest(drifted, parent),
            Throws.TypeOf<ArgumentException>());
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
    public void GraphBuilder_IssuesUniqueIdsAndPreservesAuthoredOrder()
    {
        var requestId = new RenderRequestId(42);
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderProvenanceId provenance = builder.AddProvenance("root", "renderer-entry");
        RenderValueId source = builder.AddValue([], provenance, payload: "source");
        RenderValueId mapped = builder.AddValue([source], provenance, payload: "map");
        RenderFragmentId first = builder.AddFragment([source], provenance, payload: "first");
        RenderFragmentId second = builder.AddFragment([mapped], provenance, payload: "second");
        builder.PublishRoot(second);
        RenderCacheCandidateId candidate = builder.AddCacheCandidate(first, "candidate-key");

        RecordedRenderGraph graph = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(graph.RequestId, Is.EqualTo(requestId));
            Assert.That(source, Is.Not.EqualTo(mapped));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(graph.Fragments.Select(static item => item.AuthoredOrder), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(graph.Fragments.Select(static item => item.Id), Is.EqualTo(new[] { first, second }));
            Assert.That(graph.Values[1].Inputs, Is.EqualTo(new[] { source }));
            Assert.That(graph.PublicationRoots, Is.EqualTo(new[] { second }));
            Assert.That(graph.Provenance.Single().Origin, Is.EqualTo("root"));
            Assert.That(graph.CacheCandidates.Single().Id, Is.EqualTo(candidate));
            Assert.That(graph.CacheCandidates.Single().FragmentId, Is.EqualTo(first));
            Assert.That(() => builder.AddFragment([], provenance), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void GraphBuilder_RejectsIdsFromAnotherRequest()
    {
        var first = new RecordedRenderGraphBuilder(new RenderRequestId(1));
        var second = new RecordedRenderGraphBuilder(new RenderRequestId(2));
        RenderProvenanceId firstProvenance = first.AddProvenance("one", "root");
        RenderProvenanceId secondProvenance = second.AddProvenance("two", "root");
        RenderValueId foreign = first.AddValue([], firstProvenance);

        Assert.That(
            () => second.AddValue([foreign], secondProvenance),
            Throws.TypeOf<InvalidOperationException>());
    }
}
