using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using static Beutl.UnitTests.Engine.Graphics.Rendering.Recording.RenderNodeRecordingCacheTests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[NonParallelizable]
[TestFixture]
public sealed class RecordingPerVisitAllocationTests
{
    private const int Frames = 200;
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    // Measured at 5,192 bytes on one machine; the margin covers platforms that size collections differently.
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
}
