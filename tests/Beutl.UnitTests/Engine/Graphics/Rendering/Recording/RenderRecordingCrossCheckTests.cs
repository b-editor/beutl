using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins that the recording cross-check catches a node whose recorded output drifts while it reports no
/// changes, and that it stays out of the way otherwise.
/// </summary>
/// <remarks>
/// This is the safety net a recorded-graph cache needs before it can skip a node's
/// <see cref="RenderNode.Process(RenderNodeContext)"/> call. <see cref="RenderNode.HasChanges"/> is public, so
/// an out-of-tree node that forgets to raise it breaks the cache with no compile error; today the same
/// omission only costs the pixel cache, which is why one can exist unnoticed.
/// </remarks>
[NonParallelizable]
[TestFixture]
public sealed class RenderRecordingCrossCheckTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    [SetUp]
    public void SetUp()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");
    }

    [Test]
    public void ANodeThatDriftsBetweenRecordings_FailsAndNamesItsType()
    {
        using var node = new DriftingSourceNode(s_bounds);

        using (RenderRecordingCrossCheck.Enable())
        {
            var exception = Assert.Throws<RenderRecordingCrossCheckException>(() => Record(node));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.NodeType, Is.EqualTo(typeof(DriftingSourceNode)));
                Assert.That(
                    exception.Message,
                    Does.Contain(typeof(DriftingSourceNode).FullName!),
                    "a failure has to name the node an author has to go and fix");
                Assert.That(exception.Message, Does.Contain("HasChanges"));
                Assert.That(
                    exception.Message,
                    Does.Contain("MarkChanged()"),
                    "the message has to name the call that fixes this: HasChanges is read-only, so an "
                    + "author told to assign it cannot act on the failure");
            });
        }
    }

    [Test]
    public void WithoutTheCrossCheck_TheSameDriftGoesUndetected()
    {
        using var node = new DriftingSourceNode(s_bounds);

        Assert.That(
            () => Record(node),
            Throws.Nothing,
            "recording twice and comparing is the only thing that sees this, which is why it exists");
    }

    [Test]
    public void ANodeThatRecordsTheSameGraphTwice_IsAccepted()
    {
        using var node = new StableSourceNode(s_bounds);

        using (RenderRecordingCrossCheck.Enable())
        {
            RecordedRenderGraph graph = Record(node);

            Assert.That(graph.PublicationRoots, Has.Length.EqualTo(1));
        }
    }

    /// <remarks>
    /// The contract is only about a node that reports no changes; a node that does report them is re-recorded
    /// on any skip path, so its output is allowed to differ.
    /// </remarks>
    [Test]
    public void ANodeThatReportsChanges_IsNotHeldToTheContract()
    {
        using var node = new DriftingSourceNode(s_bounds);
        node.MarkChanged();

        using (RenderRecordingCrossCheck.Enable())
        {
            Assert.That(() => Record(node), Throws.Nothing);
        }
    }

    /// <remarks>
    /// The retained recording is the artifact a skip path hands back, so it is the baseline once one exists.
    /// This node records the same graph twice within a request - the probe sees nothing - and a different one
    /// for the next request, which only the retained shape can catch.
    /// </remarks>
    [Test]
    public void ANodeThatDriftsBetweenRequests_FailsAgainstTheRetainedRecording()
    {
        using var node = new PerRequestDriftingSourceNode(s_bounds);

        using (RenderRecordingCrossCheck.Enable())
        {
            Assert.That(() => Record(node), Throws.Nothing, "the first request has nothing to compare against");

            var exception = Assert.Throws<RenderRecordingCrossCheckException>(() => Record(node));

            Assert.That(exception!.NodeType, Is.EqualTo(typeof(PerRequestDriftingSourceNode)));
        }
    }

    /// <remarks>
    /// Recorded metadata is identical here; only the shader source moves. Comparing the payload's structural
    /// identity - the same one the plan cache rebinds a compiled plan on - is what sees it.
    /// </remarks>
    [Test]
    public void ADriftInsideThePayload_IsCaughtByTheStructuralIdentity()
    {
        using var node = new PerRequestShaderSourceDriftNode();

        using (RenderRecordingCrossCheck.Enable())
        {
            Assert.That(() => Record(node), Throws.Nothing);

            var exception = Assert.Throws<RenderRecordingCrossCheckException>(() => Record(node));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.NodeType, Is.EqualTo(typeof(PerRequestShaderSourceDriftNode)));
                Assert.That(exception.Message, Does.Contain("structural identity"));
            });
        }
    }

    /// <summary>
    /// The drift that slips between BESG005 and the payload's structural identity: a per-call value.
    /// </summary>
    /// <remarks>
    /// BESG005 excludes assignments inside <see cref="RenderNode.Process(RenderNodeContext)"/> because
    /// memoizing while recording is legitimate and reads the same, so this compiles clean. Structural
    /// identity excludes a target command's per-call values because they are what one compiled plan is
    /// rebound over, so the shape comparison had nothing to say either, and a skip path replayed the first
    /// region for the rest of the node's life.
    /// </remarks>
    [Test]
    public void ANodeThatAdvancesAPerCallTargetRegionInsideProcess_IsReported()
    {
        using var node = new DriftingTargetRegionNode();

        using (RenderRecordingCrossCheck.Enable())
        {
            var exception = Assert.Throws<RenderRecordingCrossCheckException>(() => Record(node));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.NodeType, Is.EqualTo(typeof(DriftingTargetRegionNode)));
                Assert.That(exception.Message, Does.Contain("per-call"));
            });
        }
    }

    /// <summary>The same for the value the finding names first: an opacity advanced while recording.</summary>
    [Test]
    public void ANodeThatAdvancesAPerCallOpacityInsideProcess_IsReported()
    {
        using var node = new DriftingOpacityNode(s_bounds);

        using (RenderRecordingCrossCheck.Enable())
        {
            var exception = Assert.Throws<RenderRecordingCrossCheckException>(() => Record(node));

            Assert.That(exception!.NodeType, Is.EqualTo(typeof(DriftingOpacityNode)));
        }
    }

    /// <summary>
    /// The sharing this must not collapse: a value computed once while recording and read from the memo after.
    /// </summary>
    /// <remarks>
    /// This is why the assignment cannot simply be reported where it is written. Memoizing is an assignment
    /// inside <see cref="RenderNode.Process(RenderNodeContext)"/> to state that same method reads, exactly
    /// like the drift above; what separates them is whether a second recording produces the same value.
    /// </remarks>
    [Test]
    public void ANodeThatMemoizesAPerCallValueInsideProcess_IsAccepted()
    {
        using var node = new MemoizingOpacityNode(s_bounds);

        using (RenderRecordingCrossCheck.Enable())
        {
            Assert.That(() => Record(node), Throws.Nothing);
            Assert.That(() => Record(node), Throws.Nothing, "and again against the retained recording");
        }
    }

    /// <remarks>
    /// There has to be a fresh recording for the retained one to be checked against, so while this is on a
    /// node that would have been skipped records anyway.
    /// </remarks>
    [Test]
    public void WhileEnabled_ARepeatingNodeRecordsAgainInsteadOfBeingSkipped()
    {
        using var node = new CountingStableSourceNode(s_bounds);

        using (RenderRecordingCrossCheck.Enable())
        {
            Record(node);
            Assert.That(node.ProcessCalls, Is.EqualTo(2), "the first request has to probe for its baseline");
            Record(node);
            Record(node);
        }

        Assert.That(
            node.ProcessCalls,
            Is.EqualTo(4),
            "a request with a retained baseline records once, not twice");

        Record(node);

        Assert.That(node.ProcessCalls, Is.EqualTo(4), "with the check off the recording is reused again");
    }

    /// <remarks>
    /// The probe recording runs the node's real Process, so it registers the node's real resources. Leaving
    /// them in the request would let a debug harness change what the frame owns.
    /// </remarks>
    [Test]
    public void TheProbeRecording_ReleasesTheResourcesItRegistered()
    {
        using var node = new OwningSourceNode(s_bounds);

        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        using (RenderRecordingCrossCheck.Enable())
        {
            _ = new RenderRequestRecorder(request).Record(node);
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.CreatedResources, Has.Count.EqualTo(2), "the node is recorded twice");
            Assert.That(
                node.CreatedResources[0].DisposeCount,
                Is.EqualTo(1),
                "the probe's resource belongs to a recording the graph never received");
            Assert.That(
                node.CreatedResources[1].DisposeCount,
                Is.Zero,
                "the surviving recording still owns its resource");
        });

        owner.Cleanup();
        Assert.That(node.CreatedResources[1].DisposeCount, Is.EqualTo(1));
    }

    private static RecordedRenderGraph Record(RenderNode node)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        return new RenderRequestRecorder(request).Record(node);
    }

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            maxWorkingScale: 1f,
            owner: owner));

    private static OpaqueRenderDescription CreateSource(Rect bounds)
        => OpaqueRenderDescription.CreateEngineSource(
            state: bounds,
            execute: static (session, state) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(state);
                output.Canvas.Use(static canvas => canvas.Clear());
                session.Publish(output);
            },
            directReplay: static (session, _) => session.Canvas.Clear(),
            bounds: OpaqueRenderBoundsContract.Source(bounds),
            hitTest: RenderHitTestContract.OutputBounds,
            scale: RenderScaleContract.Vector,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive);

    private sealed class StableSourceNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(CreateSource(bounds)));
    }

    /// <summary>A node whose recorded bounds grow every time it records, and which never says so.</summary>
    private sealed class DriftingSourceNode(Rect bounds) : RenderNode
    {
        private Rect _bounds = bounds;

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(CreateSource(_bounds)));
            _bounds = _bounds.Inflate(1);
        }
    }

    private sealed class CountingStableSourceNode(Rect bounds) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            context.Publish(context.OpaqueSource(CreateSource(bounds)));
        }
    }

    // BESG005 is right about both of these fixtures and that is what they are for: each one changes state
    // its Process reads without reporting it, so that the dynamic check can be shown to catch what the
    // static rule would have stopped an author from writing.
