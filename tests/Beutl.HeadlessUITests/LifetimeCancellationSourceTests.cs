using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LifetimeCancellationSourceTests
{
    [Test]
    public void ConcurrentCancelAndDispose_AreIdempotentAndTokenRemainsReadable()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var source = new LifetimeCancellationSource();
            CancellationToken token = source.Token;

            Assert.DoesNotThrow(() => Parallel.Invoke(
                source.Cancel,
                source.Dispose,
                source.Cancel,
                source.Dispose));
            Assert.DoesNotThrow(() => _ = token.IsCancellationRequested);
            Assert.That(token.IsCancellationRequested, Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelOrDispose_DuringCancellationCallback_DoesNotWaitForCallback(bool dispose)
    {
        var source = new LifetimeCancellationSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = source.Token.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });

        Task cancellation = Task.Run(source.Cancel);
        Task reentry = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Action operation = dispose ? source.Dispose : source.Cancel;
            reentry = Task.Run(operation);

            // A callback may wait for a worker which also cancels or disposes this lifetime.
            await reentry.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(source.Token.IsCancellationRequested, Is.True);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(cancellation, reentry).WaitAsync(TimeSpan.FromSeconds(5));
            source.Dispose();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelOrDispose_WhenCallbackThrows_PropagatesFailureAndRemainsIdempotent(bool dispose)
    {
        using var source = new LifetimeCancellationSource();
        var failure = new InvalidOperationException("Callback failed.");
        int callbacks = 0;
        using CancellationTokenRegistration registration = source.Token.Register(() =>
        {
            callbacks++;
            throw failure;
        });
        Action operation = dispose ? source.Dispose : source.Cancel;

        AggregateException? actual = Assert.Throws<AggregateException>(() => operation());

        Assert.Multiple(() =>
        {
            Assert.That(actual!.InnerExceptions, Is.EqualTo(new[] { failure }));
            Assert.That(source.Token.IsCancellationRequested, Is.True);
        });
        Assert.DoesNotThrow(source.Cancel);
        Assert.DoesNotThrow(source.Dispose);
        Assert.That(callbacks, Is.EqualTo(1));
    }

    [Test]
    public void CancellationCallback_CanCancelAndDisposeLifetime()
    {
        using var source = new LifetimeCancellationSource();
        bool invoked = false;
        using CancellationTokenRegistration registration = source.Token.Register(() =>
        {
            source.Cancel();
            source.Dispose();
            invoked = true;
        });

        Assert.DoesNotThrow(source.Cancel);

        Assert.That(invoked, Is.True);
        Assert.That(source.Token.IsCancellationRequested, Is.True);
    }
}
