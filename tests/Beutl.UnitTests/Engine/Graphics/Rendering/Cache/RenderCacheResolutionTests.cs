using Beutl.Graphics;
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
        warmNode.Cache.ReportRenderCount(RenderNodeCache.Count);
        disabledNode.Cache.ReportRenderCount(RenderNodeCache.Count);

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

    [Test]
    public void FrameCache_ColdMissPublishesAndWarmHitSkipsProducerWithPixelParity()
    {
        using var node = new SolidCacheNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
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
    public void ExecutionFailure_RejectsEveryStagedCaptureWithoutPartialPublication()
    {
        using var root = new ContainerRenderNode();
        var completed = new SolidCacheNode();
        var failing = new SolidCacheNode(throwOnExecute: true);
        completed.Cache.ReportRenderCount(RenderNodeCache.Count);
        failing.Cache.ReportRenderCount(RenderNodeCache.Count);
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
        first.Cache.ReportRenderCount(RenderNodeCache.Count);
        invalidatedOwner.Cache.ReportRenderCount(RenderNodeCache.Count);
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
    public void SuccessfulPublication_TransfersOneTargetAndDisposesItExactlyOnceOnInvalidation()
    {
        using var node = new SolidCacheNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var factory = new TrackingTargetFactory();
        var diagnostics = new RenderPipelineDiagnosticsState();
        var renderer = CreateFrameRenderer(node, factory, diagnostics);

        using (renderer.Rasterize())
        {
        }
        RenderPipelineDiagnosticSnapshot publication = diagnostics.Latest;
        Assert.Multiple(() =>
        {
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(publication[RenderPipelineCounter.RenderCacheCaptures], Is.EqualTo(1));
            Assert.That(publication[RenderPipelineCounter.RejectedRenderCacheCaptures], Is.Zero);
            Assert.That(publication[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(2));
            Assert.That(publication[RenderPipelineCounter.IntermediateDischarges], Is.EqualTo(2));
        });

        renderer.Dispose();
        TrackingRenderTarget[] transferred = factory.Targets
            .Where(static target => !target.IsDisposed)
            .ToArray();

        Assert.That(transferred, Has.Length.EqualTo(1));
        node.Cache.Invalidate();
        Assert.Multiple(() =>
        {
            Assert.That(transferred[0].IsDisposed, Is.True);
            Assert.That(transferred[0].DisposeCalls, Is.EqualTo(1));
            Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.DisposeCalls == 1));
        });
    }

    [Test]
    public void AuxiliaryRequests_MayNotPublishPersistentMisses()
    {
        using var node = new SolidCacheNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                UseRenderCache = true,
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
    public void StaticPrefix_StaysWarmAcrossAnimatedTailIdentityChanges()
    {
        var lookup = new RecordingLookup();
        using (Scenario first = PrefixAndTail(frame: 0))
        {
            RenderCacheResolution cold = Resolve(first, lookup);
            lookup.AddRange(cold.MissCaptures);
        }

        for (int frame = 1; frame <= 5; frame++)
        {
            using Scenario scenario = PrefixAndTail(frame);
            RenderCacheResolution resolution = Resolve(scenario, lookup);

            Assert.Multiple(() =>
            {
                Assert.That(
                    resolution.GetDecision(scenario.Candidate("prefix")).Kind,
                    Is.EqualTo(RenderCacheResolutionKind.Hit),
                    $"static prefix at frame {frame}");
                Assert.That(
                    resolution.GetDecision(scenario.Candidate("tail")).Kind,
                    Is.EqualTo(RenderCacheResolutionKind.MissCapture),
                    $"animated tail at frame {frame}");
            });
            lookup.AddRange(resolution.MissCaptures);
        }
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
                s_context),
            Throws.InvalidOperationException);
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

    private static RenderRequest NewRequest()
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds));

    private static RenderNodeRenderer CreateFrameRenderer(
        RenderNode node,
        IRenderTargetFactory? targetFactory = null,
        IRenderPipelineDiagnosticsState? diagnostics = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = true,
                TargetFactory = targetFactory,
                RenderPurpose = RenderRequestPurpose.Frame,
                Diagnostics = diagnostics,
            });

    private static Scenario SingleCandidate(
        Rect? requestedRegion = null,
        float outputScale = 1,
        RenderRequestPurpose purpose = RenderRequestPurpose.Frame,
        Rect? bounds = null,
        object? candidateKey = null)
    {
        RenderFragmentReference source = Pure(bounds: bounds);
        return Build(
            [source],
            [source],
            [(source, candidateKey ?? "source")],
            requestedRegion,
            outputScale,
            purpose);
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
        => new RenderCacheResolver().Resolve(
            scenario.Request,
            scenario.Graph,
            scenario.Regions,
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
        bool stopAtMetadata = false)
    {
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            purpose,
            targetDomain: s_bounds,
            requestedRegion: requestedRegion,
            outputScale: outputScale);
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
        Rect? bounds = null)
    {
        inputs ??= [];
        return new RenderFragmentReference(
            RenderFragmentKind.ContributeValues,
            bounds ?? s_bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: inputs.Any(static item => item.HasTargetEffects),
            hasOpaqueExternalWork: inputs.Any(static item => item.HasOpaqueExternalWork),
            inputs,
            payload,
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
                        static _ => { },
                        TargetRegion.Region(s_bounds),
                        Rect.Empty,
                        RenderHitTestContract.None,
                        TargetAccess.ReadWrite,
                        runtimeIdentity: new RenderRuntimeIdentity("command")));
                cardinality = RenderValueCardinality.None;
                contributes = false;
                canBeUsed = false;
                inputs = [child];
                break;
            case RenderFragmentKind.RawTargetScope:
                payload = new RawTargetScopeRenderFragmentPayload(
                    RawTargetScopeDescription.Create(
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
                        RenderScaleContract.MaterializeAtWorkingScale));
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

    private sealed record RuntimeValue(int Value);

    private sealed record CollidingKey(string Value)
    {
        public override int GetHashCode() => 7;
    }

    private sealed class CacheableNode(bool disableCache) : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            if (disableCache)
                context.DisableRenderCache();

            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                _ => ExecuteCount++,
                RenderOperationBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(CacheableNode),
                runtimeIdentity: new RenderRuntimeIdentity("stable"));
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class SolidCacheNode(bool throwOnExecute = false) : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public Action? OnExecute { get; set; }

        public override void Process(RenderNodeContext context)
        {
            Brush.Resource fill = Brushes.Resource.Red;
            RenderResource<Brush.Resource> fillResource = context.Borrow(
                fill,
                fill.GetOriginal().Id,
                fill.Version);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    ExecuteCount++;
                    OnExecute?.Invoke();
                    if (throwOnExecute)
                        throw new InvalidOperationException("injected execution failure");

                    session.UseResource(fillResource, currentFill =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                        output.Canvas.Use(canvas => canvas.DrawRectangle(s_bounds, currentFill, pen: null));
                        session.Publish(output);
                    });
                },
                RenderOperationBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(SolidCacheNode),
                runtimeIdentity: new RenderRuntimeIdentity((typeof(SolidCacheNode), throwOnExecute)),
                resources: [fillResource]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class TrackingTargetFactory : IRenderTargetFactory
    {
        public List<TrackingRenderTarget> Targets { get; } = [];

        public RenderTarget Create(PixelSize deviceSize)
        {
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