#pragma warning disable BESG005

    /// <summary>A node that records one graph per request and a different one for the next, silently.</summary>
    private sealed class PerRequestDriftingSourceNode(Rect bounds) : RenderNode
    {
        private Rect _bounds = bounds;

        public override void PrepareForRequest(RenderNodePreparation preparation)
            => _bounds = _bounds.Inflate(1);

        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(CreateSource(_bounds)));
    }

    /// <summary>A node whose recorded metadata never moves and whose shader source does, silently.</summary>
    private sealed class PerRequestShaderSourceDriftNode : RenderNode
    {
        private const string FirstSource =
            "uniform float gain; half4 apply(half4 color) { return color * gain; }";

        private const string SecondSource =
            "uniform float gain; half4 apply(half4 color) { return half4(color.rgb * gain, color.a); }";

        private bool _useSecondSource;

        public override void PrepareForRequest(RenderNodePreparation preparation)
            => _useSecondSource = !_useSecondSource;

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.OpaqueSource(OpaqueRenderDescription.CreateEngineSource(
                state: s_bounds,
                execute: static (session, state) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(state);
                    session.Publish(output);
                },
                directReplay: static (session, _) => session.Canvas.Clear(),
                bounds: OpaqueRenderBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.MaterializeAtWorkingScale,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive));
            ShaderDescription shader = ShaderDescription.CurrentPixel(
                _useSecondSource ? SecondSource : FirstSource,
                static bindings => bindings.Uniform("gain", 0.5f));
            context.Publish(context.Shader(input, shader));
        }
    }

