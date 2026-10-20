using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[NonParallelizable]
[TestFixture]
public sealed class RecordingPerVisitAllocationTests
{
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
