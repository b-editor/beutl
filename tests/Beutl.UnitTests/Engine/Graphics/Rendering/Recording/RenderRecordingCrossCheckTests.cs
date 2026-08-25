using Beutl.Graphics;
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
        using var node = new DriftingSourceNode(s_bounds) { HasChanges = true };

        using (RenderRecordingCrossCheck.Enable())
        {
            Assert.That(() => Record(node), Throws.Nothing);
        }
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
