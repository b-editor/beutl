using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class RenderCacheResolutionTests
{
    private static readonly Rect s_bounds = new(0, 0, 64, 64);
    private static readonly RenderCacheResolutionContext s_context = new(
        RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
        new RenderCacheDeviceContextIdentity("device-a", "context-a"));

    [Test]
    public void Recorder_DeclaresOnlyWarmEnabledNodeCandidatesWithoutReadingCachePixels()
    {
        using var coldNode = new CacheableNode(disableCache: false);
        using var warmNode = new CacheableNode(disableCache: false);
        using var disabledNode = new CacheableNode(disableCache: true);
        warmNode.Cache.RecordStableRequests();
        disabledNode.Cache.RecordStableRequests();

        using var coldRequest = NewRequest();
        using var firstWarmRequest = NewRequest();
        using var secondWarmRequest = NewRequest();
        using var disabledRequest = NewRequest();
        RecordedRenderGraph cold = new RenderRequestRecorder(coldRequest).Record(coldNode);
        RecordedRenderGraph firstWarm = new RenderRequestRecorder(firstWarmRequest).Record(warmNode);
        RecordedRenderGraph secondWarm = new RenderRequestRecorder(secondWarmRequest).Record(warmNode);
        RecordedRenderGraph disabled = new RenderRequestRecorder(disabledRequest).Record(disabledNode);

        Assert.Multiple(() =>
        {
            Assert.That(cold.CacheCandidates, Is.Empty);
            Assert.That(disabled.CacheCandidates, Is.Empty);
            Assert.That(firstWarm.CacheCandidates.Length, Is.EqualTo(1));
            Assert.That(firstWarm.CacheCandidates.Single().Cache, Is.SameAs(warmNode.Cache));
            Assert.That(
                secondWarm.CacheCandidates.Single().CacheKey,
                Is.SameAs(firstWarm.CacheCandidates.Single().CacheKey));
            Assert.That(warmNode.ExecuteCount, Is.EqualTo(0));
        });
    }

    /// <remarks>
    /// A node reachable from two parents is recorded once per parent. Both recordings point at the same
    /// RenderNodeCache, so offering both as candidates lets one family try to publish two independent outputs
    /// to one cache, which the executor rejects by failing the frame.
    /// </remarks>
    [Test]
    public void Recorder_OffersOneCandidatePerNodeEvenWhenTwoParentsShareIt()
    {
        var shared = new CacheableNode(disableCache: false);
        shared.Cache.RecordStableRequests();
        using var container = new ContainerRenderNode();
        container.AddChild(new ReferencesChildRenderNode(shared));
        container.AddChild(new ReferencesChildRenderNode(shared));

        using var request = NewRequest();
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(container);

        Assert.That(
            graph.CacheCandidates.Count(candidate => ReferenceEquals(candidate.Cache, shared.Cache)),
            Is.EqualTo(1),
            "A shared node must offer its cache one candidate, not one per parent.");
    }

    [Test]
    public void Recorder_KeepsSiblingsCacheableAfterOneOfThemOptsOut()
    {
        var disabledNode = new CacheableNode(disableCache: true);
        var laterNode = new CacheableNode(disableCache: false);
        disabledNode.Cache.RecordStableRequests();
        laterNode.Cache.RecordStableRequests();
        using var container = new ContainerRenderNode();
        container.AddChild(disabledNode);
        container.AddChild(laterNode);

        using var request = NewRequest();
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(container);

        Assert.That(
            graph.CacheCandidates.Select(static candidate => candidate.Cache),
            Is.EqualTo(new[] { laterNode.Cache }),
            "A container hierarchy is recorded onto one parent checkpoint, so a node opting out of the "
            + "cache must not decide for the siblings recorded after it.");
    }

    [Test]
    public void FrameCache_ColdMissPublishesAndWarmHitSkipsProducerWithPixelParity()
    {
        using var node = new SolidCacheNode();
        node.Cache.RecordStableRequests();
        using var renderer = CreateFrameRenderer(node);

        using RenderNodeRasterization cold = renderer.Rasterize();
        using RenderNodeRasterization warm = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(cold.Bitmap, Is.Not.Null);
            Assert.That(warm.Bitmap, Is.Not.Null);
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(
                warm.Bitmap!.GetPixelSpan<ushort>().SequenceEqual(cold.Bitmap!.GetPixelSpan<ushort>()),
                Is.True);
        });
    }

    [Test]
    public void StaticPrefixCache_AcceptsHundredAnimatedFramesWithZeroPrefixExecution()
    {
        using var node = new SolidCacheNode();
        node.Cache.RecordStableRequests();
        using var renderer = CreateFrameRenderer(node);

        using RenderNodeRasterization first = renderer.Rasterize();
        Assert.That(node.ExecuteCount, Is.EqualTo(1),
            "the static prefix must execute exactly once on the cold frame");
        ushort[] firstPixels = first.Bitmap!.GetPixelSpan<ushort>().ToArray();

        for (int frame = 1; frame < 100; frame++)
        {
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            Assert.That(
                rasterization.Bitmap!.GetPixelSpan<ushort>().SequenceEqual(firstPixels),
                Is.True,
                $"warm frame {frame} must match the cold prefix output (SC-012)");
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCount, Is.EqualTo(1),
                "the static prefix must not re-execute across 100 animated frames (SC-012)");
            Assert.That(node.Cache.IsCached, Is.True);
        });
    }

    [Test]
    public void ExecutionFailure_RejectsEveryStagedCaptureWithoutPartialPublication()
    {
        using var root = new ContainerRenderNode();
        var completed = new SolidCacheNode();
        var failing = new SolidCacheNode(throwOnExecute: true);
        completed.Cache.RecordStableRequests();
        failing.Cache.RecordStableRequests();
        root.AddChild(completed);
        root.AddChild(failing);
        using var renderer = CreateFrameRenderer(root);

        Assert.That(() => renderer.Rasterize(), Throws.InvalidOperationException);
        Assert.Multiple(() =>
        {
            Assert.That(completed.ExecuteCount, Is.EqualTo(1));
            Assert.That(failing.ExecuteCount, Is.EqualTo(1));
            Assert.That(completed.Cache.IsCached, Is.False);
            Assert.That(failing.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void PublicationFailure_RejectsTheWholeBatch()
    {
        using var root = new ContainerRenderNode();
        var first = new SolidCacheNode();
        var invalidatedOwner = new SolidCacheNode();
        invalidatedOwner.OnExecute = invalidatedOwner.Cache.Dispose;
        first.Cache.RecordStableRequests();
        invalidatedOwner.Cache.RecordStableRequests();
        root.AddChild(first);
        root.AddChild(invalidatedOwner);
        using var renderer = CreateFrameRenderer(root);

        Assert.That(() => renderer.Rasterize(), Throws.InstanceOf<ObjectDisposedException>());
        Assert.Multiple(() =>
        {
            Assert.That(first.Cache.IsCached, Is.False);
            Assert.That(invalidatedOwner.Cache.IsCached, Is.False);
        });
    }


    [Test]
    public void AuxiliaryRequests_MayNotPublishPersistentMisses()
    {
        using var node = new SolidCacheNode();
        node.Cache.RecordStableRequests();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                },
            });

        using (renderer.Rasterize())
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCount, Is.EqualTo(2));
            Assert.That(node.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void ParentHit_SupersedesChildWithoutLookingUpOrRewritingIt()
    {
        RenderFragmentReference child = Pure();
        RenderFragmentReference parent = Pure([child]);
        using Scenario scenario = Build(
            [child, parent],
            [parent],
            [(child, "child"), (parent, "parent")]);
        RenderCacheResolution cold = Resolve(scenario);
        var lookup = new RecordingLookup();
        lookup.AddRange(cold.MissCaptures);

        RenderCacheResolution warmed = Resolve(scenario, lookup);
        RenderCacheDecision childDecision = warmed.GetDecision(scenario.Candidate(child));
        RenderCacheDecision parentDecision = warmed.GetDecision(scenario.Candidate(parent));

        Assert.Multiple(() =>
        {
            Assert.That(parentDecision.Kind, Is.EqualTo(RenderCacheResolutionKind.Hit));
            Assert.That(childDecision.Kind, Is.EqualTo(RenderCacheResolutionKind.Superseded));
            Assert.That(childDecision.SupersededBy, Is.EqualTo(parentDecision.Candidate.Id));
            Assert.That(lookup.RequestedKeys, Is.EqualTo(new object[] { "parent" }));
            Assert.That(parent.Inputs.Single(), Is.SameAs(child));
            Assert.That(scenario.Graph.Fragments.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void ParentMiss_LeavesValidChildHitSelectableAndStagesTheParent()
    {
        RenderFragmentReference child = Pure();
        RenderFragmentReference parent = Pure([child], payload: new RuntimeValue(1));
        using Scenario scenario = Build(
            [child, parent],
            [parent],
            [(child, "child"), (parent, "parent")]);
        RenderCacheResolution cold = Resolve(scenario);
        var lookup = new RecordingLookup();
        lookup.Add(cold.GetDecision(scenario.Candidate(child)).MissCapture!);

        RenderCacheResolution warmed = Resolve(scenario, lookup);

        Assert.Multiple(() =>
        {
            Assert.That(
                warmed.GetDecision(scenario.Candidate(parent)).Kind,
                Is.EqualTo(RenderCacheResolutionKind.MissCapture));
            Assert.That(
                warmed.GetDecision(scenario.Candidate(child)).Kind,
                Is.EqualTo(RenderCacheResolutionKind.Hit));
            Assert.That(warmed.Hits.Single().OriginalProducerId, Is.EqualTo(child.Id));
            Assert.That(warmed.MissCaptures.Single().ProducerId, Is.EqualTo(parent.Id));
            Assert.That(lookup.RequestedKeys, Is.EqualTo(new object[] { "parent", "child" }));
        });
    }

    [TestCase("TargetCommand", "TargetTokenDependency")]
    [TestCase("RawTargetScope", "RawTargetWork")]
    [TestCase("TargetCapture", "TargetTokenDependency")]
    public void TargetAndRawCandidates_BypassWhilePureChildrenRemainSelectable(
        string boundaryKindName,
        string expectedReasonName)
    {
        RenderFragmentKind boundaryKind = Enum.Parse<RenderFragmentKind>(boundaryKindName);
        RenderCacheBypassReason expectedReason = Enum.Parse<RenderCacheBypassReason>(expectedReasonName);
        RenderFragmentReference child = Pure();
        RenderFragmentReference boundary = Boundary(boundaryKind, child);
        RenderFragmentReference[] roots = boundaryKind == RenderFragmentKind.TargetCapture
            ? [child, boundary]
            : [boundary];
        using Scenario scenario = Build(
            [child, boundary],
            roots,
            [(child, "child"), (boundary, "boundary")]);
        RenderCacheResolution cold = Resolve(scenario);
        var lookup = new RecordingLookup();
        RenderCacheMissCapture? childCapture = cold
            .GetDecision(scenario.Candidate(child))
            .MissCapture;
        if (childCapture is not null)
            lookup.Add(childCapture);

        RenderCacheResolution warmed = Resolve(scenario, lookup);
        RenderCacheDecision boundaryDecision = warmed.GetDecision(scenario.Candidate(boundary));
        RenderCacheDecision childDecision = warmed.GetDecision(scenario.Candidate(child));

        Assert.Multiple(() =>
        {
            Assert.That(boundaryDecision.Kind, Is.EqualTo(RenderCacheResolutionKind.Bypass));
            Assert.That(boundaryDecision.BypassReason, Is.EqualTo(expectedReason));
            if (childCapture is not null)
                Assert.That(childDecision.Kind, Is.EqualTo(RenderCacheResolutionKind.Hit));
        });
    }

    [Test]
    public void CompleteIdentity_InvalidatesCoverageDensityFormatPurposeDeviceContextAndBounds()
    {
        using Scenario baseline = SingleCandidate();
        RenderCacheResolution cold = Resolve(baseline);
        var lookup = new RecordingLookup();
        lookup.Add(cold.MissCaptures.Single());

        AssertMiss(SingleCandidate(requestedRegion: new Rect(0, 0, 32, 64)), s_context, lookup);
        AssertMiss(SingleCandidate(outputScale: 2), s_context, lookup);
        AssertMiss(
            SingleCandidate(),
            new RenderCacheResolutionContext(
                new RenderCacheFormatIdentity("RGBA8", "Premultiplied", "LinearSrgb"),
                s_context.DeviceContext),
            lookup);
        AssertMiss(SingleCandidate(purpose: RenderRequestPurpose.Auxiliary), s_context, lookup);
        AssertMiss(
            SingleCandidate(),
            new RenderCacheResolutionContext(
                s_context.Format,
                new RenderCacheDeviceContextIdentity("device-b", "context-a")),
            lookup);
        AssertMiss(
            SingleCandidate(),
            new RenderCacheResolutionContext(
                s_context.Format,
                new RenderCacheDeviceContextIdentity("device-a", "context-b")),
            lookup);
        AssertMiss(SingleCandidate(bounds: new Rect(0, 0, 63, 64)), s_context, lookup);
    }

    [Test]
    public void BinderFreeShaderIdentity_ReusesAcrossUnobservedSharedStageRequirement()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        using Scenario baseline = ShaderFanOut(description, widenSiblingRequirement: false);
        RenderCacheResolution cold = Resolve(baseline);
        var lookup = new RecordingLookup();
        lookup.Add(cold.MissCaptures.Single());

        using Scenario expanded = ShaderFanOut(description, widenSiblingRequirement: true);
        RenderCacheResolution resolution = Resolve(expanded, lookup);

        Assert.Multiple(() =>
        {
            Assert.That(
                expanded.Regions.GetFragmentRequirement(expanded.Named("shared")),
                Is.Not.EqualTo(baseline.Regions.GetFragmentRequirement(baseline.Named("shared"))));
            Assert.That(resolution.Hits, Has.Length.EqualTo(1));
            Assert.That(resolution.MissCaptures, Is.Empty);
        });
    }

    [Test]
    public void BinderFreeCandidateIdentity_ReusesWhenExternalReusableSiblingChangesSharedRequirement()
    {
        using Scenario baseline = ExternalReusableShaderFanOut(widenSiblingRequirement: false);
        RenderCacheResolution cold = Resolve(baseline);
        var lookup = new RecordingLookup();
        lookup.Add(cold.MissCaptures.Single());

        using Scenario expanded = ExternalReusableShaderFanOut(widenSiblingRequirement: true);
        RenderCacheResolution resolution = Resolve(expanded, lookup);

        Assert.Multiple(() =>
        {
            Assert.That(
                expanded.Regions.GetFragmentRequirement(expanded.Named("candidate")),
                Is.EqualTo(baseline.Regions.GetFragmentRequirement(baseline.Named("candidate"))));
            Assert.That(
                baseline.Regions.GetFragmentRequirement(baseline.Named("producer")),
                Is.EqualTo(RequiredRegion.Region(new Rect(16, 16, 16, 16))));
            Assert.That(
                expanded.Regions.GetFragmentRequirement(expanded.Named("producer")),
                Is.EqualTo(RequiredRegion.Region(new Rect(8, 8, 32, 32))));
            Assert.That(resolution.Hits, Has.Length.EqualTo(1));
            Assert.That(resolution.MissCaptures, Is.Empty);
        });
    }

    [Test]
    public void GridSensitiveIdentity_DistinguishesIntegralDestinationTranslations()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        var firstContext = new RenderCacheResolutionContext(
            s_context.Format,
            s_context.DeviceContext,
            deviceGridOffset: new Vector(1, 1));
        var secondContext = new RenderCacheResolutionContext(
            s_context.Format,
            s_context.DeviceContext,
            deviceGridOffset: new Vector(2, 2));
        var lookup = new RecordingLookup();
        using (Scenario first = ShaderCandidate(description))
        {
            RenderCacheResolution cold = Resolve(first, context: firstContext);
            lookup.AddRange(cold.MissCaptures);
            Assert.That(
                cold.MissCaptures.Single().Identity.DeviceGridOffset,
                Is.EqualTo(new Vector(1, 1)));
        }

        using Scenario second = ShaderCandidate(description);
        RenderCacheResolution moved = Resolve(second, lookup, secondContext);

        Assert.Multiple(() =>
        {
            Assert.That(moved.Hits, Is.Empty);
            Assert.That(moved.MissCaptures, Has.Length.EqualTo(1));
            Assert.That(
                moved.MissCaptures.Single().Identity.DeviceGridOffset,
                Is.EqualTo(new Vector(2, 2)));
        });
    }

    [TestCase(2f, 2f)]
    [TestCase(1.5f, 1.5f)]
    public void DivergentFanOut_ColdAndWarmCacheUseHighestCappedDensity(
        float maxWorkingScale,
        float expectedDensity)
    {
        RenderFragmentReference source = Pure();
        RenderFragmentReference unitScale = FixedScaleMap(source, 1, 1);
        RenderFragmentReference doubleScale = FixedScaleMap(source, 2, expectedDensity);
        using Scenario scenario = Build(
            [source, unitScale, doubleScale],
            [unitScale, doubleScale],
            [(source, "source")],
            maxWorkingScale: maxWorkingScale);

        RenderCacheResolution cold = Resolve(scenario);
        RenderCacheMissCapture capture = cold.MissCaptures.Single();
        var lookup = new RecordingLookup();
        lookup.Add(capture);

        RenderCacheResolution warm = Resolve(scenario, lookup);
        RenderCacheDecision decision = warm.GetDecision(scenario.Candidate(source));

        Assert.Multiple(() =>
        {
            Assert.That(capture.Identity.Density, Is.EqualTo(expectedDensity));
            Assert.That(decision.Kind, Is.EqualTo(RenderCacheResolutionKind.Hit));
            Assert.That(decision.Hit!.Entry.Identity.Density, Is.EqualTo(expectedDensity));
        });
    }

    [Test]
    public void MaterializationDemands_OpacityMaskDependencyUsesActiveTargetDensity()
    {
        RenderFragmentReference primary = Pure(scale: EffectiveScale.At(0.5f));
        RenderFragmentReference maskDependency = Pure();
        var opacityMask = new RenderFragmentReference(
            RenderFragmentKind.OpacityMask,
            s_bounds,
            EffectiveScale.At(0.5f),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [primary, maskDependency],
            payload: null,
            static _ => true);

        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
            RenderMaterializationDemandResolver.Resolve(
                [opacityMask],
                outputScale: 1,
                maxWorkingScale: float.PositiveInfinity).Demands;

        Assert.Multiple(() =>
        {
            Assert.That(demands[opacityMask], Is.EqualTo(EffectiveScale.At(0.5f)));
            Assert.That(demands[primary], Is.EqualTo(EffectiveScale.At(0.5f)));
            Assert.That(demands[maskDependency], Is.EqualTo(EffectiveScale.At(1)));
        });
    }

    [Test]
    public void MaterializationDemands_CachedOpacityMaskDependencyUsesValueDensity()
    {
        RenderFragmentReference primary = Pure(scale: EffectiveScale.At(0.5f));
        RenderFragmentReference maskDependency = Pure();
        var opacityMask = new RenderFragmentReference(
            RenderFragmentKind.OpacityMask,
            s_bounds,
            EffectiveScale.At(0.5f),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [primary, maskDependency],
            payload: null,
            static _ => true);
        var boundaries = new HashSet<RenderFragmentReference>(
            ReferenceEqualityComparer.Instance)
        {
            opacityMask,
        };

        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
            RenderMaterializationDemandResolver.Resolve(
                [opacityMask],
                outputScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                boundaries).Demands;

        Assert.Multiple(() =>
        {
            Assert.That(demands[opacityMask], Is.EqualTo(EffectiveScale.At(0.5f)));
            Assert.That(demands[primary], Is.EqualTo(EffectiveScale.At(0.5f)));
            Assert.That(demands[maskDependency], Is.EqualTo(EffectiveScale.At(0.5f)));
        });
    }




    [Test]
    public void OpacityMaskIdentity_IncludesUnboundedDependencyMaterializationDemand()
    {
        using Scenario unitScale = OpacityMaskCandidate(outputScale: 1);
        using Scenario doubleScale = OpacityMaskCandidate(outputScale: 2);
        ImmutableArray<RenderFragmentReference> unitRoots =
            RenderRequestCompiler.ResolveRoots(unitScale.Graph);
        ImmutableArray<RenderFragmentReference> doubleRoots =
            RenderRequestCompiler.ResolveRoots(doubleScale.Graph);
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> unitDemands =
            RenderMaterializationDemandResolver.Resolve(
                unitRoots,
                outputScale: 1,
                maxWorkingScale: float.PositiveInfinity).Demands;
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> doubleDemands =
            RenderMaterializationDemandResolver.Resolve(
                doubleRoots,
                outputScale: 2,
                maxWorkingScale: float.PositiveInfinity).Demands;

        RenderFragmentOutputIdentity unitIdentity = RenderFragmentOutputIdentity.Create(
            unitRoots.Single(),
            unitScale.Graph.RequestId,
            unitDemands);
        RenderFragmentOutputIdentity doubleIdentity = RenderFragmentOutputIdentity.Create(
            doubleRoots.Single(),
            doubleScale.Graph.RequestId,
            doubleDemands);

        Assert.Multiple(() =>
        {
            Assert.That(
                unitDemands[unitScale.Named("dependency")],
                Is.EqualTo(EffectiveScale.At(1)));
            Assert.That(
                doubleDemands[doubleScale.Named("dependency")],
                Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(doubleIdentity, Is.Not.EqualTo(unitIdentity));
        });
    }

    [Test]
    public void SingleCandidate_ColdAndWarmConvergeWithinTwoPassesAndProbeLookupOnce()
    {
        using Scenario scenario = SingleCandidate();
        var lookup = new RecordingLookup();

        RenderCachePlanningResult cold = ResolvePlanning(scenario, lookup);
        Assert.Multiple(() =>
        {
            Assert.That(cold.ResolutionPasses, Is.InRange(1, 2));
            Assert.That(cold.Resolution.MissCaptures, Has.Length.EqualTo(1));
            Assert.That(lookup.RequestedKeys, Is.EqualTo(new object[] { "source" }));
        });

        lookup.Add(cold.Resolution.MissCaptures.Single());
        lookup.RequestedKeys.Clear();
        RenderCachePlanningResult warm = ResolvePlanning(scenario, lookup);

        Assert.Multiple(() =>
        {
            Assert.That(warm.ResolutionPasses, Is.InRange(1, 2));
            Assert.That(warm.Resolution.Hits, Has.Length.EqualTo(1));
            Assert.That(lookup.RequestedKeys, Is.EqualTo(new object[] { "source" }));
        });
    }

    [Test]
    public void LookupOnlyStableHit_ConvergesInTwoPassesWithOneUnderlyingProbe()
    {
        using Scenario scenario = SingleCandidate();
        RenderCachePlanningResult cold = ResolvePlanning(scenario);
        var lookup = new RecordingLookup();
        lookup.Add(cold.Resolution.MissCaptures.Single());
        var lookupOnlyContext = new RenderCacheResolutionContext(
            s_context.Format,
            s_context.DeviceContext,
            allowPersistentLookup: true,
            allowCapturePublication: false);

        RenderCachePlanningResult result = ResolvePlanning(
            scenario,
            lookup,
            lookupOnlyContext);

        Assert.Multiple(() =>
        {
            Assert.That(result.ResolutionPasses, Is.EqualTo(2));
            Assert.That(result.Resolution.Hits, Has.Length.EqualTo(1));
            Assert.That(result.Resolution.MissCaptures, Is.Empty);
            Assert.That(lookup.RequestedKeys, Is.EqualTo(new object[] { "source" }));
        });
    }

    [Test]
    public void FourPassBoundaryCascade_FallsBackWithUncachedDemandsAndReportsTheCap()
    {
        RenderFragmentReference source = Pure();
        RenderFragmentReference fourth = ValueReplayMap(
            source,
            EffectiveScale.At(0.0625f),
            "fourth-runtime");
        RenderFragmentReference third = ValueReplayMap(
            fourth,
            EffectiveScale.At(0.125f),
            "third-runtime");
        RenderFragmentReference second = ValueReplayMap(
            third,
            EffectiveScale.At(0.25f),
            "second-runtime");
        RenderFragmentReference first = ValueReplayMap(
            second,
            EffectiveScale.At(0.5f),
            "first-runtime");
        using Scenario scenario = Build(
            [source, fourth, third, second, first],
            [first],
            [
                (fourth, "fourth"),
                (third, "third"),
                (second, "second"),
                (first, "first"),
            ],
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["source"] = source,
            });
        var lookup = new DelayedIdentityHitLookup(
            new Dictionary<object, int>
            {
                ["first"] = 1,
                ["second"] = 2,
                ["third"] = 3,
                ["fourth"] = 4,
            });
        var lookupOnlyContext = new RenderCacheResolutionContext(
            s_context.Format,
            s_context.DeviceContext,
            allowPersistentLookup: true,
            allowCapturePublication: false);

        RenderCachePlanningResult result = ResolvePlanning(
            scenario,
            lookup,
            lookupOnlyContext);

        Assert.Multiple(() =>
        {
            Assert.That(result.ResolutionPasses, Is.EqualTo(4));
            Assert.That(
                result.Resolution.Decisions,
                Has.All.Property(nameof(RenderCacheDecision.BypassReason))
                    .EqualTo(RenderCacheBypassReason.UnstableBoundaryPlan));
            Assert.That(
                result.MaterializationDemands[scenario.Named("source")],
                Is.EqualTo(EffectiveScale.At(1)));
        });
    }

    [Test]
    public void OpacityMaskCacheBoundary_ColdAndWarmCrossDensityUseStableValueDemand()
    {
        using Scenario coldScenario = OpacityMaskCandidate(outputScale: 1);
        RenderCachePlanningResult cold = ResolvePlanning(coldScenario);
        var lookup = new RecordingLookup();
        lookup.Add(cold.Resolution.MissCaptures.Single());

        using Scenario warmScenario = OpacityMaskCandidate(outputScale: 2);
        RenderCachePlanningResult warm = ResolvePlanning(warmScenario, lookup);

        Assert.Multiple(() =>
        {
            Assert.That(
                cold.MaterializationDemands[coldScenario.Named("dependency")],
                Is.EqualTo(EffectiveScale.At(0.5f)));
            Assert.That(
                warm.MaterializationDemands[warmScenario.Named("dependency")],
                Is.EqualTo(EffectiveScale.At(0.5f)));
            Assert.That(warm.Resolution.Hits.Length, Is.EqualTo(1));
            Assert.That(
                warm.Resolution.Hits.Single().Identity,
                Is.EqualTo(cold.Resolution.MissCaptures.Single().Identity));
        });
    }

    [Test]
    public void LookupOnlyBoundaryCycle_FallsBackToUncachedReplayDemands()
    {
        using Scenario scenario = OpacityMaskCandidate(outputScale: 1);
        var lookup = new FirstIdentityOnlyLookup();
        var lookupOnlyContext = new RenderCacheResolutionContext(
            s_context.Format,
            s_context.DeviceContext,
            allowPersistentLookup: true,
            allowCapturePublication: false);

        RenderCachePlanningResult result = ResolvePlanning(
            scenario,
            lookup,
            lookupOnlyContext);

        Assert.Multiple(() =>
        {
            Assert.That(result.Resolution.Hits, Is.Empty);
            Assert.That(result.Resolution.MissCaptures, Is.Empty);
            Assert.That(result.ResolutionPasses, Is.EqualTo(2));
            Assert.That(
                result.Resolution.Decisions.Single().BypassReason,
                Is.EqualTo(RenderCacheBypassReason.UnstableBoundaryPlan));
            Assert.That(
                result.MaterializationDemands[scenario.Named("dependency")],
                Is.EqualTo(EffectiveScale.At(1)));
        });
    }

    [Test]
    public void NestedValueReplayCaches_WarmParentHitUsesRawPlanningBoundaries()
    {
        RenderFragmentReference dependency = Pure();
        RenderFragmentReference child = ValueReplayMap(dependency, EffectiveScale.At(1), "child");
        RenderFragmentReference parent = ValueReplayMap(child, EffectiveScale.At(0.5f), "parent");
        using Scenario scenario = Build(
            [dependency, child, parent],
            [parent],
            [(child, "child"), (parent, "parent")],
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["dependency"] = dependency,
                ["child"] = child,
                ["parent"] = parent,
            });
        RenderCachePlanningResult cold = ResolvePlanning(scenario);
        var lookup = new RecordingLookup();
        lookup.AddRange(cold.Resolution.MissCaptures);

        RenderCachePlanningResult warm = ResolvePlanning(scenario, lookup);

        Assert.Multiple(() =>
        {
            Assert.That(
                warm.Resolution.GetDecision(scenario.Candidate("parent")).Kind,
                Is.EqualTo(RenderCacheResolutionKind.Hit));
            Assert.That(
                warm.Resolution.GetDecision(scenario.Candidate("child")).Kind,
                Is.EqualTo(RenderCacheResolutionKind.Superseded));
            Assert.That(
                warm.MaterializationDemands[scenario.Named("dependency")],
                Is.EqualTo(EffectiveScale.At(1)));
        });
    }

    [Test]
    public void PlanningBoundaryThatBecomesIneligible_IsRemovedFromFinalDemands()
    {
        var expandedBounds = new Rect(0, 0, 12_000, 1);
        var parentBounds = new Rect(0, 0, 32, 32);
        RenderFragmentReference dependency = Pure();
        RenderBoundsContract expandChild = RenderBoundsContract.Create(
            static _ => new Rect(0, 0, 12_000, 1),
            static _ => s_bounds);
        RenderFragmentReference child = ValueReplayMap(
            dependency,
            EffectiveScale.Unbounded,
            "expanding-child",
            expandedBounds,
            expandChild);
        RenderBoundsContract shrinkToParent = RenderBoundsContract.CreateFullInput(
            static _ => new Rect(0, 0, 32, 32));
        RenderFragmentReference parent = ValueReplayMap(
            child,
            EffectiveScale.At(2),
            "shrink-parent",
            parentBounds,
            shrinkToParent);
        using Scenario scenario = Build(
            [dependency, child, parent],
            [parent],
            [(child, "child"), (parent, "parent")],
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["dependency"] = dependency,
                ["child"] = child,
                ["parent"] = parent,
            },
            cacheRules: new RenderCacheRules(MaxPixels: 20_000, MinPixels: 1));

        RenderCachePlanningResult result = ResolvePlanning(scenario);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Resolution.GetDecision(scenario.Candidate("parent")).Kind,
                Is.EqualTo(RenderCacheResolutionKind.MissCapture));
            Assert.That(
                result.Resolution.GetDecision(scenario.Candidate("child")).BypassReason,
                Is.EqualTo(RenderCacheBypassReason.OutsideCacheRules));
            Assert.That(
                result.MaterializationDemands[scenario.Named("dependency")],
                Is.EqualTo(EffectiveScale.At(2)));
        });
    }

    [Test]
    public void MaterializationDemands_TargetCommandLayerUsesConcreteInputSupply()
    {
        RenderFragmentReference denseInput = Pure(scale: EffectiveScale.At(2));
        var layer = new RenderFragmentReference(
            RenderFragmentKind.Layer,
            s_bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            [denseInput],
            payload: null,
            static _ => true);
        var command = new RenderFragmentReference(
            RenderFragmentKind.TargetCommand,
            s_bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.None,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            [layer],
            payload: null,
            static _ => false);

        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
            RenderMaterializationDemandResolver.Resolve(
                [command],
                outputScale: 1,
                maxWorkingScale: float.PositiveInfinity).Demands;

        Assert.Multiple(() =>
        {
            Assert.That(demands[layer], Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(demands[denseInput], Is.EqualTo(EffectiveScale.At(2)));
        });
    }

    /// <remarks>
    /// A raw scope's callback is opaque, so the declared scale contract is the only statement of how the
    /// replayed input is consumed. Forwarding the target demand past a scope that resamples rasterizes an
    /// unbounded child at the target density and then enlarges it.
    /// </remarks>
    [Test]
    public void MaterializationDemands_RawTargetScopeCarriesItsDeclaredDemandBackwards()
    {
        RenderFragmentReference leaf = Pure();
        RenderFragmentReference scope = RawTargetScope(
            leaf,
            RenderScaleContract.MapInputSupply(ReduceSupplyByFour, QuadrupleDemand));

        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
            RenderMaterializationDemandResolver.Resolve(
                [scope],
                outputScale: 1,
                maxWorkingScale: float.PositiveInfinity).Demands;

        Assert.That(demands[leaf], Is.EqualTo(EffectiveScale.At(4)));
    }

    /// <remarks>
    /// The companion to <see cref="MaterializationDemands_RawTargetScopeCarriesItsDeclaredDemandBackwards"/>:
    /// a scope whose enlargement is already carried by the destination matrix declares no backward map, and
    /// pre-scaling its input there would rasterize it large and then draw it scaled again.
    /// </remarks>
    [Test]
    public void MaterializationDemands_RawTargetScopeWithoutABackwardMapLeavesTheDemandAlone()
    {
        RenderFragmentReference leaf = Pure();
        RenderFragmentReference scope = RawTargetScope(
            leaf,
            RenderScaleContract.MapInputSupplyPreservingDemand(ReduceSupplyByFour));

        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
            RenderMaterializationDemandResolver.Resolve(
                [scope],
                outputScale: 1,
                maxWorkingScale: float.PositiveInfinity).Demands;

        Assert.That(demands[leaf], Is.EqualTo(EffectiveScale.At(1)));
    }

    [TestCase(4f)]
    [TestCase(float.PositiveInfinity)]
    public void MaterializationDemands_UpscalingTransformStaysWithinWorkingAndBufferCeilings(
        float maxWorkingScale)
    {
        var bounds = new Rect(0, 0, 8, 6);
        RenderFragmentReference leaf = Pure(bounds: bounds);
        var layer = new RenderFragmentReference(
            RenderFragmentKind.Layer,
            bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            [leaf],
            new LayerRenderFragmentPayload(bounds),
            static _ => true);
        RenderFragmentReference transform = ValueReplayMap(
            layer,
            EffectiveScale.Unbounded,
            "upscale",
            scaleContract: RenderScaleContract.MapInputSupply(
                static supply => supply,
                ScaleDemandByOneMillion));

        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
            RenderMaterializationDemandResolver.Resolve(
                [transform],
                outputScale: 1,
                maxWorkingScale: maxWorkingScale).Demands;

        float expected = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            bounds,
            MathF.Min(1_000_000, maxWorkingScale));
        PixelRect allocated = PixelRect.FromRect(bounds, demands[layer].Value);
        Assert.Multiple(() =>
        {
            Assert.That(demands[layer], Is.EqualTo(EffectiveScale.At(expected)));
            Assert.That(demands[leaf], Is.EqualTo(EffectiveScale.At(expected)));
            Assert.That(allocated.Width, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(allocated.Height, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
        });
    }

    [Test]
    public void ContributeValuesCache_DelegatesDensityAndFootprintToLargeLayerInput()
    {
        var inputBounds = new Rect(0, 0, 64, 1);
        var layerDomain = new Rect(0, 0, 10_000, 1);
        var requestedRegion = new Rect(0, 0, 1, 1);
        const float outputScale = 2;
        float expectedDensity = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            layerDomain,
            outputScale);
        RenderFragmentReference leaf = Pure(bounds: inputBounds);
        var layer = new RenderFragmentReference(
            RenderFragmentKind.Layer,
            inputBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            [leaf],
            new LayerRenderFragmentPayload(layerDomain),
            static _ => false);
        var contributing = new RenderFragmentReference(
            RenderFragmentKind.ContributeValues,
            inputBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            [layer],
            payload: null,
            static _ => true);
        using Scenario scenario = Build(
            [leaf, layer, contributing],
            [contributing],
            [(contributing, "contributing")],
            requestedRegion,
            outputScale,
            cacheRules: new RenderCacheRules(MaxPixels: 1_000, MinPixels: 1));

        RenderCachePlanningResult planning = ResolvePlanning(scenario);
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
            planning.MaterializationDemands;
        RenderCacheDecision decision = planning.Resolution
            .GetDecision(scenario.Candidate(contributing));

        Assert.Multiple(() =>
        {
            Assert.That(demands[contributing], Is.EqualTo(EffectiveScale.At(expectedDensity)));
            Assert.That(demands[layer], Is.EqualTo(EffectiveScale.At(expectedDensity)));
            Assert.That(
                (long)PixelRect.FromRect(layerDomain, expectedDensity).Width,
                Is.GreaterThan(1_000));
            Assert.That(decision.Kind, Is.EqualTo(RenderCacheResolutionKind.Bypass));
            Assert.That(decision.BypassReason, Is.EqualTo(RenderCacheBypassReason.OutsideCacheRules));
        });
    }

    [Test]
    public void FullHashCollision_NeverSubstitutesAnUnequalEntry()
    {
        using Scenario first = SingleCandidate(candidateKey: new CollidingKey("first"));
        RenderCacheResolution cold = Resolve(first);
        RenderCacheEntry wrong = new(cold.MissCaptures.Single().Identity, new object());
        using Scenario second = SingleCandidate(candidateKey: new CollidingKey("second"));

        RenderCacheResolution resolution = Resolve(second, new CollisionLookup(wrong));

        Assert.Multiple(() =>
        {
            Assert.That(wrong.Identity.GetHashCode(),
                Is.EqualTo(resolution.MissCaptures.Single().Identity.GetHashCode()));
            Assert.That(resolution.Hits, Is.Empty);
            Assert.That(resolution.MissCaptures.Length, Is.EqualTo(1));
            Assert.That(resolution.MissCaptures.Single().Identity, Is.Not.EqualTo(wrong.Identity));
        });
    }

    [Test]
    public void FusionMode_IsPartOfRenderOutputCacheIdentity()
    {
        var lookup = new RecordingLookup();
        RenderOutputCacheIdentity enabledIdentity;
        using (Scenario enabled = SingleCandidate(fusionMode: FusionMode.Enabled))
        {
            RenderCacheResolution cold = Resolve(enabled, lookup);
            RenderCacheMissCapture capture = cold.MissCaptures.Single();
            enabledIdentity = capture.Identity;
            lookup.Add(capture);
        }

        using Scenario disabled = SingleCandidate(fusionMode: FusionMode.Disabled);
        RenderCacheResolution resolution = Resolve(disabled, lookup);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Hits, Is.Empty);
            Assert.That(resolution.MissCaptures, Has.Length.EqualTo(1));
            Assert.That(resolution.MissCaptures.Single().Identity, Is.Not.EqualTo(enabledIdentity));
            Assert.That(enabledIdentity.FusionMode, Is.EqualTo(FusionMode.Enabled));
            Assert.That(resolution.MissCaptures.Single().Identity.FusionMode, Is.EqualTo(FusionMode.Disabled));
        });
    }

    [Test]
    public void MissCapture_RetainsProducerValuesAndProvenanceWithoutChangingTokenTopology()
    {
        RenderFragmentReference source = Pure();
        RenderFragmentReference command = Boundary(RenderFragmentKind.TargetCommand, source);
        using Scenario scenario = Build(
            [source, command],
            [command],
            [(source, "source")]);
        TargetDependencyPlan before = TargetDependencyLowerer.Lower([command]);

        RenderCacheResolution resolution = Resolve(scenario);
        TargetDependencyPlan after = TargetDependencyLowerer.Lower([command]);
        RecordedRenderFragment producer = scenario.Graph.Fragments.Single(item => item.Id == source.Id);
        RenderCacheMissCapture capture = resolution.MissCaptures.Single();

        Assert.Multiple(() =>
        {
            Assert.That(capture.ProducerId, Is.EqualTo(producer.Id));
            Assert.That(capture.ValueIds, Is.EqualTo(producer.Values));
            Assert.That(capture.ProvenanceId, Is.EqualTo(producer.ProvenanceId));
            Assert.That(command.Inputs.Single(), Is.SameAs(source));
            Assert.That(after.Steps, Is.EqualTo(before.Steps));
            Assert.That(after.Scopes, Is.EqualTo(before.Scopes));
        });
    }

    [Test]
    public void Resolve_BeforeRegionDiscovery_IsRejected()
    {
        RenderFragmentReference source = Pure();
        using Scenario scenario = Build(
            [source],
            [source],
            [(source, "source")],
            stopAtMetadata: true);

        Assert.That(
            () => new RenderCacheResolver().Resolve(
                scenario.Request,
                scenario.Graph,
                scenario.Regions,
                RenderRequestCompiler.ResolveRoots(scenario.Graph),
                s_context),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Resolve_DefaultContextWithoutCandidates_IsRejected()
    {
        RenderFragmentReference source = Pure();
        using Scenario scenario = Build(
            [source],
            [source],
            []);

        Assert.That(
            () => new RenderCacheResolver().Resolve(
                scenario.Request,
                scenario.Graph,
                scenario.Regions,
                RenderRequestCompiler.ResolveRoots(scenario.Graph),
                default),
            Throws.ArgumentException);
    }

    private static Scenario PrefixAndTail(int frame)
    {
        RenderFragmentReference prefix = Pure(payload: new RuntimeValue(100));
        RenderFragmentReference tail = Pure([prefix], payload: new RuntimeValue(frame));
        return Build(
            [prefix, tail],
            [tail],
            [(prefix, "prefix"), (tail, "tail")],
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["prefix"] = prefix,
                ["tail"] = tail,
            });
    }

    private static Scenario ShaderCandidate(
        ShaderDescription description,
        float outputScale = 1,
        float maxWorkingScale = float.PositiveInfinity,
        EffectiveScale? scale = null)
    {
        RenderFragmentReference source = Pure(scale: scale);
        var shader = new RenderFragmentReference(
            RenderFragmentKind.Shader,
            s_bounds,
            scale ?? EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [source],
            new ShaderRenderFragmentPayload(description),
            static _ => true);
        return Build(
            [source, shader],
            [shader],
            [(shader, "shader")],
            outputScale: outputScale,
            maxWorkingScale: maxWorkingScale);
    }

    private static Scenario ShaderFanOut(
        ShaderDescription sharedDescription,
        bool widenSiblingRequirement)
    {
        RenderFragmentReference source = Pure();
        RenderFragmentReference shared = Shader(source, sharedDescription);
        RenderFragmentReference candidate = Shader(
            shared,
            ShaderDescription.CurrentPixel("half4 apply(half4 color) { return color; }"));
        RenderBoundsContract siblingBounds = widenSiblingRequirement
            ? RenderBoundsContract.Create(
                static input => input,
                static requested => requested.Inflate(new Thickness(8)))
            : RenderBoundsContract.Create(
                static input => input,
                static requested => requested);
        RenderFragmentReference sibling = Shader(
            shared,
            ShaderDescription.WholeSource(
                "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
                siblingBounds));

        return Build(
            [source, shared, candidate, sibling],
            [candidate, sibling],
            [(candidate, "fanout-candidate")],
            requestedRegion: new Rect(16, 16, 16, 16),
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["candidate"] = candidate,
                ["shared"] = shared,
            });
    }

    private static Scenario TransparentShaderFanOut(
        ShaderDescription reusableDescription,
        bool widenSiblingRequirement)
    {
        RenderFragmentReference source = Pure();
        RenderFragmentReference producer = Shader(
            source,
            ShaderDescription.CurrentPixel("half4 apply(half4 color) { return color; }"));
        RenderFragmentReference wrapper = Pure([producer]);
        RenderFragmentReference candidate = Shader(wrapper, reusableDescription);
        RenderBoundsContract siblingBounds = widenSiblingRequirement
            ? RenderBoundsContract.Create(
                static input => input,
                static requested => requested.Inflate(new Thickness(8)))
            : RenderBoundsContract.Create(
                static input => input,
                static requested => requested);
        RenderFragmentReference sibling = Shader(
            producer,
            ShaderDescription.WholeSource(
                "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
                siblingBounds));

        return Build(
            [source, producer, wrapper, candidate, sibling],
            [candidate, sibling],
            [(candidate, "transparent-fanout-candidate")],
            requestedRegion: new Rect(16, 16, 16, 16),
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["candidate"] = candidate,
                ["producer"] = producer,
                ["wrapper"] = wrapper,
            });
    }

    private static Scenario ExternalReusableShaderFanOut(bool widenSiblingRequirement)
    {
        RenderFragmentReference source = Pure();
        RenderFragmentReference producer = Shader(
            source,
            ShaderDescription.CurrentPixel("half4 apply(half4 color) { return color; }"));
        RenderFragmentReference candidate = Shader(
            producer,
            ShaderDescription.CurrentPixel("half4 apply(half4 color) { return color; }"));
        RenderBoundsContract siblingBounds = widenSiblingRequirement
            ? RenderBoundsContract.Create(
                static input => input,
                static requested => requested.Inflate(new Thickness(8)))
            : RenderBoundsContract.Create(
                static input => input,
                static requested => requested);
        ShaderDescription siblingDescription = ShaderDescription.WholeSource(
            "uniform shader src; uniform float amount; "
            + "half4 main(float2 coord) { return src.eval(coord) * amount; }",
            siblingBounds,
            bindings => bindings.Uniform(
                "amount",
                1f,
                static (writer, value, context) => writer.Set(value + context.InputBounds.Width)));
        RenderFragmentReference sibling = Shader(producer, siblingDescription);

        return Build(
            [source, producer, candidate, sibling],
            [candidate, sibling],
            [(candidate, "binder-free-candidate")],
            requestedRegion: new Rect(16, 16, 16, 16),
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["candidate"] = candidate,
                ["producer"] = producer,
            });
    }

    private static RenderFragmentReference Shader(
        RenderFragmentReference input,
        ShaderDescription description)
        => new(
            RenderFragmentKind.Shader,
            description.Bounds.TransformBounds(input.Bounds),
            input.EffectiveScale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [input],
            new ShaderRenderFragmentPayload(description),
            static _ => true);

    private static Scenario GeometryCandidate(GeometryDescription description)
    {
        RenderFragmentReference source = Pure();
        var geometry = new RenderFragmentReference(
            RenderFragmentKind.Geometry,
            s_bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [source],
            new GeometryRenderFragmentPayload(description),
            static _ => true);
        return Build(
            [source, geometry],
            [geometry],
            [(geometry, "geometry")]);
    }

    private static Scenario OpacityMaskCandidate(float outputScale)
    {
        RenderFragmentReference primary = Pure(scale: EffectiveScale.At(0.5f));
        RenderFragmentReference dependency = Pure();
        var opacityMask = new RenderFragmentReference(
            RenderFragmentKind.OpacityMask,
            s_bounds,
            EffectiveScale.At(0.5f),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [primary, dependency],
            payload: null,
            static _ => true);
        return Build(
            [primary, dependency, opacityMask],
            [opacityMask],
            [(opacityMask, "mask")],
            outputScale: outputScale,
            names: new Dictionary<string, RenderFragmentReference>
            {
                ["dependency"] = dependency,
            });
    }

    private static RenderFragmentReference ValueReplayMap(
        RenderFragmentReference input,
        EffectiveScale scale,
        string key,
        Rect? bounds = null,
        RenderBoundsContract? boundsContract = null,
        RenderScaleContract? scaleContract = null)
    {
        TargetScopeDescription description = TargetScopeDescription.CreateValueReplayMap(
            static session => session.Canvas.Use(_ => session.ReplayInput()),
            boundsContract ?? RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            scaleContract ?? RenderScaleContract.PreserveInputSupply,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
            deviceGridMapping: RenderDeviceGridMapping.Preserved);
        return new RenderFragmentReference(
            RenderFragmentKind.TargetScope,
            bounds ?? input.Bounds,
            scale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            [input],
            new TargetScopeRenderFragmentPayload(description),
            static _ => true);
    }

    private static RenderFragmentReference RawTargetScope(
        RenderFragmentReference input,
        RenderScaleContract scale)
    {
        RawTargetScopeDescription description = RawTargetScopeDescription.CreateRequestLocal(
            static session => session.ReplayInput(),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            scale);
        return new RenderFragmentReference(
            RenderFragmentKind.RawTargetScope,
            input.Bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: true,
            [input],
            new RawTargetScopeRenderFragmentPayload(description),
            static _ => true);
    }

    private static EffectiveScale ReduceSupplyByFour(EffectiveScale inputSupply)
        => inputSupply.IsUnbounded ? EffectiveScale.Unbounded : EffectiveScale.At(inputSupply.Value / 4);

    private static EffectiveScale QuadrupleDemand(EffectiveScale outputDemand)
        => EffectiveScale.At(outputDemand.Value * 4);

    private static EffectiveScale ScaleDemandByOneMillion(EffectiveScale outputDemand)
        => EffectiveScale.At(outputDemand.Value * 1_000_000);

    private static RenderRequest NewRequest()
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            cachePolicy: RenderCacheOptions.Enabled,
            targetDomain: s_bounds));

    private static RenderNodeRenderer CreateFrameRenderer(
        RenderNode node,
        IRenderTargetFactory? targetFactory = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = targetFactory,
            });

    private static Scenario SingleCandidate(
        Rect? requestedRegion = null,
        float outputScale = 1,
        RenderRequestPurpose purpose = RenderRequestPurpose.Frame,
        Rect? bounds = null,
        object? candidateKey = null,
        FusionMode fusionMode = FusionMode.Enabled)
    {
        RenderFragmentReference source = Pure(bounds: bounds);
        return Build(
            [source],
            [source],
            [(source, candidateKey ?? "source")],
            requestedRegion,
            outputScale,
            purpose,
            fusionMode: fusionMode);
    }

    private static void AssertMiss(
        Scenario scenario,
        RenderCacheResolutionContext context,
        IRenderCacheLookup lookup)
    {
        using (scenario)
        {
            RenderCacheResolution resolution = Resolve(scenario, lookup, context);
            Assert.That(resolution.Hits, Is.Empty);
            Assert.That(resolution.MissCaptures.Length, Is.EqualTo(1));
        }
    }

    private static RenderCacheResolution Resolve(
        Scenario scenario,
        IRenderCacheLookup? lookup = null,
        RenderCacheResolutionContext? context = null)
        => ResolvePlanning(scenario, lookup, context).Resolution;

    private static RenderCachePlanningResult ResolvePlanning(
        Scenario scenario,
        IRenderCacheLookup? lookup = null,
        RenderCacheResolutionContext? context = null)
        => new RenderCacheResolver().Resolve(
            scenario.Request,
            scenario.Graph,
            scenario.Regions,
            RenderRequestCompiler.ResolveRoots(scenario.Graph),
            context ?? s_context,
            lookup);

    private static Scenario Build(
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlyList<(RenderFragmentReference Reference, object Key)> candidates,
        Rect? requestedRegion = null,
        float outputScale = 1,
        RenderRequestPurpose purpose = RenderRequestPurpose.Frame,
        IReadOnlyDictionary<string, RenderFragmentReference>? names = null,
        bool stopAtMetadata = false,
        float maxWorkingScale = float.PositiveInfinity,
        RenderCacheRules? cacheRules = null,
        FusionMode fusionMode = FusionMode.Enabled)
    {
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            purpose,
            targetDomain: s_bounds,
            requestedRegion: requestedRegion,
            outputScale: outputScale,
            maxWorkingScale: maxWorkingScale,
            cachePolicy: new RenderCacheOptions(
                IsEnabled: true,
                cacheRules ?? RenderCacheRules.Default),
            fusionMode: fusionMode);
        var request = new RenderRequest(options);
        var builder = new RecordedRenderGraphBuilder(request.Id);
        var provenance = new Dictionary<RenderFragmentReference, RenderProvenanceId>(
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference reference in references)
        {
            RenderProvenanceId provenanceId = builder.AddProvenance(reference, "test-node");
            provenance.Add(reference, provenanceId);
            RenderValueId[] inputs = reference.Inputs.SelectMany(static item => item.ValueIds).ToArray();
            reference.ValueIds = reference.ValueCardinality.Maximum == 0
                ? []
                : [builder.AddValue(inputs, provenanceId, reference)];
            reference.Id = builder.AddFragment(reference.ValueIds, provenanceId, reference);
        }

        var candidateIds = new Dictionary<RenderFragmentReference, RenderCacheCandidateId>(
            ReferenceEqualityComparer.Instance);
        foreach ((RenderFragmentReference reference, object key) in candidates)
        {
            candidateIds.Add(
                reference,
                builder.AddCacheCandidate(reference.Id!.Value, key));
        }
        foreach (RenderFragmentReference root in roots)
            builder.PublishRoot(root.Id!.Value);

        RecordedRenderGraph graph = builder.Build();
        request.TransitionTo(RenderRequestState.Recording);
        request.TransitionTo(RenderRequestState.Recorded);
        _ = TargetDependencyLowerer.Lower([.. roots], options.TargetDomain);
        request.TransitionTo(RenderRequestState.TargetDependenciesLowered);
        request.TransitionTo(RenderRequestState.MetadataResolved);
        RegionAnalysis regions = new RegionAnalyzer().Analyze(options, roots);
        if (!stopAtMetadata)
            request.TransitionTo(RenderRequestState.RegionsResolved);

        return new Scenario(request, graph, regions, candidateIds, names);
    }

    private static RenderFragmentReference Pure(
        IReadOnlyList<RenderFragmentReference>? inputs = null,
        object? payload = null,
        Rect? bounds = null,
        EffectiveScale? scale = null)
    {
        inputs ??= [];
        return new RenderFragmentReference(
            RenderFragmentKind.ContributeValues,
            bounds ?? s_bounds,
            scale ?? EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: inputs.Any(static item => item.HasTargetEffects),
            hasOpaqueExternalWork: inputs.Any(static item => item.HasOpaqueExternalWork),
            inputs,
            payload,
            static _ => true);
    }


    private static ShaderRenderFragmentPayload CreateShaderPayload()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        return new ShaderRenderFragmentPayload(description);
    }

    private static GeometryRenderFragmentPayload CreateGeometryPayload()
    {
        GeometryDescription description = GeometryDescription.CreateRequestLocal(
            static _ => { },
            RenderBoundsContract.Identity,
            RenderHitTestContract.OutputBounds);
        return new GeometryRenderFragmentPayload(description);
    }

    private static RenderFragmentReference FixedScaleMap(
        RenderFragmentReference input,
        float authoredScale,
        float resolvedScale)
    {
        var identity = new FixedScaleIdentity(authoredScale);
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
            static _ => { },
            OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            RenderScaleContract.Custom(
                new FixedScaleResolver(authoredScale).Resolve));
        return new RenderFragmentReference(
            RenderFragmentKind.OpaqueMap,
            s_bounds,
            EffectiveScale.At(resolvedScale),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: true,
            [input],
            new OpaqueRenderFragmentPayload(
                OpaqueRenderTopology.Map,
                description,
                [RenderInputReadback.None]),
            static _ => true);
    }

    private static RenderFragmentReference Boundary(
        RenderFragmentKind kind,
        RenderFragmentReference child)
    {
        object payload;
        RenderValueCardinality cardinality;
        bool contributes;
        bool canBeUsed;
        IReadOnlyList<RenderFragmentReference> inputs;
        switch (kind)
        {
            case RenderFragmentKind.TargetCommand:
                payload = new TargetCommandRenderFragmentPayload(
                    TargetCommandDescription.Create(
                        "command",
                        static (_, _) => { },
                        TargetRegion.Region(s_bounds),
                        Rect.Empty,
                        RenderHitTestContract.None),
                    []);
                cardinality = RenderValueCardinality.None;
                contributes = false;
                canBeUsed = false;
                inputs = [child];
                break;
            case RenderFragmentKind.RawTargetScope:
                payload = new RawTargetScopeRenderFragmentPayload(
                    RawTargetScopeDescription.CreateRequestLocal(
                        static _ => { },
                        RenderBoundsContract.Identity,
                        RenderHitTestContract.AnyInput,
                        RenderScaleContract.PreserveInputSupply));
                cardinality = RenderValueCardinality.Single;
                contributes = true;
                canBeUsed = false;
                inputs = [child];
                break;
            case RenderFragmentKind.TargetCapture:
                payload = new TargetCaptureRenderFragmentPayload(
                    TargetCaptureDescription.Create(
                        TargetRegion.Region(s_bounds),
                        s_bounds,
                        RenderHitTestContract.None,
                        TargetCaptureScaleContract.MaterializeAtWorkingScale));
                cardinality = RenderValueCardinality.Single;
                contributes = false;
                canBeUsed = true;
                inputs = [];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new RenderFragmentReference(
            kind,
            kind == RenderFragmentKind.TargetCommand ? Rect.Empty : s_bounds,
            kind == RenderFragmentKind.TargetCommand ? EffectiveScale.Unbounded : EffectiveScale.At(1),
            cardinality,
            contributes,
            canBeUsed,
            hasTargetEffects: true,
            hasOpaqueExternalWork: kind == RenderFragmentKind.RawTargetScope,
            inputs,
            payload,
            static _ => false);
    }

    private sealed class Scenario : IDisposable
    {
        private readonly IReadOnlyDictionary<RenderFragmentReference, RenderCacheCandidateId> _candidateIds;
        private readonly IReadOnlyDictionary<string, RenderFragmentReference>? _names;

        public Scenario(
            RenderRequest request,
            RecordedRenderGraph graph,
            RegionAnalysis regions,
            IReadOnlyDictionary<RenderFragmentReference, RenderCacheCandidateId> candidateIds,
            IReadOnlyDictionary<string, RenderFragmentReference>? names)
        {
            Request = request;
            Graph = graph;
            Regions = regions;
            _candidateIds = candidateIds;
            _names = names;
        }

        public RenderRequest Request { get; }

        public RecordedRenderGraph Graph { get; }

        public RegionAnalysis Regions { get; }

        public RenderCacheCandidateId Candidate(RenderFragmentReference reference)
            => _candidateIds[reference];

        public RenderCacheCandidateId Candidate(string name)
            => Candidate(_names![name]);

        public RenderFragmentReference Named(string name)
            => _names![name];

        public void Dispose() => Request.Dispose();
    }

    private sealed class RecordingLookup : IRenderCacheLookup
    {
        private readonly List<RenderCacheEntry> _entries = [];

        public List<object> RequestedKeys { get; } = [];

        public void Add(RenderCacheMissCapture capture)
            => _entries.Add(new RenderCacheEntry(capture.Identity, new object()));

        public void AddRange(IEnumerable<RenderCacheMissCapture> captures)
        {
            foreach (RenderCacheMissCapture capture in captures)
                Add(capture);
        }

        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? entry)
        {
            RequestedKeys.Add(candidate.CacheKey);
            entry = _entries.FirstOrDefault(item => item.Identity.Equals(identity));
            return entry is not null;
        }
    }

    private sealed class CollisionLookup(RenderCacheEntry entry) : IRenderCacheLookup
    {
        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? result)
        {
            result = entry;
            return true;
        }
    }

    private sealed class FirstIdentityOnlyLookup : IRenderCacheLookup
    {
        private RenderOutputCacheIdentity? _firstIdentity;

        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? result)
        {
            if (_firstIdentity is null)
            {
                _firstIdentity = identity;
                result = new RenderCacheEntry(identity, new object());
                return true;
            }

            if (_firstIdentity.Equals(identity))
            {
                result = new RenderCacheEntry(identity, new object());
                return true;
            }

            result = null;
            return false;
        }
    }

    private sealed class DelayedIdentityHitLookup(IReadOnlyDictionary<object, int> hitThresholds)
        : IRenderCacheLookup
    {
        private readonly Dictionary<object, List<RenderOutputCacheIdentity>> _identities = [];

        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? result)
        {
            if (!_identities.TryGetValue(candidate.CacheKey, out var identities))
            {
                identities = [];
                _identities.Add(candidate.CacheKey, identities);
            }

            int identityIndex = identities.FindIndex(item => item.Equals(identity));
            if (identityIndex < 0)
            {
                identities.Add(identity);
                identityIndex = identities.Count - 1;
            }

            bool hit = identityIndex + 1 >= hitThresholds[candidate.CacheKey];
            result = hit
                ? new RenderCacheEntry(identity, new object())
                : null;
            return hit;
        }
    }

    private sealed record RuntimeValue(int Value);

    private sealed record FixedScaleResolver(float Scale)
    {
        public float Resolve(RenderScaleContext _) => Scale;
    }

    private readonly record struct FixedScaleIdentity(float Scale);

    private static void RenderStableGeometry(GeometrySession session, string state)
    {
    }

    private sealed record CollidingKey(string Value)
    {
        public override int GetHashCode() => 7;
    }

    private sealed class CacheableNode(bool disableCache) : RenderNode
    {
        private static readonly RenderResourceSlot<ExecutionProbe> s_probeSlot = new();
        private readonly ExecutionProbe _probe = new();
        private readonly object _probeKey = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            if (disableCache)
                context.DisableRenderCache();

            RenderResource<ExecutionProbe> probe = context.Borrow(_probe);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                "stable",
                static (session, _) => session.UseResource(
                    s_probeSlot,
                    static probe => probe.Record()),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [s_probeSlot.Bind(probe)]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class SolidCacheNode(bool throwOnExecute = false) : RenderNode
    {
        private static readonly RenderResourceSlot<Brush.Resource> s_fillSlot = new();

        public override void Process(RenderNodeContext context)
        {
            Brush.Resource fill = Brushes.Resource.Red;
            RenderResource<Brush.Resource> fillResource = context.Borrow(fill);
            RenderResource<SolidCacheProbe> probeResource = context.Borrow(_probe);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                throwOnExecute,
                static (session, shouldThrow) =>
                    session.UseResource(s_probeSlot, probe =>
                    {
                        probe.Record();
                        if (shouldThrow)
                            throw new InvalidOperationException("injected execution failure");

                        session.UseResource(s_fillSlot, currentFill =>
                        {
                            using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                            output.Canvas.Use(canvas => canvas.DrawRectangle(s_bounds, currentFill, pen: null));
                            session.Publish(output);
                        });
                    }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [s_fillSlot.Bind(fillResource), s_probeSlot.Bind(probeResource)]);
            context.Publish(context.OpaqueSource(description));
        }

        private static readonly RenderResourceSlot<SolidCacheProbe> s_probeSlot = new();
        private readonly SolidCacheProbe _probe = new();
        private readonly object _probeKey = new();

        public int ExecuteCount => _probe.Count;

        public Action? OnExecute
        {
            get => _probe.OnExecute;
            set => _probe.OnExecute = value;
        }

    }

    private sealed class SolidCacheProbe
    {
        public int Count { get; private set; }

        public Action? OnExecute { get; set; }

        public void Record()
        {
            Count++;
            OnExecute?.Invoke();
        }
    }

    private sealed class TrackingTargetFactory : IRenderTargetFactory
    {
        public List<TrackingRenderTarget> Targets { get; } = [];

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            var result = new TrackingRenderTarget(deviceSize);
            Targets.Add(result);
            return result;
        }
    }

    private sealed class TrackingRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public TrackingRenderTarget(PixelSize size)
            : base(CreateSurface(size), size.Width, size.Height)
        {
        }

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed)
                DisposeCalls++;
            base.Dispose(disposing);
        }

        private static SKSurface CreateSurface(PixelSize size)
            => SKSurface.Create(new SKImageInfo(
                   size.Width,
                   size.Height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   s_colorSpace))
               ?? throw new InvalidOperationException("Could not create a cache-test render target.");
    }
}
