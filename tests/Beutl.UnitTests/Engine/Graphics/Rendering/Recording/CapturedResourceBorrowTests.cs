using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins what <see cref="RenderNodeContext.Borrow{T}(ValueTuple{T, int})"/> puts into a borrowed resource's
/// cache identity, against the three-argument form the engine's own nodes used before it.
/// </summary>
[TestFixture]
public sealed class CapturedResourceBorrowTests
{
    [Test]
    public void AnAttachedResource_KeepsTheIdentityTheThreeArgumentFormProduced()
    {
        var geometry = new EllipseGeometry();
        using Geometry.Resource resource = (Geometry.Resource)geometry.ToResource(CompositionContext.Default);
        (Geometry.Resource Resource, int Version) captured = resource.Capture()!.Value;

        RenderResourceIdentity fromCapture = BorrowIdentity(context => context.Borrow(captured));
        RenderResourceIdentity fromArguments = BorrowIdentity(context => context.Borrow(
            captured.Resource,
            captured.Resource.GetOriginal().Id,
            captured.Version));

        Assert.That(fromCapture, Is.EqualTo(fromArguments));
        Assert.That(fromCapture.Key, Is.EqualTo(geometry.Id));
    }

    [Test]
    public void ADetachedResource_GetsAStableIdentityWhereTheBackingObjectIdWouldThrow()
    {
        using var first = new EngineObject.Resource();
        using var second = new EngineObject.Resource();

        Assert.That(first.GetOriginal(), Is.Null,
            "a resource that never went through ToResource has no backing EngineObject");

        RenderResourceIdentity firstIdentity = BorrowIdentity(c => c.Borrow(first.Capture()!.Value));
        RenderResourceIdentity firstAgain = BorrowIdentity(c => c.Borrow(first.Capture()!.Value));
        RenderResourceIdentity secondIdentity = BorrowIdentity(c => c.Borrow(second.Capture()!.Value));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => first.GetOriginal().Id,
                Throws.TypeOf<NullReferenceException>(),
                "the derivation the call sites used before cannot serve a detached resource");
            Assert.That(firstIdentity, Is.EqualTo(firstAgain),
                "a detached resource must coalesce with itself across requests");
            Assert.That(firstIdentity, Is.Not.EqualTo(secondIdentity),
                "two detached resources must never share a cache identity");
        });
    }

    [Test]
    public void TheRecordedVersion_IsTheSnapshotsAndNotTheResourcesCurrentOne()
    {
        var geometry = new EllipseGeometry();
        using Geometry.Resource resource = (Geometry.Resource)geometry.ToResource(CompositionContext.Default);
        (Geometry.Resource Resource, int Version) captured = resource.Capture()!.Value;
        resource.Version = captured.Version + 7;

        RenderResourceIdentity identity = BorrowIdentity(context => context.Borrow(captured));

        Assert.That(identity.Version, Is.EqualTo(captured.Version),
            "the cache key must not encode a version the node's Update never compared");
    }

    [Test]
    public void ANullResourceInTheSnapshot_IsRejectedByTheOverload()
    {
        Assert.That(
            () => BorrowIdentity(context => context.Borrow<Geometry.Resource>((null!, 0))),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void TheOverload_CostsNoMoreThanTheThreeArgumentFormItReplaces()
    {
        var geometry = new EllipseGeometry();
        using Geometry.Resource resource = (Geometry.Resource)geometry.ToResource(CompositionContext.Default);
        (Geometry.Resource Resource, int Version) captured = resource.Capture()!.Value;

        long fromCapture = MeasureBytesPerBorrow(context => context.Borrow(captured));
        long fromArguments = MeasureBytesPerBorrow(context => context.Borrow(
            captured.Resource,
            captured.Resource.GetOriginal().Id,
            captured.Version));

        TestContext.Out.WriteLine($"captured snapshot: {fromCapture} bytes/borrow");
        TestContext.Out.WriteLine($"explicit arguments: {fromArguments} bytes/borrow");
        Assert.That(fromCapture, Is.LessThanOrEqualTo(fromArguments),
            "Process runs per node per frame, so passing the snapshot must not box more than the pair did");
    }

    private static long MeasureBytesPerBorrow(Func<RenderNodeContext, RenderResource> borrow)
    {
        const int Iterations = 5000;
        for (int index = 0; index < 200; index++)
            _ = BorrowIdentity(borrow);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Iterations; index++)
            _ = BorrowIdentity(borrow);
        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / Iterations;
    }

    private static RenderResourceIdentity BorrowIdentity(Func<RenderNodeContext, RenderResource> borrow)
    {
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));
        var host = new BorrowOnlyHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        RenderResource resource = borrow(new RenderNodeContext(transaction));
        transaction.Commit();
        return resource.CacheIdentity;
    }

    private sealed class BorrowOnlyHost(RenderRequest request) : IRenderRequestRecordingHost
    {
        public RenderRequest Request { get; } = request;

        public bool IsRenderCacheEnabled => true;

        public IReadOnlyList<RenderFragmentReference> RecordNode(
            NodeRecordingTransaction parent,
            RenderNode node,
            IReadOnlyList<RenderFragmentReference> inputs,
            bool subtree)
            => throw new NotSupportedException();

        public RecordedNestedRenderRequest RecordNestedRequest(RenderNode root, RenderRequestOptions options)
            => throw new NotSupportedException();

        public void Commit(NodeRecordingCommit commit)
        {
            foreach (RenderResource resource in commit.Resources)
                Request.Options.Owner.ResourceRegistry.Commit(resource);
        }
    }
}
