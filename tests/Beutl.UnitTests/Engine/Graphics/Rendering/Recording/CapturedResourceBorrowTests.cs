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
            Assert.That(first.GetOriginal(), Is.Null,
                "the nullable original contract reports that a detached resource has no backing id");
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

    /// <remarks>
    /// The per-borrow cost is a few tens of bytes of boxing on top of a recording scaffold that allocates three
    /// orders of magnitude more, so a single timed round resolves the difference to roughly one byte and any
    /// unrelated allocation on this thread — a tiering re-JIT, a lazily grown pool — flips it. Allocation noise
    /// is one-sided, because nothing an unrelated caller does can lower the counter, so the smallest of several
    /// rounds is the steady-state cost and the estimate stops depending on which round was quiet.
    /// </remarks>
    private static long MeasureBytesPerBorrow(Func<RenderNodeContext, RenderResource> borrow)
    {
        const int Iterations = 5000;
        const int Rounds = 7;
        for (int index = 0; index < 200; index++)
            _ = BorrowIdentity(borrow);

        long quietestRound = long.MaxValue;
        for (int round = 0; round < Rounds; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < Iterations; index++)
                _ = BorrowIdentity(borrow);
            long after = GC.GetAllocatedBytesForCurrentThread();
            quietestRound = Math.Min(quietestRound, after - before);
        }

        return quietestRound / Iterations;
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
