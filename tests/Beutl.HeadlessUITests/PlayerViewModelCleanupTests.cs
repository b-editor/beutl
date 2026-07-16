using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class PlayerViewModelCleanupTests
{
    [Test]
    public void TryDisposeMeasurementResource_SweepsAllResourcesAndPreservesFirstCleanupFailure()
    {
        var firstFailure = new InvalidOperationException("first-cleanup-failure");
        var secondFailure = new ApplicationException("second-cleanup-failure");
        var first = new DisposalProbe(firstFailure);
        var second = new DisposalProbe(secondFailure);
        var third = new DisposalProbe();
        Exception? failure = null;

        foreach (IDisposable resource in new IDisposable[] { first, second, third })
        {
            PlayerViewModel.TryDisposeMeasurementResource(resource, ref failure);
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(firstFailure));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(third.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TryDisposeMeasurementResource_DoesNotReplacePrimaryFailure()
    {
        var primaryFailure = new InvalidOperationException("measurement-failure");
        var cleanupFailure = new ApplicationException("cleanup-failure");
        var resource = new DisposalProbe(cleanupFailure);
        Exception? failure = primaryFailure;

        PlayerViewModel.TryDisposeMeasurementResource(resource, ref failure);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primaryFailure));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
        });
    }

    private sealed class DisposalProbe(Exception? failure = null) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (failure != null)
                throw failure;
        }
    }
}
