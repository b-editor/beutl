using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Microsoft.Extensions.Logging;

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

    [Test]
    public void ReportSecondaryFailures_LogsEachMaskedFailureOnce()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start();
        var first = new InvalidOperationException("render-secondary-1");
        var second = new InvalidOperationException("render-secondary-2");
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(new ApplicationException("render-primary"));
        owner.RecordPrimaryFailure(first);
        owner.ReportSecondaryFailures();
        owner.RecordPrimaryFailure(second);
        owner.ReportSecondaryFailures();
        owner.ReportSecondaryFailures();

        Assert.Multiple(() =>
        {
            Assert.That(logs.Entries.Select(entry => entry.Exception), Is.EqualTo(new Exception[] { first, second }));
            Assert.That(logs.Entries.Select(entry => entry.Level), Is.All.EqualTo(LogLevel.Warning));
        });
    }

    [Test]
    public void ReportSecondaryFailures_LogsNothingWhenOnlyThePrimaryFailed()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start();
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(new ApplicationException("render-primary"));
        owner.ReportSecondaryFailures();

        Assert.That(logs.Entries, Is.Empty);
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
