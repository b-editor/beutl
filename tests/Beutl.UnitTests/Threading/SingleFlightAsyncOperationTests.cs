using Beutl.Threading;

namespace Beutl.UnitTests.Threading;

[TestFixture]
public class SingleFlightAsyncOperationTests
{
    [Test]
    public async Task RunOrJoinAsync_WhenIdle_RunsOperationAndReturnsTrue()
    {
        var op = new SingleFlightAsyncOperation();
        int runs = 0;

        bool owned = await op.RunOrJoinAsync(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        Assert.That(owned, Is.True);
        Assert.That(runs, Is.EqualTo(1));
        Assert.That(op.InFlight, Is.Null);
    }

    [Test]
    public async Task TryRunAsync_WhenIdle_RunsOperationAndReturnsTrue()
    {
        var op = new SingleFlightAsyncOperation();
        int runs = 0;

        bool owned = await op.TryRunAsync(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        Assert.That(owned, Is.True);
        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public async Task TryRunAsync_WhenBusy_SkipsWithoutRunningOrAwaiting()
    {
        var op = new SingleFlightAsyncOperation();
        var release = new TaskCompletionSource();
        int runs = 0;

        Task<bool> owner = op.RunOrJoinAsync(async () =>
        {
            runs++;
            await release.Task;
        });

        Assert.That(op.InFlight, Is.Not.Null);

        // A second caller skips the body and returns without awaiting the owner.
        bool second = await op.TryRunAsync(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        Assert.That(second, Is.False);
        Assert.That(runs, Is.EqualTo(1));
        Assert.That(owner.IsCompleted, Is.False);

        release.SetResult();
        Assert.That(await owner, Is.True);
        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public async Task RunOrJoinAsync_WhenBusy_JoinsInFlightAndReturnsFalse()
    {
        var op = new SingleFlightAsyncOperation();
        var release = new TaskCompletionSource();
        int runs = 0;

        Task<bool> owner = op.RunOrJoinAsync(async () =>
        {
            runs++;
            await release.Task;
        });

        Task<bool> joiner = op.RunOrJoinAsync(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        // The joiner must wait for the owner rather than starting its own run.
        Assert.That(joiner.IsCompleted, Is.False);

        release.SetResult();

        Assert.That(await owner, Is.True);
        Assert.That(await joiner, Is.False);
        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public async Task RunOrJoinAsync_ManyConcurrentCallers_RunBodyExactlyOnce()
    {
        var op = new SingleFlightAsyncOperation();
        var release = new TaskCompletionSource();
        int runs = 0;

        Task<bool> owner = op.RunOrJoinAsync(async () =>
        {
            Interlocked.Increment(ref runs);
            await release.Task;
        });

        var joiners = Enumerable.Range(0, 16)
            .Select(_ => op.RunOrJoinAsync(() =>
            {
                Interlocked.Increment(ref runs);
                return Task.CompletedTask;
            }))
            .ToArray();

        release.SetResult();

        bool ownerResult = await owner;
        bool[] joinerResults = await Task.WhenAll(joiners);

        Assert.That(ownerResult, Is.True);
        Assert.That(joinerResults, Is.All.False);
        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public async Task RunOrJoinAsync_AfterCompletion_CanRunAgain()
    {
        var op = new SingleFlightAsyncOperation();
        int runs = 0;

        bool first = await op.RunOrJoinAsync(() => { runs++; return Task.CompletedTask; });
        bool second = await op.RunOrJoinAsync(() => { runs++; return Task.CompletedTask; });

        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
        Assert.That(runs, Is.EqualTo(2));
        Assert.That(op.InFlight, Is.Null);
    }

    [Test]
    public void RunOrJoinAsync_OwnerThrows_PropagatesToOwnerAndResetsGate()
    {
        var op = new SingleFlightAsyncOperation();

        Assert.That(
            async () => await op.RunOrJoinAsync(() => throw new InvalidOperationException("boom")),
            Throws.TypeOf<InvalidOperationException>());

        // The gate must reset so subsequent operations can run.
        Assert.That(op.InFlight, Is.Null);
    }

    [Test]
    public async Task RunOrJoinAsync_OperationCatchesOwnException_OwnerSucceedsAndGateResets()
    {
        // Mirrors the call sites, where the lambda handles its own exceptions.
        var op = new SingleFlightAsyncOperation();
        bool ran = false;

        bool owned = await op.RunOrJoinAsync(async () =>
        {
            await Task.Yield();
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch
            {
                ran = true;
            }
        });

        Assert.That(owned, Is.True);
        Assert.That(ran, Is.True);
        Assert.That(op.InFlight, Is.Null);

        // Gate is reusable after a self-handled failure.
        Assert.That(await op.RunOrJoinAsync(() => Task.CompletedTask), Is.True);
    }

    [Test]
    public async Task RunOrJoinAsync_JoinerDoesNotObserveOwnerFailure()
    {
        var op = new SingleFlightAsyncOperation();
        var release = new TaskCompletionSource();

        Task<bool> owner = op.RunOrJoinAsync(async () =>
        {
            await release.Task;
            throw new InvalidOperationException("boom");
        });

        Task<bool> joiner = op.RunOrJoinAsync(() => Task.CompletedTask);

        release.SetResult();

        Assert.That(async () => await owner, Throws.TypeOf<InvalidOperationException>());
        // The joiner only waits for teardown to finish; it must complete without throwing.
        Assert.That(await joiner, Is.False);
    }
}
