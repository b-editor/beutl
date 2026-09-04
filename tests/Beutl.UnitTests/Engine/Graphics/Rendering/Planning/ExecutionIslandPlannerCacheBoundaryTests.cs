using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class ExecutionIslandPlannerCacheBoundaryTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 24);

    [TestCase((int)RenderCacheBypassReason.CacheDisabled)]
    [TestCase((int)RenderCacheBypassReason.CapturePublicationDisabled)]
    [TestCase((int)RenderCacheBypassReason.OutsideCacheRules)]
    [TestCase((int)RenderCacheBypassReason.UnstableBoundaryPlan)]
    public void BypassedCandidate_DoesNotSplitMaximalCompatibleRun(
        int bypassReasonValue)
    {
        var bypassReason = (RenderCacheBypassReason)bypassReasonValue;
        GraphFixture fixture = CreateShaderGraph(includeGeometryPrefix: false);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.Bypass,
            bypassReason);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Enabled);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ShaderRuns, Has.Exactly(1).Items);
            Assert.That(ResolveFragments(fixture.Graph, plan.ShaderRuns.Single().StageFragmentIndices)
                    .Select(static fragment => fragment.Id),
                Is.EqualTo(new[] { fixture.CachedProducer.Id, fixture.Tail.Id }));
            Assert.That(plan.Boundaries, Has.None.Matches<ExecutionIslandBoundary>(static boundary =>
                boundary.Reason is ExecutionIslandBoundaryReason.CacheInput
                    or ExecutionIslandBoundaryReason.CacheCapture));
        });
    }

    [Test]
    public void BypassedCandidate_WithFusionDisabledUsesOnlyFusionDisabledSplit()
    {
        GraphFixture fixture = CreateShaderGraph(includeGeometryPrefix: false);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.Bypass,
            RenderCacheBypassReason.CacheDisabled);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ShaderRuns.Select(static run => run.StageFragmentIndices.Length),
                Is.EqualTo(new[] { 1, 1 }));
            Assert.That(plan.Boundaries.Count(static boundary =>
                boundary.Reason == ExecutionIslandBoundaryReason.FusionDisabled), Is.EqualTo(1));
            Assert.That(plan.Boundaries, Has.None.Matches<ExecutionIslandBoundary>(static boundary =>
                boundary.Reason is ExecutionIslandBoundaryReason.CacheInput
                    or ExecutionIslandBoundaryReason.CacheCapture));
        });
    }

    [Test]
    public void SelectedMissCapture_SplitsAfterProducerWithExactCacheCaptureReason()
    {
        GraphFixture fixture = CreateShaderGraph(includeGeometryPrefix: false);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.MissCapture);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Enabled);
        ExecutionIslandBoundary[] cacheBoundaries = plan.Boundaries
            .Where(static boundary => boundary.Reason is ExecutionIslandBoundaryReason.CacheInput
                or ExecutionIslandBoundaryReason.CacheCapture)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(plan.ShaderRuns.Select(static run => run.StageFragmentIndices.Length),
                Is.EqualTo(new[] { 1, 1 }));
            Assert.That(cacheBoundaries, Has.Exactly(1).Items);
            Assert.That(
                cacheBoundaries[0].BeforeFragmentIndex,
                Is.EqualTo(GetFragmentIndex(fixture.Graph, fixture.CachedProducer)));
            Assert.That(cacheBoundaries[0].AfterFragmentIndex, Is.Null);
            Assert.That(cacheBoundaries[0].Reason,
                Is.EqualTo(ExecutionIslandBoundaryReason.CacheCapture));
        });
    }

    [Test]
    public void SelectedHit_OmitsReplacedProducerAndPrivateSubtreeFromExecutablePlan()
    {
        GraphFixture fixture = CreateShaderGraph(includeGeometryPrefix: true);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.Hit);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Enabled);
        ExecutionIslandBoundary[] cacheBoundaries = plan.Boundaries
            .Where(static boundary => boundary.Reason is ExecutionIslandBoundaryReason.CacheInput
                or ExecutionIslandBoundaryReason.CacheCapture)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Islands, Has.Exactly(1).Items);
            Assert.That(
                ResolveFragments(fixture.Graph, plan.Islands.Single()),
                Is.EqualTo(new[] { fixture.Tail }));
            Assert.That(ResolveFragments(fixture.Graph, plan.ShaderRuns.Single().StageFragmentIndices)
                    .Select(static fragment => fragment.Id),
                Is.EqualTo(new[] { fixture.Tail.Id }));
            Assert.That(ResolveFragments(fixture.Graph, plan.Islands),
                Has.None.SameAs(fixture.CachedProducer));
            Assert.That(ResolveFragments(fixture.Graph, plan.Islands),
                Has.None.SameAs(fixture.Prefix));
            Assert.That(plan.Boundaries, Has.None.Matches<ExecutionIslandBoundary>(static boundary =>
                boundary.Reason == ExecutionIslandBoundaryReason.Geometry));
            Assert.That(cacheBoundaries, Has.Exactly(1).Items);
            Assert.That(cacheBoundaries[0].BeforeFragmentIndex, Is.Null);
            Assert.That(
                cacheBoundaries[0].AfterFragmentIndex,
                Is.EqualTo(GetFragmentIndex(fixture.Graph, fixture.CachedProducer)));
            Assert.That(cacheBoundaries[0].Reason,
                Is.EqualTo(ExecutionIslandBoundaryReason.CacheInput));
        });
    }

    [Test]
    public void SelectedHit_DoesNotPruneSubtreeStillPublishedByAnotherRoot()
    {
        GraphFixture fixture = CreateShaderGraph(
            includeGeometryPrefix: true,
            publishPrefix: true);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.Hit);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Enabled);

        Assert.Multiple(() =>
        {
            Assert.That(ResolveFragments(fixture.Graph, plan.Islands),
                Has.Some.SameAs(fixture.Prefix));
            Assert.That(ResolveFragments(fixture.Graph, plan.Islands),
                Has.None.SameAs(fixture.CachedProducer));
            Assert.That(plan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(static boundary =>
                boundary.Reason == ExecutionIslandBoundaryReason.Geometry));
            Assert.That(plan.Boundaries.Count(static boundary =>
                boundary.Reason == ExecutionIslandBoundaryReason.CacheInput), Is.EqualTo(1));
        });
    }

    [Test]
    public void StructuralIdentity_DistinguishesHitFromMissCaptureAtSameCandidate()
    {
        GraphFixture fixture = CreateShaderGraph(includeGeometryPrefix: false);
        RenderCacheResolution hit = CreateResolution(fixture, RenderCacheResolutionKind.Hit);
        RenderCacheResolution miss = CreateResolution(fixture, RenderCacheResolutionKind.MissCapture);
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds);

        StructuralPlanIdentity hitIdentity = StructuralPlanIdentity.Create(
            options.PlanIdentity,
            fixture.Graph,
            SkslBackendBudget.Unlimited,
            hit);
        StructuralPlanIdentity missIdentity = StructuralPlanIdentity.Create(
            options.PlanIdentity,
            fixture.Graph,
            SkslBackendBudget.Unlimited,
            miss);

        Assert.That(hitIdentity, Is.Not.EqualTo(missIdentity));
    }

    private static ExecutionIslandPlan Plan(
        GraphFixture fixture,
        RenderCacheResolution resolution,
        FusionMode fusionMode)
        => new ExecutionIslandPlanner().Plan(
            fixture.Graph,
            RenderRequestCompiler.ResolveRoots(fixture.Graph),
            resolution,
            fusionMode,
            SkslBackendBudget.Unlimited);

    private static int GetFragmentIndex(
        RecordedRenderGraph graph,
        RenderFragmentReference fragment)
    {
        int index = graph.Fragments.IndexOf(fragment);
        return index >= 0
            ? index
            : throw new InvalidOperationException("The fixture fragment is not part of the recorded graph.");
    }

    private static IEnumerable<RenderFragmentReference> ResolveFragments(
        RecordedRenderGraph graph,
        ExecutionIsland island)
        => island.FragmentIndices.Select(index => graph.Fragments[index]);

    private static IEnumerable<RenderFragmentReference> ResolveFragments(
        RecordedRenderGraph graph,
        IEnumerable<int> fragmentIndices)
        => fragmentIndices.Select(index => graph.Fragments[index]);

    private static IEnumerable<RenderFragmentReference> ResolveFragments(
        RecordedRenderGraph graph,
        IEnumerable<ExecutionIsland> islands)
        => islands.SelectMany(island => ResolveFragments(graph, island));

    private static RenderCacheResolution CreateResolution(
        GraphFixture fixture,
        RenderCacheResolutionKind kind,
        RenderCacheBypassReason bypassReason = RenderCacheBypassReason.None)
    {
        RenderCacheCandidate candidate = fixture.Graph.CacheCandidates.Single();
        RenderFragmentReference recorded = fixture.Graph.GetFragment(candidate.FragmentId);
        var identity = new RenderOutputCacheIdentity(
            candidate.CacheKey,
            RenderFragmentOutputIdentity.Create(fixture.CachedProducer, fixture.Graph.RequestId),
            fixture.CachedProducer.Bounds,
            RequiredRegion.Full,
            density: 1,
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            FusionMode.Enabled,
            new RenderCacheDeviceContextIdentity("planner-test-device", "planner-test-context"));

        RenderCacheDecision decision = kind switch
        {
            RenderCacheResolutionKind.Bypass => new RenderCacheDecision(
                candidate,
                kind,
                bypassReason,
                identity,
                null,
                null,
                null),
            RenderCacheResolutionKind.Hit => new RenderCacheDecision(
                candidate,
                kind,
                RenderCacheBypassReason.None,
                identity,
                new RenderCacheHitSubstitution(
                    candidate.Id,
                    recorded.Id!.Value,
                    identity,
                    new RenderCacheEntry(identity, new object())),
                null,
                null),
            RenderCacheResolutionKind.MissCapture => new RenderCacheDecision(
                candidate,
                kind,
                RenderCacheBypassReason.None,
                identity,
                null,
                new RenderCacheMissCapture(
                    candidate.Id,
                    recorded.Id!.Value,
                    identity),
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new RenderCacheResolution([decision]);
    }

    private static GraphFixture CreateShaderGraph(
        bool includeGeometryPrefix,
        bool publishPrefix = false)
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference source = Fragment(
            RenderFragmentKind.MaterializedInput,
            payload: null,
            EffectiveScale.At(1));
        RenderFragmentReference prefix = includeGeometryPrefix
            ? Fragment(RenderFragmentKind.Geometry, payload: null, EffectiveScale.At(1), source)
            : source;
        RenderFragmentReference cachedProducer = CurrentPixel(prefix, "return color * 0.75;");
        RenderFragmentReference tail = CurrentPixel(cachedProducer, "return half4(color.bgr, color.a);");
        RenderFragmentReference[] references = includeGeometryPrefix
            ? [source, prefix, cachedProducer, tail]
            : [source, cachedProducer, tail];

        var builder = new RecordedRenderGraphBuilder(requestId);
        foreach (RenderFragmentReference reference in references)
            builder.AddFragment(reference);

        builder.AddCacheCandidate(cachedProducer.Id!.Value, "selected-candidate");
        builder.PublishRoot(tail.Id!.Value);
        if (publishPrefix)
            builder.PublishRoot(prefix.Id!.Value);

        return new GraphFixture(builder.Build(), prefix, cachedProducer, tail);
    }

    private static RenderFragmentReference CurrentPixel(
        RenderFragmentReference input,
        string body)
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            $"half4 apply(half4 color) {{ {body} }}");
        return Fragment(
            RenderFragmentKind.Shader,
            new ShaderRenderFragmentPayload(description),
            EffectiveScale.Unbounded,
            input);
    }

    private static RenderFragmentReference Fragment(
        RenderFragmentKind kind,
        object? payload,
        EffectiveScale scale,
        params RenderFragmentReference[] inputs)
        => new(
            kind,
            s_bounds,
            scale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: inputs.Any(static input => input.HasTargetEffects),
            hasOpaqueExternalWork: inputs.Any(static input => input.HasOpaqueExternalWork),
            [.. inputs],
            payload,
            RenderFragmentHitTest.Bounds);

    private sealed record GraphFixture(
        RecordedRenderGraph Graph,
        RenderFragmentReference Prefix,
        RenderFragmentReference CachedProducer,
        RenderFragmentReference Tail);
}
