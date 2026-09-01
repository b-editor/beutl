using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[NonParallelizable]
[TestFixture]
public sealed class RecordingIdentityCollisionTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    [Test]
    public void AnInputThatOnlyCollidesWithTheOneARecordingWasMadeOver_ForcesARecord()
    {
        (Rect first, Rect second) = CollidingTargetRegions();
        using var child = new TargetRegionNode(first);
        using var parent = new RenderNodeRecordingCacheTests.CountingContainerNode();
        parent.AddChild(child);

        Record(parent);
        Record(parent);
        long recordedOver = parent.RecordingSnapshot!.InputFingerprints.Single();
        Assert.That(parent.ProcessCalls, Is.EqualTo(1), "an unchanged subtree must still be served");

        child.MarkChanged();
        child.Region = second;
        Record(parent);

        Assert.Multiple(() =>
        {
            Assert.That(
                parent.RecordingSnapshot!.InputFingerprints.Single(),
                Is.EqualTo(recordedOver),
                "the two regions were chosen so that the digest cannot tell them apart");
            Assert.That(
                parent.ProcessCalls,
                Is.EqualTo(2),
                "a digest match is a reject that did not fire, not proof that the structures agree");
        });
    }

    [Test]
    public void TheCrossCheck_AcceptsAParentWhoseInputOnlyCollidesWithTheOneItWasRecordedOver()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");

        (Rect first, Rect second) = CollidingTargetRegions();
        using var child = new TargetRegionNode(first);
        using var parent = new RenderNodeRecordingCacheTests.CountingContainerNode();
        parent.AddChild(child);

        using (RenderRecordingCrossCheck.Enable())
        {
            Record(parent);
            child.MarkChanged();
            child.Region = second;

            Assert.That(() => Record(parent), Throws.Nothing);
        }
    }

    /// <summary>
    /// Two affected regions that differ and pack to one value, so the fingerprints they produce collide.
    /// </summary>
    /// <remarks>
    /// The packing multiplies by 31 once per rectangle word, so a step of one in the X word is cancelled by a
    /// step of 31 cubed the other way in the Height word.
    /// </remarks>
    private static (Rect First, Rect Second) CollidingTargetRegions()
    {
        const int CancellingStep = 31 * 31 * 31;
        int x = BitConverter.SingleToInt32Bits(1f);
        int height = BitConverter.SingleToInt32Bits(1f);
        return (
            new Rect(
                BitConverter.Int32BitsToSingle(x),
                0,
                4,
                BitConverter.Int32BitsToSingle(height + CancellingStep)),
            new Rect(
                BitConverter.Int32BitsToSingle(x + 1),
                0,
                4,
                BitConverter.Int32BitsToSingle(height)));
    }

    /// <summary>Records one target command whose affected region can be swapped.</summary>
    private sealed class TargetRegionNode(Rect region) : RenderNode
    {
        // Deliberately not raising HasChanges: the test that moves it says so explicitly.
#pragma warning disable BESG005
        public Rect Region { get; set; } = region;
#pragma warning restore BESG005

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    0,
                    static (_, _) => { },
                    TargetRegion.Region(Region),
                    s_bounds,
                    RenderHitTestContract.None)));
        }
    }

    private static RecordedRenderGraph Record(RenderNode node)
    {
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node, cacheEnabled: false);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(
            new RenderNodeRecordingCacheTests.RequestSetup().CreateOptions(owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        lifecycle.CompleteSuccessfully(false);
        return graph;
    }
}