#pragma warning restore BESG005

    /// <summary>A node whose recorded target region grows every time it records, and which never says so.</summary>
    private sealed class DriftingTargetRegionNode : RenderNode
    {
        private float _width = 10;

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    0,
                    static (_, _) => { },
                    TargetRegion.Region(new Rect(0, 0, _width, 10)),
                    s_bounds,
                    RenderHitTestContract.None)));
            _width += 1;
        }
    }

    /// <summary>A node whose recorded opacity moves every time it records, and which never says so.</summary>
    private sealed class DriftingOpacityNode(Rect bounds) : RenderNode
    {
        private float _opacity = 0.5f;

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.OpaqueSource(CreateSource(bounds));
            context.Publish(context.Opacity(input, _opacity));
            _opacity -= 0.125f;
        }
    }

    /// <summary>A node that settles its opacity on the first recording and reads the memo after.</summary>
    private sealed class MemoizingOpacityNode(Rect bounds) : RenderNode
    {
        private float? _opacity;

        public override void Process(RenderNodeContext context)
        {
            _opacity ??= 0.5f;
            RenderFragmentHandle input = context.OpaqueSource(CreateSource(bounds));
            context.Publish(context.Opacity(input, _opacity.Value));
        }
    }

    private sealed class OwningSourceNode(Rect bounds) : RenderNode
    {
        public List<TrackedResource> CreatedResources { get; } = [];

        public override void Process(RenderNodeContext context)
        {
            var resource = new TrackedResource();
            CreatedResources.Add(resource);
            _ = context.Own(resource);
            context.Publish(context.OpaqueSource(CreateSource(bounds)));
        }
    }

    internal sealed class TrackedResource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
