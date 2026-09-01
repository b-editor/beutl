using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[NonParallelizable]
[TestFixture]
public sealed class RenderNodeRecordingCacheTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    [Test]
    public void AStaticNode_IsProcessedOnceAndReusedAfter()
    {
        using var node = new CountingSourceNode(s_bounds);

        for (int frame = 0; frame < 5; frame++)
            Record(node);

        Assert.Multiple(() =>
        {
            Assert.That(node.ProcessCalls, Is.EqualTo(1), "a recording that repeats must not be made again");
            Assert.That(
                node.PrepareCalls,
                Is.EqualTo(5),
                "PrepareForRequest is the node's one chance to reconcile children, so it runs on every request");
        });
    }

    [Test]
    public void AStaticSubtree_IsProcessedOnceThroughEveryLevel()
    {
        using var leaf = new CountingSourceNode(s_bounds);
        using var middle = new CountingContainerNode();
        using var root = new CountingContainerNode();
        middle.AddChild(leaf);
        root.AddChild(middle);

        for (int frame = 0; frame < 4; frame++)
            Record(root);

        Assert.Multiple(() =>
        {
            Assert.That(leaf.ProcessCalls, Is.EqualTo(1));

            // A fresh container reports a change for the children it was given, so its first request records
            // and every later one reuses.
            Assert.That(middle.ProcessCalls, Is.EqualTo(1));
            Assert.That(root.ProcessCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void AReusedRecording_ProducesTheSameGraphAsAFreshOne()
    {
        using var reused = new CountingSourceNode(s_bounds);
        using var fresh = new CountingSourceNode(s_bounds);

        Record(reused);
        RecordedRenderGraph reusedGraph = Record(reused);
        RecordedRenderGraph freshGraph = Record(fresh);

        Assert.Multiple(() =>
        {
            Assert.That(reused.ProcessCalls, Is.EqualTo(1));
            Assert.That(reusedGraph.Fragments, Has.Length.EqualTo(freshGraph.Fragments.Length));
            Assert.That(reusedGraph.Values, Has.Length.EqualTo(freshGraph.Values.Length));
            Assert.That(reusedGraph.PublicationRoots, Has.Length.EqualTo(freshGraph.PublicationRoots.Length));
            Assert.That(
                ((RenderFragmentReference)reusedGraph.Fragments[0].Payload!).RecordingFingerprint,
                Is.EqualTo(((RenderFragmentReference)freshGraph.Fragments[0].Payload!).RecordingFingerprint));
            Assert.That(
                ((RenderFragmentReference)reusedGraph.Fragments[0].Payload!).Id,
                Is.EqualTo(reusedGraph.Fragments[0].Id),
                "a replayed fragment has to carry the identity of the graph it was replayed into");
        });
    }

    [Test]
    public void ANodeThatReportsChanges_IsProcessedAgain()
    {
        using var node = new CountingSourceNode(s_bounds);

        Record(node);
        Record(node);
        node.MarkChanged();
        Record(node);
        Record(node);

        Assert.That(node.ProcessCalls, Is.EqualTo(2));
    }

    [Test]
    public void ConsumingARecording_LowersTheMarkTheNodeRaised()
    {
        using var node = new CountingSourceNode(s_bounds);
        node.MarkChanged();

        Record(node);
        int callsAfterTheMarkedRequest = node.ProcessCalls;
        bool markSurvivedTheRequest = node.HasChanges;

        Record(node);

        Assert.Multiple(() =>
        {
            Assert.That(callsAfterTheMarkedRequest, Is.EqualTo(1), "the marked node had to record");
            Assert.That(markSurvivedTheRequest, Is.False, "the request that recorded it consumed the mark");
            Assert.That(
                node.ProcessCalls,
                Is.EqualTo(1),
                "nothing changed after the mark was consumed, so the recording repeats");
        });
    }

    [Test]
    public void AChangedChild_ForcesItsAncestorsToRecordAgain()
    {
        using var leaf = new CountingSourceNode(s_bounds);
        using var root = new CountingContainerNode();
        root.AddChild(leaf);

        Record(root);
        Record(root);
        leaf.MarkChanged();
        leaf.Bounds = s_bounds.Inflate(10);
        Record(root);

        Assert.Multiple(() =>
        {
            Assert.That(leaf.ProcessCalls, Is.EqualTo(2));
            Assert.That(
                root.ProcessCalls,
                Is.EqualTo(2),
                "the container records over its child's fragments, so a changed child changes its recording");
        });
    }

    [TestCase("intent")]
    [TestCase("purpose")]
    [TestCase("targetDomain")]
    [TestCase("requestedRegion")]
    [TestCase("outputScale")]
    [TestCase("maxWorkingScale")]
    [TestCase("cachePolicy")]
    [TestCase("fusionMode")]
    public void AChangedRequestValue_ForcesARecord(string requestValue)
    {
        using var node = new CountingSourceNode(s_bounds);
        var baseline = new RequestSetup();

        Record(node, baseline);
        Record(node, baseline);
        Assert.That(node.ProcessCalls, Is.EqualTo(1), "the unchanged request must reuse");

        Record(node, Vary(baseline, requestValue));

        Assert.That(node.ProcessCalls, Is.EqualTo(2));
    }

    private static RequestSetup Vary(RequestSetup setup, string requestValue)
        => requestValue switch
        {
            "intent" => setup with { Intent = RenderIntent.Delivery },
            "purpose" => setup with { Purpose = RenderRequestPurpose.Bounds },
            "targetDomain" => setup with { TargetDomain = new Rect(0, 0, 32, 32) },
            "requestedRegion" => setup with { RequestedRegion = new Rect(0, 0, 16, 16) },
            "outputScale" => setup with { OutputScale = 2f },
            "maxWorkingScale" => setup with { MaxWorkingScale = 4f },
            "cachePolicy" => setup with { CachePolicy = RenderCacheOptions.Enabled },
            "fusionMode" => setup with { FusionMode = FusionMode.Disabled },
            _ => throw new ArgumentOutOfRangeException(nameof(requestValue), requestValue, null),
        };

    [Test]
    public void ANodeThatBindsAResource_IsRefusedAndRecordsEveryRequest()
    {
        using var raw = new BorrowedThing();
        using var node = new ResourceBindingNode(s_bounds, raw);

        for (int frame = 0; frame < 3; frame++)
            Record(node);

        Assert.That(
            node.ProcessCalls,
            Is.EqualTo(3),
            "a resource registration is released with its request, so a fragment naming one cannot be replayed");
    }

    [Test]
    public void ANodeThatOpensANestedRequest_IsRefusedAndRecordsEveryRequest()
    {
        using var inner = new CountingSourceNode(s_bounds);
        using var node = new NestedRequestNode(inner);

        for (int frame = 0; frame < 3; frame++)
            Record(node);

        Assert.That(node.ProcessCalls, Is.EqualTo(3));
    }

    [Test]
    public void ANodeThatRecordsAnotherNode_IsRefusedAndRecordsEveryRequest()
    {
        using var inner = new CountingSourceNode(s_bounds);
        using var node = new RecordsAnotherNodeNode(inner);

        for (int frame = 0; frame < 3; frame++)
            Record(node);

        Assert.Multiple(() =>
        {
            Assert.That(
                node.ProcessCalls,
                Is.EqualTo(3),
                "the absorbed fragments are another node's recording, whose own change reporting governs them");
            Assert.That(
                inner.ProcessCalls,
                Is.EqualTo(1),
                "an input-free node has nothing its recording could have been rebased over, so it reuses");
        });
    }

    [Test]
    public void ANodeThatDrivesANodeRecordingNothing_IsStillRefused()
    {
        using var silent = new SilentNode();
        using var node = new RecordsAnotherNodeNode(silent);

        for (int frame = 0; frame < 3; frame++)
            Record(node);

        Assert.Multiple(() =>
        {
            Assert.That(node.ProcessCalls, Is.EqualTo(3));
            Assert.That(
                silent.ProcessCalls,
                Is.EqualTo(1),
                "the driven node has no inputs of its own, so its recording is still offered back to it");
        });
    }

    [Test]
    public void AnAncestorOfARefusedNode_IsStillReused()
    {
        using var raw = new BorrowedThing();
        using var leaf = new ResourceBindingNode(s_bounds, raw);
        using var root = new CountingContainerNode();
        root.AddChild(leaf);

        for (int frame = 0; frame < 3; frame++)
            Record(root);

        Assert.Multiple(() =>
        {
            Assert.That(leaf.ProcessCalls, Is.EqualTo(3));
            Assert.That(root.ProcessCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ARefusedNodeThatChangesNothingItRecords_LeavesItsAncestorServed()
    {
        using var raw = new BorrowedThing();
        using var leaf = new ResourceBindingNode(s_bounds, raw);
        using var root = new CountingContainerNode();
        root.AddChild(leaf);

        Record(root);
        Record(root);
        leaf.MarkChanged();
        Record(root);

        Assert.Multiple(() =>
        {
            Assert.That(leaf.ProcessCalls, Is.EqualTo(3), "a refused node records for every request");
            Assert.That(root.ProcessCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ARefusedNodeThatChangesWhatItRecords_ForcesItsAncestorToRecordAgain()
    {
        using var raw = new BorrowedThing();
        using var leaf = new ResourceBindingNode(s_bounds, raw);
        using var root = new CountingContainerNode();
        root.AddChild(leaf);

        Record(root);
        Record(root);
        leaf.MarkChanged();
        leaf.Bounds = s_bounds.Inflate(4);
        Record(root);

        Assert.That(root.ProcessCalls, Is.EqualTo(2));
    }

    [Test]
    public void AReusedRecording_KeepsItsRenderCacheOptOut()
    {
        using var optedOut = new CacheOptOutNode(s_bounds);
        using var control = new CountingSourceNode(s_bounds);
        optedOut.Cache.RecordStableRequests();
        control.Cache.RecordStableRequests();
        var setup = new RequestSetup { CachePolicy = RenderCacheOptions.Enabled };

        Record(optedOut, setup);
        Record(control, setup);
        RecordedRenderGraph reusedOptOut = Record(optedOut, setup);
        RecordedRenderGraph reusedControl = Record(control, setup);

        Assert.Multiple(() =>
        {
            Assert.That(optedOut.ProcessCalls, Is.EqualTo(1), "the opted-out recording is still reused");
            Assert.That(reusedOptOut.CacheCandidates, Is.Empty);
            Assert.That(reusedControl.CacheCandidates, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void ADisposedNodeReleasesItsRetainedRecording()
    {
        var node = new CountingSourceNode(s_bounds);
        Record(node);
        Assert.That(node.RecordingSnapshot, Is.Not.Null);

        node.Dispose();

        Assert.That(node.RecordingSnapshot, Is.Null);
    }

    /// <summary>Records one request the way a frame does, change reporting and all.</summary>
    /// <remarks>
    /// The lifecycle is what clears <see cref="RenderNode.HasChanges"/> after a successful request. Without
    /// it a node that ever reported a change reports it forever and nothing here could ever be reused.
    /// </remarks>
    private static RecordedRenderGraph Record(RenderNode node, RequestSetup? setup = null)
    {
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node, cacheEnabled: false);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest((setup ?? new RequestSetup()).CreateOptions(owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        lifecycle.CompleteSuccessfully(false);
        return graph;
    }

    internal sealed record RequestSetup
    {
        public RenderIntent Intent { get; init; } = RenderIntent.Preview;

        public RenderRequestPurpose Purpose { get; init; } = RenderRequestPurpose.Auxiliary;

        public Rect? TargetDomain { get; init; }

        public Rect? RequestedRegion { get; init; }

        public float OutputScale { get; init; } = 1f;

        public float MaxWorkingScale { get; init; } = 1f;

        public RenderCacheOptions CachePolicy { get; init; } = RenderCacheOptions.Disabled;

        public FusionMode FusionMode { get; init; } = FusionMode.Enabled;

        public RenderRequestOptions CreateOptions(RenderRequestOwner owner)
            => new(
                Intent,
                Purpose,
                TargetDomain,
                RequestedRegion,
                OutputScale,
                MaxWorkingScale,
                CachePolicy,
                FusionMode,
                owner);
    }

    internal static OpaqueRenderDescription CreateSource(Rect bounds)
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

    internal sealed class CountingSourceNode(Rect bounds) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public int PrepareCalls { get; private set; }

        // Deliberately not raising HasChanges: the tests that move it also say so explicitly.
#pragma warning disable BESG005
        public Rect Bounds { get; set; } = bounds;
#pragma warning restore BESG005

        public override void PrepareForRequest(RenderNodePreparation preparation) => PrepareCalls++;

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            context.Publish(context.OpaqueSource(CreateSource(Bounds)));
        }
    }

    internal sealed class CountingContainerNode : ContainerRenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            base.Process(context);
        }
    }

    internal sealed class CacheOptOutNode(Rect bounds) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            context.DisableRenderCache();
            context.Publish(context.OpaqueSource(CreateSource(bounds)));
        }
    }

    internal sealed class ResourceBindingNode(Rect bounds, BorrowedThing raw) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        // Deliberately not raising HasChanges: the test that moves it marks the node itself.
#pragma warning disable BESG005
        public Rect Bounds { get; set; } = bounds;
#pragma warning restore BESG005

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            _ = context.Borrow(raw);
            context.Publish(context.OpaqueSource(CreateSource(Bounds)));
        }
    }

    internal sealed class NestedRequestNode(RenderNode inner) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            _ = context.RecordNestedTarget(inner, new Rect(0, 0, 8, 8));
        }
    }

    internal sealed class RecordsAnotherNodeNode(RenderNode inner) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            context.PublishRange(context.RecordNode(inner, []));
        }
    }

    internal sealed class SilentNode : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context) => ProcessCalls++;
    }

    internal sealed class BorrowedThing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
