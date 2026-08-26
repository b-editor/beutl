using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins that the recording gate settles a fingerprint match by comparison rather than trusting the digest.
/// </summary>
/// <remarks>
/// FR-033 requires identity comparison to remain correct under hash collisions.
/// <c>RenderFragmentReference.RecordingFingerprint</c> is 64 bits standing for a whole input cone, and two of
/// the members it folds in - a target region's rectangle and a bounds contract's identity - it can only hash,
/// so a collision is constructible rather than merely improbable.
/// </remarks>
[NonParallelizable]
[TestFixture]
public sealed class RecordingIdentityCollisionTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    /// <summary>
    /// The digest the gate rejects on can collide, so agreeing on it cannot be the whole answer.
    /// </summary>
    /// <remarks>
    /// A target command's affected region reaches the fingerprint through a multiply-accumulate over the four
    /// rectangle words, which is a hash and has neighbours that collide by construction. Everything else about
    /// these two fragments is identical, so a recording made over one digests exactly as a recording made over
    /// the other while the two name different regions. FR-033 is what says the comparison has to survive that.
    /// </remarks>
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

        child.HasChanges = true;
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

    /// <summary>The cross-check cannot stand in for this: it never looks at the inputs.</summary>
    /// <remarks>
    /// <see cref="RecordedNodeShape"/> describes the fragments one node recorded and the labels its inputs
    /// carry, not the structure of those inputs, so a parent recorded over either region describes the same
    /// recording. It accepts the collision whether or not the gate catches it, which is why its acceptance is
    /// no evidence about the gate.
    /// </remarks>
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
            child.HasChanges = true;
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
        public Rect Region { get; set; } = region;

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
