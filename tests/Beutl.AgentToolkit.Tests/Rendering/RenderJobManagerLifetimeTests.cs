using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Rendering;

namespace Beutl.AgentToolkit.Tests.Rendering;

[TestFixture]
public sealed class RenderJobManagerLifetimeTests
{
    [Test]
    public void Dispose_before_a_queued_job_starts_reports_cancellation_and_releases_its_lease()
    {
        using var manager = new RenderJobManager();
        var context = new QueuedSynchronizationContext();
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        var lease = new CountingLease();
        bool workStarted = false;
        string jobId;

        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            jobId = manager.Enqueue("test", (_, _) =>
            {
                workStarted = true;
                return Task.FromResult<JsonNode>(new JsonObject());
            }, lease);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Assert.That(context.PendingCount, Is.EqualTo(1));
        manager.Dispose();
        context.RunPending();

        RenderJobSnapshot? snapshot = manager.Get(jobId);
        Assert.Multiple(() =>
        {
            Assert.That(workStarted, Is.False);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.State, Is.EqualTo("cancelled"));
            Assert.That(snapshot.Error, Is.Null);
            Assert.That(snapshot.Result, Is.Null);
            Assert.That(snapshot.CompletedAt, Is.Not.Null);
            Assert.That(manager.HasRunningJobs, Is.False);
            Assert.That(lease.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Work_disposal_failure_after_cancellation_remains_a_failure()
    {
        using var manager = new RenderJobManager();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new CompletingLease();
        string jobId = manager.Enqueue("test", async (_, _) =>
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            throw new ObjectDisposedException("work-resource");
        }, lease);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(manager.Cancel(jobId), Is.True);
            release.TrySetResult();
            await lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(5));

            RenderJobSnapshot? snapshot = manager.Get(jobId);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot!.State, Is.EqualTo("failed"));
                Assert.That(snapshot.Error!.Message, Does.Contain(nameof(ObjectDisposedException)));
                Assert.That(snapshot.Result, Is.Null);
            });
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();

        public int PendingCount => _pending.Count;

        public override void Post(SendOrPostCallback d, object? state)
        {
            _pending.Enqueue((d, state));
        }

        public void RunPending()
        {
            while (_pending.TryDequeue(out var continuation))
            {
                continuation.Callback(continuation.State);
            }
        }
    }

    private sealed class CountingLease : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class CompletingLease : IDisposable
    {
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            Released.TrySetResult();
        }
    }
}
