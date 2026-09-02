using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

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
            static requested => requested.Inflate(new Thickness(5)));
        RenderFragmentReference output = graph.Map(source, grow);
        var options = Options(requestedRegion: new Rect(0, 0, 20, 20));

        RegionAnalysis result = new RegionAnalyzer().Analyze(options, [output]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Measurement.OutputBounds, Is.EqualTo(new Rect(5, 5, 110, 110)));
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
            static requested => requested.Inflate(new Thickness(10)));
        RenderFragmentReference output = graph.Map(source, shrink);

        RegionAnalysis result = new RegionAnalyzer().Analyze(Options(), [output]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Measurement.OutputBounds, Is.EqualTo(new Rect(10, 10, 80, 80)));
            Assert.That(result.FinalCommitBounds, Is.EqualTo(result.Measurement.OutputBounds));
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
            Assert.That(result.ValueRequirements[source.ValueIds.Single()], Is.EqualTo(RequiredRegion.Full));
        });
    }

    [Test]
    public void Analyze_UnionsFanOutRequirementsBeforeVisitingSharedProducer()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(new Rect(0, 0, 100, 20));
        RenderBoundsContract leftBounds = RenderBoundsContract.Create(
            static _ => new Rect(0, 0, 40, 20),
            static requested => requested);
        RenderBoundsContract rightBounds = RenderBoundsContract.Create(
            static _ => new Rect(60, 0, 40, 20),
            static requested => requested);
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
            static requested => requested.Inflate(new Thickness(10)));
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
            static requested => requested);
        RenderFragmentReference invalidForwardOutput = forwardGraph.Map(
            forwardSource,
            invalidForward,
            recordedBounds: new Rect(0, 0, 10, 10));

        var backwardGraph = new FragmentGraph();
        RenderFragmentReference backwardSource = backwardGraph.Source(new Rect(0, 0, 10, 10));
        RenderBoundsContract invalidBackward = RenderBoundsContract.Create(
            static input => input,
            static _ => new Rect(0, 0, -1, 10));
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
            static requested => requested);
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
    public void Analyze_RejectsNonDeterministicSymbolicForwardMapping()
    {
        var graph = new FragmentGraph();
        var placeholder = new Rect(0, 0, 10, 10);
        RenderFragmentReference source = graph.SymbolicSource(placeholder);
        var inset = new DriftingInset();
        RenderBoundsContract nonDeterministic = RenderBoundsContract.Create(
            inset,
            static (state, input) => input.Inflate(state.Next),
            static (_, requested) => requested);
        RenderFragmentReference output = graph.Map(source, nonDeterministic);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RegionAnalyzer().Analyze(Options(new Rect(0, 0, 200, 200)), [output]));

        Assert.Multiple(() =>
        {
            Assert.That(output.RecordedBounds, Is.EqualTo(placeholder),
                "Recording must have mapped the placeholder before the inset moved.");
            Assert.That(
                failure!.Message,
                Does.Contain("A forward bounds mapping changed between recording and graph-wide metadata resolution"));
            // Recording, resolving over the owning domain, and replaying at the recorded point. The last two
            // read the same moved inset, so comparing them to each other would have accepted this mapping.
            Assert.That(inset.Reads, Is.EqualTo(3));
        });
    }

    [Test]
    public void Analyze_RejectsAConcreteForwardMappingWhoseNodeMovedAfterRecording()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.Source(new Rect(0, 0, 10, 10));
        using var node = new ShiftingNode { Offset = 5 };
        RenderFragmentReference output = graph.Map(source, node.CreateBounds());
        node.Offset = 40;

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RegionAnalyzer().Analyze(Options(), [output]));

        Assert.Multiple(() =>
        {
            Assert.That(output.RecordedBounds, Is.EqualTo(new Rect(5, 0, 10, 10)),
                "Recording must have mapped the input before the node moved.");
            Assert.That(
                failure!.Message,
                Does.Contain("A forward bounds mapping changed between recording and graph-wide metadata resolution"));
        });
    }

    [Test]
    public void Analyze_RejectsASymbolicForwardMappingWhoseNodeMovedAfterRecording()
    {
        var graph = new FragmentGraph();
        var placeholder = new Rect(0, 0, 10, 10);
        RenderFragmentReference source = graph.SymbolicSource(placeholder);
        using var node = new ShiftingNode { Offset = 5 };
        RenderFragmentReference output = graph.Map(source, node.CreateBounds());
        node.Offset = 40;

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RegionAnalyzer().Analyze(Options(new Rect(0, 0, 200, 200)), [output]));

        Assert.Multiple(() =>
        {
            Assert.That(output.RecordedBounds, Is.EqualTo(placeholder.Translate(new Vector(5, 0))));
            Assert.That(
                failure!.Message,
                Does.Contain("A forward bounds mapping changed between recording and graph-wide metadata resolution"));
        });
    }

    [Test]
    public void Analyze_RejectsNonDeterministicSymbolicSupplyContract()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.SymbolicSource(new Rect(0, 0, 10, 10));
        var supply = new DriftingSupply();
        RenderFragmentReference output = graph.Map(
            source,
            RenderBoundsContract.Identity,
            scale: RenderScaleContract.MapInputSupplyPreservingDemand(
                supply,
                static (state, input) => state.Map(input)));

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => new RegionAnalyzer().Analyze(Options(new Rect(0, 0, 200, 200)), [output]));

        Assert.Multiple(() =>
        {
            Assert.That(output.RecordedEffectiveScale, Is.EqualTo(EffectiveScale.At(1)));
            Assert.That(
                failure!.Message,
                Does.Contain("A supply-density contract changed between recording and graph-wide metadata resolution"));
            // Recording, resolving over the moved input supply, and replaying at the recorded point.
            Assert.That(supply.Reads, Is.EqualTo(3));
        });
    }

    [Test]
    public void Analyze_AcceptsDeterministicSymbolicMappingsOverAnInputThatMoved()
    {
        var graph = new FragmentGraph();
        RenderFragmentReference source = graph.SymbolicSource(new Rect(0, 0, 10, 10));
        RenderBoundsContract grow = RenderBoundsContract.Create(
            static input => input.Inflate(new Thickness(4)),
            static requested => requested.Inflate(new Thickness(4)));
        RenderFragmentReference output = graph.Map(
            source,
            grow,
            scale: RenderScaleContract.MapInputSupplyPreservingDemand(HalveSupply));
        var domain = new Rect(0, 0, 200, 200);

        RegionAnalysis result = new RegionAnalyzer().Analyze(Options(domain), [output]);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetMetadata(source).Bounds, Is.EqualTo(domain),
                "The symbolic input must resolve away from its recorded placeholder.");
            Assert.That(result.GetMetadata(output).Bounds, Is.EqualTo(domain.Inflate(new Thickness(4))));
            Assert.That(result.GetMetadata(output).EffectiveScale, Is.EqualTo(EffectiveScale.At(0.5f)));
        });
    }

    private static EffectiveScale HalveSupply(EffectiveScale input)
        => input.IsUnbounded ? EffectiveScale.Unbounded : EffectiveScale.At(input.Value / 2);

    /// <summary>Models a static bounds callback whose state changes between recording and resolution.</summary>
    private sealed class ShiftingNode : RenderNode
    {
        public float Offset { get; set; }

        public RenderBoundsContract CreateBounds()
            => RenderBoundsContract.Create(
                input => input.Translate(new Vector(Offset, 0)),
                input => input.Translate(new Vector(-Offset, 0)));

        public override void Process(RenderNodeContext context) => context.PassThrough();
    }

    private sealed class DriftingInset
    {
        private int _reads;

        public int Reads => _reads;

        public Thickness Next => new(_reads++ == 0 ? 0 : 4);
    }

    /// <summary>A supply mapping whose factor moves once, the same way <see cref="DriftingInset"/> does.</summary>
    private sealed class DriftingSupply
    {
        private int _reads;

        public int Reads => _reads;

        public EffectiveScale Map(EffectiveScale input)
            => input.IsUnbounded
                ? EffectiveScale.Unbounded
                : EffectiveScale.At(input.Value / (_reads++ == 0 ? 1 : 2));
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
            Assert.That(result.Measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 60, 60)));
            Assert.That(result.Measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 105, 105)));
            Assert.That(result.TargetDomain, Is.EqualTo(targetDomain));
            Assert.That(result.RequestedRegion, Is.EqualTo(requested));
            Assert.That(result.FinalCommitBounds, Is.EqualTo(Rect.Empty));
            Assert.That(result.GetFragmentRequirement(value), Is.EqualTo(RequiredRegion.Empty));
            Assert.That(result.GetTargetAccessRequirement(command), Is.EqualTo(RequiredRegion.Empty));
        });
    }

    [Test]
    public void ResolveMeasurement_MatchesAFullAnalysisOnBothItsResultAndItsResolvedMetadata()
    {
        RenderRequestOptions options = Options(
            targetDomain: new Rect(0, 0, 200, 160),
            requestedRegion: new Rect(10, 10, 40, 40));

        RegionAnalysis analyzed = new RegionAnalyzer().Analyze(options, [BuildRoot()]);
        RenderFragmentReference measuredRoot = BuildRoot();
        RenderNodeMeasurement measured = new RegionAnalyzer().ResolveMeasurement(options, [measuredRoot]);

        Assert.Multiple(() =>
        {
            Assert.That(measured, Is.EqualTo(analyzed.Measurement));
            Assert.That(measuredRoot.Bounds, Is.EqualTo(analyzed.GetMetadata(measuredRoot).Bounds));
            Assert.That(
                measuredRoot.EffectiveScale,
                Is.EqualTo(analyzed.GetMetadata(measuredRoot).EffectiveScale));
        });

        static RenderFragmentReference BuildRoot()
        {
            var graph = new FragmentGraph();
            RenderFragmentReference source = graph.Source(
                new Rect(10, 10, 100, 100),
                EffectiveScale.At(2));
            RenderBoundsContract grow = RenderBoundsContract.Create(
                static input => input.Inflate(new Thickness(5)),
                static requested => requested.Inflate(new Thickness(5)));
            return graph.Map(source, grow);
        }
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
        private const float DefaultOutputScale = 1;
        private const float DefaultMaxWorkingScale = float.PositiveInfinity;

        private readonly RenderRequestId _requestId = new(1);
        private long _nextId;

        public RenderFragmentReference Source(
            Rect bounds,
            EffectiveScale? scale = null)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                static _ => { },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.OpaqueSource,
                bounds,
                scale ?? EffectiveScale.At(1),
                RenderValueCardinality.Single,
                contributesValuesToTarget: true,
                canBeUsedAsValueInput: true,
                hasTargetEffects: false,
                hasOpaqueExternalWork: true,
                inputs: [],
                new OpaqueRenderFragmentPayload(
                    OpaqueRenderTopology.Source,
                    description,
                    Array.Empty<RenderInputReadback>()),
                RenderFragmentHitTest.Bounds));
        }

        /// <summary>
        /// Builds a source whose extent is stated symbolically, so resolution replaces its recorded
        /// placeholder with the owning target domain and every fragment above it resolves over an input that
        /// moved.
        /// </summary>
        public RenderFragmentReference SymbolicSource(Rect placeholderBounds)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                static _ => { },
                OpaqueRenderBoundsContract.Source(placeholderBounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.OpaqueSource,
                placeholderBounds,
                EffectiveScale.At(1),
                RenderValueCardinality.Single,
                contributesValuesToTarget: true,
                canBeUsedAsValueInput: true,
                hasTargetEffects: false,
                hasOpaqueExternalWork: true,
                inputs: [],
                new OpaqueRenderFragmentPayload(
                    OpaqueRenderTopology.Source,
                    description,
                    Array.Empty<RenderInputReadback>()),
                RenderFragmentHitTest.Bounds,
                RenderFragmentBoundsRequirement.OwningTargetDomain));
        }

        public RenderFragmentReference Map(
            RenderFragmentReference input,
            RenderBoundsContract bounds,
            bool? contributes = null,
            Rect? recordedBounds = null,
            RenderScaleContract? scale = null)
        {
            Rect outputBounds = recordedBounds ?? bounds.TransformBounds(input.Bounds);
            RenderScaleContract scaleContract = scale ?? RenderScaleContract.PreserveInputSupply;
            // Recording resolves the density from the contract over the input's recorded supply, so a graph
            // built here has to state the same answer rather than copy the input's.
            EffectiveScale outputScale = scaleContract.Resolve(
                [input.EffectiveScale],
                outputBounds,
                DefaultOutputScale,
                DefaultMaxWorkingScale);
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                static _ => { },
                OpaqueRenderBoundsContract.Map(bounds),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                scaleContract);
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.OpaqueMap,
                outputBounds,
                outputScale,
                RenderValueCardinality.Single,
                contributes ?? input.ContributesValuesToTarget,
                canBeUsedAsValueInput: true,
                hasTargetEffects: input.HasTargetEffects,
                hasOpaqueExternalWork: true,
                [input],
                new OpaqueRenderFragmentPayload(
                    OpaqueRenderTopology.Map,
                    description,
                    [RenderInputReadback.None]),
                RenderFragmentHitTest.Bounds));
        }

        public RenderFragmentReference Capture(Rect bounds, EffectiveScale scale)
        {
            TargetCaptureDescription description = TargetCaptureDescription.Create(
                TargetRegion.Full,
                bounds,
                RenderHitTestContract.None,
                TargetCaptureScaleContract.MaterializeAtWorkingScale);
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.TargetCapture,
                bounds,
                scale,
                RenderValueCardinality.Single,
                contributesValuesToTarget: false,
                canBeUsedAsValueInput: true,
                hasTargetEffects: true,
                hasOpaqueExternalWork: false,
                inputs: [],
                new TargetCaptureRenderFragmentPayload(description),
                hitTest: RenderFragmentHitTest.None));
        }

        public RenderFragmentReference Command(TargetRegion affectedRegion, Rect queryBounds)
        {
            TargetCommandDescription description = TargetCommandDescription.CreateRequestLocal(
                static _ => { },
                affectedRegion,
                queryBounds,
                RenderHitTestContract.OutputBounds);
            return Stamp(new RenderFragmentReference(
                RenderFragmentKind.TargetCommand,
                queryBounds,
                EffectiveScale.Unbounded,
                RenderValueCardinality.None,
                contributesValuesToTarget: false,
                canBeUsedAsValueInput: false,
                hasTargetEffects: true,
                hasOpaqueExternalWork: false,
                inputs: [],
                new TargetCommandRenderFragmentPayload(description, []),
                RenderFragmentHitTest.Bounds));
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
