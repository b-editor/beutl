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
    public void ReportMaskedFailures_LogsEachSecondaryFailureOnce()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start();
        var primary = new ApplicationException("render-primary");
        var first = new InvalidOperationException("render-secondary-1");
        var second = new InvalidOperationException("render-secondary-2");
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(primary);
        owner.RecordPrimaryFailure(first);
        owner.ReportMaskedFailures(primary);
        owner.RecordPrimaryFailure(second);
        owner.ReportMaskedFailures(primary);
        owner.ReportMaskedFailures(primary);

        Assert.Multiple(() =>
        {
            Assert.That(logs.Entries.Select(entry => entry.Exception), Is.EqualTo(new Exception[] { first, second }));
            Assert.That(logs.Entries.Select(entry => entry.Level), Is.All.EqualTo(LogLevel.Warning));
        });
    }

    [Test]
    public void ReportMaskedFailures_LogsNothingWhenOnlyThePrimaryFailed()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start();
        var primary = new ApplicationException("render-primary");
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(primary);
        owner.ReportMaskedFailures(primary);

        Assert.That(logs.Entries, Is.Empty);
    }

    [Test]
    public void ReportMaskedFailures_LogsTheOwnerPrimaryOnceWhenADifferentFailureIsThrown()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start();
        var ownerPrimary = new IOException("cleanup-primary");
        var secondary = new InvalidOperationException("render-secondary");
        var thrown = new ApplicationException("caller-failure");
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(ownerPrimary);
        owner.RecordPrimaryFailure(secondary);
        owner.ReportMaskedFailures(thrown);
        owner.ReportMaskedFailures(thrown);

        Assert.That(
            logs.Entries.Select(entry => entry.Exception),
            Is.EqualTo(new Exception[] { ownerPrimary, secondary }));
    }

    [Test]
    public void ReportMaskedFailures_DoesNotLogTheFailureBeingThrown()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start();
        var ownerPrimary = new IOException("cleanup-primary");
        var thrown = new ApplicationException("caller-failure");
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(ownerPrimary);
        owner.RecordPrimaryFailure(thrown);
        owner.ReportMaskedFailures(thrown);

        Assert.That(logs.Entries.Select(entry => entry.Exception), Is.EqualTo(new Exception[] { ownerPrimary }));
    }

    [Test]
    public void ReportMaskedFailures_IgnoresLoggingProviderFailures()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start(throwOnLog: true);
        var primary = new ApplicationException("render-primary");
        var first = new InvalidOperationException("render-secondary-1");
        var second = new InvalidOperationException("render-secondary-2");
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(primary);
        owner.RecordPrimaryFailure(first);
        owner.RecordPrimaryFailure(second);

        Assert.That(() => owner.ReportMaskedFailures(primary), Throws.Nothing);
        Assert.That(
            logs.Entries.Select(entry => entry.Exception),
            Is.EqualTo(new Exception[] { first, second }),
            "a failing provider must not stop the remaining failures from being reported");
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
