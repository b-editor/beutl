using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RenderRequestOwnerTests
{
    [Test]
    public void PrimaryFailure_IsPreservedAndLaterFailuresAreSecondary()
    {
        var primary = new ApplicationException("render-primary");
        var later = new InvalidOperationException("render-secondary");
        var cleanup = new IOException("cleanup-secondary");
        using var owner = new RenderRequestOwner();
        var cleanupResource = new TrackedDisposable(cleanup);
        RenderResource<TrackedDisposable> cleanupToken = owner.ResourceRegistry.RegisterOwned(cleanupResource);
        owner.ResourceRegistry.Commit(cleanupToken);

        owner.RecordPrimaryFailure(primary);
        owner.RecordPrimaryFailure(primary);
        owner.RecordPrimaryFailure(later);
        owner.Cleanup();

        Exception thrown = Assert.Throws<ApplicationException>(() => owner.ThrowIfFailed())!;
        AggregateException cleanupAggregate = (AggregateException)owner.CleanupFailures.Single();
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(primary));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(primary));
            Assert.That(owner.SecondaryFailures, Is.EqualTo(new Exception[] { later, cleanupAggregate }));
            Assert.That(cleanupAggregate.InnerExceptions, Is.EqualTo(new[] { cleanup }));
            Assert.That(cleanupResource.DisposeCount, Is.EqualTo(1));
        });
    }

    private sealed class TrackedDisposable(Exception? failure = null) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (failure is not null)
                throw failure;
        }
    }
}
