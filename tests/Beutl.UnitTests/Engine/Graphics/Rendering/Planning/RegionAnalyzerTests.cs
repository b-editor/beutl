using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RegionAnalyzerTests
{
    [Test]
    public void Analyze_MapsShiftedRequestBackwardThroughForwardGrowth()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(
            new Rect(10, 10, 100, 100),
            EffectiveScale.At(2));
        RenderBoundsContract grow = RenderBoundsContract.Create(
            static input => input.Inflate(new Thickness(5)),
            static requested => requested.Inflate(new Thickness(5)),
            "region-grow");
        RenderFragmentReference output = graph.Map(source, grow);
        var options = Options(requestedRegion: new Rect(0, 0, 20, 20));

        RegionAnalysis result = new RegionAnalyzer().Analyze(options, [output]);

        Assert.Multiple(() =>
        {
            Assert.That(result.RootOutputExtent, Is.EqualTo(new Rect(5, 5, 110, 110)));
            Assert.That(result.FinalCommitBounds, Is.EqualTo(new Rect(5, 5, 15, 15)));
            Assert.That(result.GetFragmentRequirement(output),
                Is.EqualTo(RequiredRegion.Region(new Rect(5, 5, 15, 15))));
            Assert.That(result.GetFragmentRequirement(source),
                Is.EqualTo(RequiredRegion.Region(new Rect(10, 10, 15, 15))));
            Assert.That(result.GetMetadata(source).EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(result.GetMetadata(output).EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
        });
    }

    [Test]
    public void Analyze_NullRequestSelectsCompleteForwardShrinkWithoutPromotingItToFullFallback()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(
            new Rect(0, 0, 100, 100),
            EffectiveScale.At(3));
        RenderBoundsContract shrink = RenderBoundsContract.Create(
            static input => input.Deflate(new Thickness(10)),
            static requested => requested.Inflate(new Thickness(10)),
            "region-shrink");
        RenderFragmentReference output = graph.Map(source, shrink);

        RegionAnalysis result = new RegionAnalyzer().Analyze(Options(), [output]);

        Assert.Multiple(() =>
        {
            Assert.That(result.RootOutputExtent, Is.EqualTo(new Rect(10, 10, 80, 80)));
            Assert.That(result.FinalCommitBounds, Is.EqualTo(result.RootOutputExtent));
            Assert.That(result.FinalCommitRegion,
                Is.EqualTo(RequiredRegion.Region(new Rect(10, 10, 80, 80))));
            Assert.That(result.GetFragmentRequirement(source),
                Is.EqualTo(RequiredRegion.Region(new Rect(0, 0, 100, 100))));
            Assert.That(result.GetFragmentRequirement(source), Is.Not.EqualTo(RequiredRegion.Full));
            Assert.That(result.GetMetadata(output).EffectiveScale, Is.EqualTo(EffectiveScale.At(3)));
        });
    }

    [Test]
    public void Analyze_ClipsOutsideAndShiftedEmptyCommitBoundsToTheRootOutputExtent()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(new Rect(10, 20, 30, 40));
        var analyzer = new RegionAnalyzer();

        RegionAnalysis outside = analyzer.Analyze(
            Options(requestedRegion: new Rect(100, 200, 7, 9)),
            [source]);
        RegionAnalysis empty = analyzer.Analyze(
            Options(requestedRegion: new Rect(70, 80, 0, 10)),
            [source]);

        Assert.Multiple(() =>
        {
            Assert.That(outside.FinalCommitBounds, Is.EqualTo(Rect.Empty));
            Assert.That(outside.FinalCommitRegion, Is.EqualTo(RequiredRegion.Empty));
            Assert.That(outside.GetFragmentRequirement(source), Is.EqualTo(RequiredRegion.Empty));
            Assert.That(empty.FinalCommitBounds, Is.EqualTo(new Rect(70, 80, 0, 10)));
            Assert.That(empty.FinalCommitRegion, Is.EqualTo(RequiredRegion.Empty));
            Assert.That(empty.GetFragmentRequirement(source), Is.EqualTo(RequiredRegion.Empty));
        });
    }

    [Test]
    public void Analyze_UsesExplicitFullForConservativeFullInputFallback()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(new Rect(0, 0, 100, 80));
        RenderFragmentReference identity = graph.Map(source, RenderBoundsContract.Identity);
        RenderFragmentReference output = graph.Map(identity, RenderBoundsContract.FullInput);

        RegionAnalysis result = new RegionAnalyzer().Analyze(
            Options(requestedRegion: new Rect(30, 20, 10, 10)),
            [output]);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetFragmentRequirement(output),
                Is.EqualTo(RequiredRegion.Region(new Rect(30, 20, 10, 10))));
            Assert.That(result.GetFragmentRequirement(identity), Is.EqualTo(RequiredRegion.Full));
            Assert.That(result.GetFragmentRequirement(source), Is.EqualTo(RequiredRegion.Full));
            Assert.That(result.GetValueRequirement(source.ValueIds.Single()), Is.EqualTo(RequiredRegion.Full));
        });
    }

    [Test]
    public void Analyze_UnionsFanOutRequirementsBeforeVisitingSharedProducer()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(new Rect(0, 0, 100, 20));
        RenderBoundsContract leftBounds = RenderBoundsContract.Create(
            static _ => new Rect(0, 0, 40, 20),
            static requested => requested,
            "fanout-left");
        RenderBoundsContract rightBounds = RenderBoundsContract.Create(
            static _ => new Rect(60, 0, 40, 20),
            static requested => requested,
            "fanout-right");
        RenderFragmentReference left = graph.Map(source, leftBounds);
        RenderFragmentReference right = graph.Map(source, rightBounds);

        RegionAnalysis result = new RegionAnalyzer().Analyze(
            Options(requestedRegion: new Rect(10, 0, 80, 20)),
            [left, right]);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetFragmentRequirement(left),
                Is.EqualTo(RequiredRegion.Region(new Rect(10, 0, 30, 20))));
            Assert.That(result.GetFragmentRequirement(right),
                Is.EqualTo(RequiredRegion.Region(new Rect(60, 0, 30, 20))));
            Assert.That(result.GetFragmentRequirement(source),
                Is.EqualTo(RequiredRegion.Region(new Rect(10, 0, 80, 20))));
        });
    }

    [Test]
    public void Analyze_ExpandsTargetReadApronWithoutChangingDeclaredDensity()
    {
        var graph = new FragmentGraph();
        Rect domain = new(0, 0, 100, 100);
        RenderFragmentReference capture = graph.Capture(domain, EffectiveScale.At(2));
        RenderBoundsContract blur = RenderBoundsContract.Create(
            static input => input.Inflate(new Thickness(10)),
            static requested => requested.Inflate(new Thickness(10)),
            "target-read-apron");
        RenderFragmentReference output = graph.Map(capture, blur, contributes: true);

        RegionAnalysis result = new RegionAnalyzer().Analyze(
            Options(targetDomain: domain, requestedRegion: new Rect(40, 40, 10, 10)),
            [output]);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetFragmentRequirement(capture),
                Is.EqualTo(RequiredRegion.Region(new Rect(30, 30, 30, 30))));
            Assert.That(result.GetTargetAccessRequirement(capture),
                Is.EqualTo(RequiredRegion.Region(new Rect(30, 30, 30, 30))));
            Assert.That(result.GetMetadata(capture).EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(result.GetMetadata(output).EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
        });
    }

    [Test]
    public void Analyze_RejectsInvalidForwardAndBackwardMappings()
    {
        var forwardGraph = new FragmentGraph();
        RenderFragmentReference forwardSource = forwardGraph.Source(new Rect(0, 0, 10, 10));
        RenderBoundsContract invalidForward = RenderBoundsContract.Create(
            static _ => new Rect(float.NaN, 0, 10, 10),
            static requested => requested,
            "invalid-forward");
        RenderFragmentReference invalidForwardOutput = forwardGraph.Map(
            forwardSource,
            invalidForward,
            recordedBounds: new Rect(0, 0, 10, 10));

        var backwardGraph = new FragmentGraph();
        RenderFragmentReference backwardSource = backwardGraph.Source(new Rect(0, 0, 10, 10));
        RenderBoundsContract invalidBackward = RenderBoundsContract.Create(
            static input => input,
            static _ => new Rect(0, 0, -1, 10),
            "invalid-backward");
        RenderFragmentReference invalidBackwardOutput = backwardGraph.Map(backwardSource, invalidBackward);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => new RegionAnalyzer().Analyze(Options(), [invalidForwardOutput]),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => new RegionAnalyzer().Analyze(
                    Options(requestedRegion: new Rect(0, 0, 5, 5)),
                    [invalidBackwardOutput]),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void Analyze_RejectsNonDeterministicConcreteForwardMapping()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(new Rect(0, 0, 10, 10));
        int calls = 0;
        RenderBoundsContract nonDeterministic = RenderBoundsContract.Create(
            input => calls++ == 0 ? input : input.Translate(new Point(0.25f, 0)),
            static requested => requested,
            "non-deterministic-forward");
        RenderFragmentReference output = graph.Map(source, nonDeterministic);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RegionAnalyzer().Analyze(Options(), [output]));

        Assert.Multiple(() =>
        {
            Assert.That(
                failure!.Message,
                Does.Contain("changed between recording and graph-wide metadata resolution"));
            Assert.That(calls, Is.EqualTo(2));
        });
    }

    [Test]
    public void Analyze_KeepsOutputQueryTargetRequestedAndCommitDomainsIndependent()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference value = graph.Source(new Rect(0, 0, 20, 20));
        RenderFragmentReference command = graph.Command(
            TargetRegion.Region(new Rect(50, 50, 10, 10)),
            queryBounds: new Rect(100, 100, 5, 5));
        Rect targetDomain = new(0, 0, 200, 160);
        Rect requested = new(140, 120, 30, 20);

        RegionAnalysis result = new RegionAnalyzer().Analyze(
            Options(targetDomain, requested),
            [value, command]);

        Assert.Multiple(() =>
        {
            Assert.That(result.RootOutputExtent, Is.EqualTo(new Rect(0, 0, 60, 60)));
            Assert.That(result.QueryBounds, Is.EqualTo(new Rect(0, 0, 105, 105)));
            Assert.That(result.Measurement.OutputBounds, Is.EqualTo(result.RootOutputExtent));
            Assert.That(result.Measurement.QueryBounds, Is.EqualTo(result.QueryBounds));
            Assert.That(result.TargetDomain, Is.EqualTo(targetDomain));
            Assert.That(result.RequestedRegion, Is.EqualTo(requested));
            Assert.That(result.FinalCommitBounds, Is.EqualTo(Rect.Empty));
            Assert.That(result.GetFragmentRequirement(value), Is.EqualTo(RequiredRegion.Empty));
            Assert.That(result.GetTargetAccessRequirement(command), Is.EqualTo(RequiredRegion.Empty));
        });
    }

    private static RenderRequestOptions Options(
        Rect? targetDomain = null,
        Rect? requestedRegion = null)
        => new(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain,
            requestedRegion,
            cachePolicy: Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled);

    private sealed class FragmentGraph
    {
        private readonly RenderRequestId _requestId = new(1);
        private long _nextId;

        public RenderFragmentReference Source(
            Rect bounds,
            EffectiveScale? scale = null)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                static _ => { },
                RenderOperationBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: ("region-source", ++_nextId));
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.OpaqueSource,
                bounds,
                scale ?? EffectiveScale.At(1),
                RenderValueCardinality.Single,
                contributesValuesToTarget: true,
                canBeUsedAsValueInput: true,
                hasTargetEffects: false,
                hasOpaqueExternalWork: true,
                inputs: null,
                new OpaqueRenderFragmentPayload(OpaqueRenderTopology.Source, description),
                bounds.Contains));
        }

        public RenderFragmentReference Map(
            RenderFragmentReference input,
            RenderBoundsContract bounds,
            bool? contributes = null,
            Rect? recordedBounds = null)
        {
            Rect outputBounds = recordedBounds ?? bounds.TransformBounds(input.Bounds);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                static _ => { },
                RenderOperationBoundsContract.Map(bounds),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: ("region-map", ++_nextId));
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.OpaqueMap,
                outputBounds,
                input.EffectiveScale,
                RenderValueCardinality.Single,
                contributes ?? input.ContributesValuesToTarget,
                canBeUsedAsValueInput: true,
                hasTargetEffects: input.HasTargetEffects,
                hasOpaqueExternalWork: true,
                [input],
                new OpaqueRenderFragmentPayload(OpaqueRenderTopology.Map, description),
                outputBounds.Contains));
        }

        public RenderFragmentReference Capture(Rect bounds, EffectiveScale scale)
        {
            TargetCaptureDescription description = TargetCaptureDescription.Create(
                TargetRegion.Full,
                bounds,
                RenderHitTestContract.None,
                RenderScaleContract.MaterializeAtWorkingScale);
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.TargetCapture,
                bounds,
                scale,
                RenderValueCardinality.Single,
                contributesValuesToTarget: false,
                canBeUsedAsValueInput: true,
                hasTargetEffects: true,
                hasOpaqueExternalWork: false,
                inputs: null,
                new TargetCaptureRenderFragmentPayload(description),
                hitTest: null));
        }

        public RenderFragmentReference Command(TargetRegion affectedRegion, Rect queryBounds)
        {
            TargetCommandDescription description = TargetCommandDescription.Create(
                static _ => { },
                affectedRegion,
                queryBounds,
                RenderHitTestContract.OutputBounds,
                TargetAccess.ReadWrite,
                structuralKey: ("region-command", ++_nextId));
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.TargetCommand,
                queryBounds,
                EffectiveScale.Unbounded,
                RenderValueCardinality.None,
                contributesValuesToTarget: false,
                canBeUsedAsValueInput: false,
                hasTargetEffects: true,
                hasOpaqueExternalWork: false,
                inputs: null,
                new TargetCommandRenderFragmentPayload(description),
                queryBounds.Contains));
        }

        private RenderFragmentReference Stamp(RenderFragmentReference reference)
        {
            long id = ++_nextId;
            reference.Id = new RenderFragmentId(_requestId, id);
            if (reference.ValueCardinality.Maximum != 0 || reference.ValueCardinality.Minimum != 0)
                reference.ValueIds = [new RenderValueId(_requestId, id)];
            return reference;
        }
    }
}
