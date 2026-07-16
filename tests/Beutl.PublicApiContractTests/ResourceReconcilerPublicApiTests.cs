using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ResourceReconcilerPublicApiTests
{
    [Test]
    public void PropertyReconciliation_ReplacesSameLengthArrayInPlace()
    {
        var previousOwner = new SoundGroup();
        var replacementOwner = new SoundGroup();
        var propertyOwner = new SoundGroup();
        propertyOwner.Children.Add(replacementOwner);
        Sound.Resource previous = previousOwner.ToResource(CompositionContext.Default);
        IList<Sound.Resource> field = new Sound.Resource[] { previous };
        bool changed = false;

        ResourceReconciler.ReconcileListFromProperty(
            CompositionContext.Default,
            propertyOwner.Children,
            0,
            field,
            ref changed);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(previous.IsDisposed, Is.True);
            Assert.That(field, Has.Count.EqualTo(1));
            Assert.That(field[0], Is.Not.SameAs(previous));
            Assert.That(field[0].GetOriginal(), Is.SameAs(replacementOwner));
        });

        field[0].Dispose();
    }

    [Test]
    public void FlowReconciliation_UpdatesSameLengthVersionArrayInPlace()
    {
        var owner = new SoundGroup();
        Sound.Resource resource = owner.ToResource(CompositionContext.Default);
        var consumed = new List<Sound.Resource> { resource };
        var field = new List<Sound.Resource> { resource };
        IList<int> versions = new[] { -1 };
        bool changed = false;

        ResourceReconciler.ReconcileListFromFlow(
            CompositionContext.Default,
            owner.Children,
            consumed,
            field,
            versions,
            flowRollbackSnapshot: null,
            ref changed);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(field, Is.EqualTo(consumed));
            Assert.That(versions, Is.EqualTo(new[] { resource.Version }));
            Assert.That(resource.IsDisposed, Is.False);
        });

        resource.Dispose();
    }

    [Test]
    public void FlowTransaction_ReconcilesThreeListsWithOneTransferSet()
    {
        var owner = new SoundGroup();
        Sound.Resource transferred = (Sound.Resource)owner.ToResource(CompositionContext.Default);
        var firstOwner = new SoundGroup();
        var secondOwner = new SoundGroup();
        var thirdOwner = new SoundGroup();
        var firstConsumed = new List<Sound.Resource> { transferred };
        var secondConsumed = new List<Sound.Resource>();
        var thirdConsumed = new List<Sound.Resource>();
        var firstField = new List<Sound.Resource>();
        var secondField = new List<Sound.Resource>();
        var thirdField = new List<Sound.Resource> { transferred };
        var firstVersions = new List<int>();
        var secondVersions = new List<int>();
        var thirdVersions = new List<int>();
        bool changed = false;

        ResourceReconciler.FlowReconciliationTransaction transaction
            = ResourceReconciler.BeginFlowTransaction(CompositionContext.Default, flowRollbackSnapshot: null);
        transaction.Add(firstOwner.Children, firstConsumed, firstField, firstVersions);
        transaction.Add(secondOwner.Children, secondConsumed, secondField, secondVersions);
        transaction.Add(thirdOwner.Children, thirdConsumed, thirdField, thirdVersions);
        transaction.Commit(ref changed);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(firstField, Is.EqualTo(firstConsumed));
            Assert.That(secondField, Is.Empty);
            Assert.That(thirdField, Is.Empty);
            Assert.That(firstVersions, Is.EqualTo(new[] { transferred.Version }));
            Assert.That(secondVersions, Is.Empty);
            Assert.That(thirdVersions, Is.Empty);
            Assert.That(transferred.IsDisposed, Is.False);
        });

        transferred.Dispose();
    }

    [Test]
    public void FlowTransaction_RejectsDuplicateRegistrationAndReuseBeforeAcquisition()
    {
        var owner = new SoundGroup();
        var consumed = new List<Sound.Resource>();
        var field = new List<Sound.Resource>();
        var versions = new List<int>();
        bool changed = false;
        ResourceReconciler.FlowReconciliationTransaction transaction
            = ResourceReconciler.BeginFlowTransaction(CompositionContext.Default, flowRollbackSnapshot: null);

        transaction.Add(owner.Children, consumed, field, versions);
        Assert.Throws<InvalidOperationException>(
            () => transaction.Add(owner.Children, consumed, field, new List<int>()));

        transaction.Commit(ref changed);

        Assert.Throws<InvalidOperationException>(() => transaction.Commit(ref changed));
        Assert.Throws<InvalidOperationException>(
            () => transaction.Add(owner.Children, consumed, new List<Sound.Resource>(), new List<int>()));
    }
}
