using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class ResourcePlanUseTrackerTests
{
    [Test]
    public void SelectedCacheHit_PrunesProducerInputsFromRemainingUseCounts()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference sharedSource = Fragment(RenderFragmentKind.OpaqueSource, []);
        sharedSource.Id = new RenderFragmentId(requestId, 1);
        RenderFragmentReference hitProducer = Fragment(RenderFragmentKind.Opacity, [sharedSource]);
        hitProducer.Id = new RenderFragmentId(requestId, 2);

        ResourcePlanUseTracker unpruned = ResourcePlanUseTracker.Create(
            [hitProducer, sharedSource]);
        ResourcePlanUseTracker pruned = ResourcePlanUseTracker.Create(
            [hitProducer, sharedSource],
            new HashSet<RenderFragmentId> { hitProducer.Id.Value });

        Assert.Multiple(() =>
        {
            Assert.That(unpruned.GetRemainingUseCount(sharedSource), Is.EqualTo(2));
            Assert.That(
                pruned.GetRemainingUseCount(sharedSource),
                Is.EqualTo(1),
                "The remaining authored root use must not be inflated by an input edge below a selected hit.");
        });
    }

    [Test]
    public void SelectedCacheHit_PrunesExclusiveProducerSubtree()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference source = Fragment(RenderFragmentKind.OpaqueSource, []);
        source.Id = new RenderFragmentId(requestId, 1);
        RenderFragmentReference hitProducer = Fragment(RenderFragmentKind.Opacity, [source]);
        hitProducer.Id = new RenderFragmentId(requestId, 2);

        ResourcePlanUseTracker tracker = ResourcePlanUseTracker.Create(
            [hitProducer],
            new HashSet<RenderFragmentId> { hitProducer.Id.Value });

        Assert.Multiple(() =>
        {
            Assert.That(tracker.GetRemainingUseCount(hitProducer), Is.EqualTo(1));
            Assert.That(
                () => tracker.GetRemainingUseCount(source),
                Throws.InvalidOperationException);
            Assert.That(
                () => tracker.CompleteUse(source),
                Throws.InvalidOperationException);
        });
    }

    [Test]
    public void FanOut_CompletesOnlyAtLastDeclaredUseAndRejectsOverConsumption()
    {
        RenderFragmentReference source = Fragment(RenderFragmentKind.OpaqueSource, []);
        RenderFragmentReference left = Fragment(RenderFragmentKind.Opacity, [source]);
        RenderFragmentReference right = Fragment(RenderFragmentKind.Opacity, [source]);
        RenderFragmentReference root = Fragment(RenderFragmentKind.OpaqueCombine, [left, right]);
        ResourcePlanUseTracker tracker = ResourcePlanUseTracker.Create([root]);

        Assert.Multiple(() =>
        {
            Assert.That(tracker.GetRemainingUseCount(source), Is.EqualTo(2));
            Assert.That(tracker.CompleteUse(source), Is.False);
            Assert.That(tracker.CompleteUse(source), Is.True);
            Assert.That(() => tracker.CompleteUse(source), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void DuplicateRoots_CountAsSeparateAuthoredUses()
    {
        RenderFragmentReference root = Fragment(RenderFragmentKind.OpaqueSource, []);
        ResourcePlanUseTracker tracker = ResourcePlanUseTracker.Create([root, root]);

        Assert.Multiple(() =>
        {
            Assert.That(tracker.GetRemainingUseCount(root), Is.EqualTo(2));
            Assert.That(tracker.CompleteUse(root), Is.False);
            Assert.That(tracker.CompleteUse(root), Is.True);
        });
    }

    private static RenderFragmentReference Fragment(
        RenderFragmentKind kind,
        IReadOnlyList<RenderFragmentReference> inputs)
        => new(
            kind,
            new Rect(0, 0, 16, 16),
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [.. inputs],
            payload: null,
            hitTest: RenderFragmentHitTest.None);
}
