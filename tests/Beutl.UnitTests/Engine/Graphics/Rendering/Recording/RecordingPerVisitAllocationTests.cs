using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[NonParallelizable]
[TestFixture]
public sealed class RecordingPerVisitAllocationTests
{
    private const int Frames = 200;
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    // Measured at 5,192 bytes for a three-node subtree of four fragments, against 5,632 before request-owner
    // rare state and the recording-family scope stopped allocating on every visit. The figure is deterministic
    // on one machine, so the margin covers a platform that sizes its collections differently rather than noise.
    private const long ReplayedSubtreeBytesPerRecordCeiling = 5_700;

    [Test]
    public void ARepeatedlyReplayedSubtree_StaysWithinItsPerVisitBudget()
    {
        using var leaf = new ChainedSourceNode(s_bounds);
        using var middle = new PassThroughContainerNode();
        using var root = new PassThroughContainerNode();
        middle.AddChild(leaf);
        root.AddChild(middle);

        for (int frame = 0; frame < Frames; frame++)
            Record(root);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < Frames; frame++)
            Record(root);
        long after = GC.GetAllocatedBytesForCurrentThread();

        long perRecord = (after - before) / Frames;
        TestContext.Out.WriteLine($"three-node replayed subtree: {perRecord} bytes/record");

        Assert.That(
            perRecord,
            Is.LessThan(ReplayedSubtreeBytesPerRecordCeiling),
            "the per-visit recording machinery must not grow back");
    }

    [Test]
    public void RecordingFamilyScope_ReleasesOnceWithoutAllocating()
    {
        var family = new RenderRecordingFamily();
        using var node = new MemoryNode<int>(0);
        for (int index = 0; index < 200; index++)
        {
            using RenderRecordingFamily.Scope scope = family.Enter(node);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 20_000; index++)
        {
            using RenderRecordingFamily.Scope scope = family.Enter(node);
        }
        long bytesPerScope = (GC.GetAllocatedBytesForCurrentThread() - before) / 20_000;

        RenderRecordingFamily.Scope reusable = family.Enter(node);
        reusable.Dispose();
        reusable.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(bytesPerScope, Is.Zero);
            Assert.That(() => family.Enter(node).Dispose(), Throws.Nothing);
        });
    }

    private static RecordedRenderGraph Record(RenderNode node)
    {
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            null,
            null,
            1f,
            1f,
            RenderCacheOptions.Disabled,
            FusionMode.Enabled,
            owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        lifecycle.CompleteSuccessfully(false);
        return graph;
    }

    private sealed class ChainedSourceNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(
                RenderNodeRecordingCacheTests.CreateSource(bounds));
            RenderFragmentHandle opacity = context.Opacity(source, 0.5f);
            context.Publish(context.Opacity(opacity, 0.25f));
        }
    }

    private sealed class PassThroughContainerNode : ContainerRenderNode;
}
