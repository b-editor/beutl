using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AsyncOperationLifetimeTests
{
    [Test]
    public async Task Cancel_EndsOneOperationAndLeavesTheRestRunning()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using AsyncOperationLifetime.Operation abandoned = lifetime.TryEnter()!;
        using AsyncOperationLifetime.Operation kept = lifetime.TryEnter()!;

        abandoned.Cancel();
        using AsyncOperationLifetime.Operation? next = lifetime.TryEnter();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(abandoned.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(kept.CancellationToken.IsCancellationRequested, Is.False,
                "Leaving one long request must not shut down everything else the tab is doing.");
            Assert.That(next, Is.Not.Null,
                "And the tab still admits the next request.");
        }
    }

    [Test]
    public async Task Cancel_StillPublishesSoTheViewModelCanResetItsState()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;

        operation.Cancel();

        Assert.That(operation.TryPublish(() => { }), Is.True,
            "Cancelling is not shutdown, so the finally block can still clear the running flag.");
    }

    [Test]
    public async Task Stop_EndsEveryOperationAdmittedSoFar()
    {
        var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation first = lifetime.TryEnter()!;
        AsyncOperationLifetime.Operation second = lifetime.TryEnter()!;

        // Stopping waits for what it cancelled, so the operations are released first.
        Task stopping = lifetime.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(second.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.TryEnter(), Is.Null);
            Assert.That(first.TryPublish(() => { }), Is.False);
        }

        first.Dispose();
        second.Dispose();
        await stopping;
        await lifetime.DisposeAsync();
    }

    [Test]
    public async Task Cancel_AfterDisposeIsIgnored()
    {
        await using var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        operation.Dispose();

        Assert.DoesNotThrow(operation.Cancel);
    }
}
