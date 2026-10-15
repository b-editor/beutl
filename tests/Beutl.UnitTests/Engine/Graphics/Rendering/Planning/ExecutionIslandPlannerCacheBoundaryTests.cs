using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class ExecutionIslandPlannerCacheBoundaryTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 24);

    [Test]
    public void BypassedCandidate_DoesNotSplitMaximalCompatibleRun()
    {
        GraphFixture fixture = CreateShaderGraph(includeEffectPrefix: false);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.Bypass);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Enabled);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ShaderRuns, Has.Exactly(1).Items);
            Assert.That(ResolveFragments(fixture.Graph, plan.ShaderRuns.Single().StageFragmentIndices)
                    .Select(static fragment => fragment.Id),
                Is.EqualTo(new[] { fixture.CachedProducer.Id, fixture.Tail.Id }));
        });
    }

    [Test]
    public void BypassedCandidate_WithFusionDisabledUsesOnlyFusionDisabledSplit()
    {
        GraphFixture fixture = CreateShaderGraph(includeEffectPrefix: false);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.Bypass);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Disabled);

        Assert.That(plan.ShaderRuns.Select(static run => run.StageFragmentIndices.Length),
            Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void SelectedMissCapture_SplitsTheRunAfterTheProducer()
    {
        GraphFixture fixture = CreateShaderGraph(includeEffectPrefix: false);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.MissCapture);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Enabled);

        Assert.That(
            plan.ShaderRuns.Select(run => ResolveFragments(fixture.Graph, run.StageFragmentIndices).Single()),
            Is.EqualTo(new[] { fixture.CachedProducer, fixture.Tail }));
    }

    [Test]
    public void SelectedHit_OmitsReplacedProducerAndPrivateSubtreeFromExecutablePlan()
    {
        GraphFixture fixture = CreateShaderGraph(includeEffectPrefix: true);
        RenderCacheResolution resolution = CreateResolution(
            fixture,
            RenderCacheResolutionKind.Hit);

        ExecutionIslandPlan plan = Plan(fixture, resolution, FusionMode.Enabled);

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
            Assert.That(plan.ShaderRuns.Single().GetInput(fixture.Graph), Is.SameAs(fixture.CachedProducer));
        });
    }

    [Test]
    public void SelectedHit_DoesNotPruneSubtreeStillPublishedByAnotherRoot()
    {
        GraphFixture fixture = CreateShaderGraph(
            includeEffectPrefix: true,
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
        });
    }

    [Test]
    public void StructuralIdentity_DistinguishesHitFromMissCaptureAtSameCandidate()
    {
        GraphFixture fixture = CreateShaderGraph(includeEffectPrefix: false);
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
        RenderCacheResolutionKind kind)
    {
        RenderCacheCandidate candidate = fixture.Graph.CacheCandidates.Single();
        return new RenderCacheResolution([CreateDecision(fixture, candidate, kind)]);
    }

    private static RenderCacheDecision CreateDecision(
        GraphFixture fixture,
        RenderCacheCandidate candidate,
        RenderCacheResolutionKind kind)
        => kind == RenderCacheResolutionKind.Bypass
            ? new RenderCacheDecision(candidate, kind, null, null)
            : RenderCacheTestSupport.CreateDecision(candidate, kind, fixture.CachedProducer.Bounds);

    private static GraphFixture CreateShaderGraph(
        bool includeEffectPrefix,
        bool publishPrefix = false)
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference source = FusionBoundaryExecutionTestSupport.SourceFragment(
            s_bounds,
            EffectiveScale.At(1));
        RenderFragmentReference prefix = includeEffectPrefix
            ? Fragment(RenderFragmentKind.FilterEffectSegment, payload: null, EffectiveScale.At(1), source)
            : source;
        RenderFragmentReference cachedProducer = CurrentPixel(prefix, "return color * 0.75;");
        RenderFragmentReference tail = CurrentPixel(cachedProducer, "return half4(color.bgr, color.a);");
        RenderFragmentReference[] references = includeEffectPrefix
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
