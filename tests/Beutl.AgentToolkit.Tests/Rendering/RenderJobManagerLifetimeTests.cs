using System.Collections.Concurrent;
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
    public async Task EnqueueRacingDispose_CancelsAcceptedJobsAndReturnsRejectedLeaseOwnership()
    {
        for (int iteration = 0; iteration < 128; iteration++)
        {
            using var manager = new RenderJobManager();
            var context = new QueuedSynchronizationContext();
            using var start = new ManualResetEventSlim();
            var accepted = new ConcurrentBag<(string JobId, CountingLease Lease)>();
            var rejected = new ConcurrentBag<CountingLease>();
            int workStarted = 0;
            Task enqueue = Task.Run(() =>
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    start.Wait();
                    for (int job = 0; job < 16; job++)
                    {
                        var lease = new CountingLease();
                        try
                        {
                            string id = manager.Enqueue("race", (_, _) =>
                            {
                                Interlocked.Increment(ref workStarted);
                                return Task.FromResult<JsonNode>(new JsonObject());
                            }, lease);
                            accepted.Add((id, lease));
                        }
                        catch (ObjectDisposedException)
                        {
                            Assert.That(lease.DisposeCount, Is.Zero, "a rejected lease remains caller-owned");
                            lease.Dispose();
                            rejected.Add(lease);
                        }
                    }
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            });
            Task dispose = Task.Run(() =>
            {
                start.Wait();
                Thread.Yield();
                manager.Dispose();
            });
            start.Set();
            await Task.WhenAll(enqueue, dispose).WaitAsync(TimeSpan.FromSeconds(5));
            context.RunPending();

            Assert.Multiple(() =>
            {
                Assert.That(workStarted, Is.Zero);
                Assert.That(accepted.Count + rejected.Count, Is.EqualTo(16));
                foreach ((string id, CountingLease lease) in accepted)
                {
                    RenderJobSnapshot? snapshot = manager.Get(id);
                    Assert.That(snapshot!.State, Is.EqualTo("cancelled"), $"accepted job in iteration {iteration}");
                    Assert.That(snapshot.Error, Is.Null);
                    Assert.That(lease.DisposeCount, Is.EqualTo(1));
                }
                foreach (CountingLease lease in rejected)
                    Assert.That(lease.DisposeCount, Is.EqualTo(1));
                Assert.That(manager.HasRunningJobs, Is.False);
            });
        }
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
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();

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
