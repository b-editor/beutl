using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class ResourcePlanUseScheduleTests
{
    [Test]
    public void SelectedCacheHit_PrunesProducerInputsFromRemainingUseCounts()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference sharedSource = Fragment(RenderFragmentKind.OpaqueSource, []);
        sharedSource.Id = new RenderFragmentId(requestId, 1);
        RenderFragmentReference hitProducer = Fragment(RenderFragmentKind.Opacity, [sharedSource]);
        hitProducer.Id = new RenderFragmentId(requestId, 2);

        ResourcePlanUseSchedule unpruned = ResourcePlan.CreateUseSchedule(
            [hitProducer, sharedSource]);
        ResourcePlanUseSchedule pruned = ResourcePlan.CreateUseSchedule(
            [hitProducer, sharedSource],
            new HashSet<RenderFragmentId> { hitProducer.Id.Value });

        Assert.Multiple(() =>
        {
            Assert.That(
                unpruned.Lifetimes.Single(item => ReferenceEquals(item.Fragment, sharedSource))
                    .ConsumerPositions,
                Has.Length.EqualTo(2));
            Assert.That(
                pruned.Lifetimes.Single(item => ReferenceEquals(item.Fragment, sharedSource))
                    .ConsumerPositions,
                Has.Length.EqualTo(1),
                "The remaining authored root use must not be inflated by an input edge below a selected hit.");
            Assert.That(
                pruned.BeginExecution().GetRemainingUseCount(sharedSource),
                Is.EqualTo(1));
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

        ResourcePlanUseSchedule schedule = ResourcePlan.CreateUseSchedule(
            [hitProducer],
            new HashSet<RenderFragmentId> { hitProducer.Id.Value });

        Assert.Multiple(() =>
        {
            Assert.That(schedule.Lifetimes.Select(static item => item.Fragment),
                Is.EqualTo(new[] { hitProducer }));
            Assert.That(schedule.BeginExecution().GetRemainingUseCount(hitProducer), Is.EqualTo(1));
            Assert.That(
                () => schedule.BeginExecution().GetRemainingUseCount(source),
                Throws.InvalidOperationException);
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
            inputs,
            payload: null,
            hitTest: null);
}
