using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Budgets what one request costs when every node in it repeats the recording it already made.
/// </summary>
/// <remarks>
/// This is the cost the recording cache cannot remove: a node it skips still gets a transaction, a set of
/// handles, and a commit, and each of its fragments is recreated for the new request. The scene-level
/// ceilings in <c>RenderDescriptionAllocationTests</c> move with the whole pipeline; this one isolates the
/// per-visit machinery so a regression in it cannot hide inside a scene total.
/// </remarks>
[NonParallelizable]
[TestFixture]
public sealed class RecordingPerVisitAllocationTests
{
    private const int Frames = 200;
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    // Measured at 8,424 bytes for a three-node subtree of four fragments, against 9,120 before the per-visit
    // buffers were right-sized and the replay scratch pooled, and 8,496 before a node's commit became a value
    // and its fragments stopped building a hit-test delegate per recording. The figure is deterministic on
    // one machine, so the margin covers a platform that sizes its collections differently rather than
    // measurement noise.
    private const long ReplayedSubtreeBytesPerRecordCeiling = 8_700;

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

    private static RecordedRenderGraph Record(RenderNode node)
    {
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node, cacheEnabled: false);
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
