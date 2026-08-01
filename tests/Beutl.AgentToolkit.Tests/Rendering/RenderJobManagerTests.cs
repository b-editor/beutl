using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Rendering;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class RenderJobManagerTests
{
    [Test]
    public async Task Get_returns_null_for_unknown_job()
    {
        await using var manager = new RenderJobManager();
        Assert.That(manager.Get("does-not-exist"), Is.Null);
    }

    [Test]
    public async Task Enqueue_reports_running_then_completed_with_result()
    {
        await using var manager = new RenderJobManager();
        var gate = new TaskCompletionSource();

        string jobId = manager.Enqueue("test", async _ =>
        {
            await gate.Task;
            return new JsonObject { ["ok"] = true };
        }, new TestLease());

        Assert.That(SpinWait.SpinUntil(() => manager.Get(jobId)?.State == "running", 2000), Is.True);
        Assert.That(manager.Get(jobId)!.Result, Is.Null);

        gate.SetResult();

        RenderJobSnapshot snapshot = await WaitForTerminalAsync(manager, jobId);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.State, Is.EqualTo("completed"));
            Assert.That(snapshot.Result, Is.Not.Null);
            Assert.That(snapshot.Result!["ok"]!.GetValue<bool>(), Is.True);
            Assert.That(snapshot.Error, Is.Null);
            Assert.That(snapshot.CompletedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task Failed_job_maps_exception_to_error_code()
    {
        await using var manager = new RenderJobManager();

        string jobId = manager.Enqueue(
            "test",
            _ => throw new InvalidOperationException("boom"),
            new TestLease());

        RenderJobSnapshot snapshot = await WaitForTerminalAsync(manager, jobId);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.State, Is.EqualTo("failed"));
            Assert.That(snapshot.Error, Is.Not.Null);
            Assert.That(snapshot.Error!.Code, Is.EqualTo("internal_error"));
            // ToolErrorMapper redacts unexpected exception messages (they can embed absolute paths);
            // only the exception type reaches the client.
            Assert.That(snapshot.Error.Message, Does.Contain(nameof(InvalidOperationException)));
            Assert.That(snapshot.Error.Message, Does.Not.Contain("boom"));
        });
    }

    [Test]
    public async Task Cancel_running_job_transitions_to_cancelled()
    {
        await using var manager = new RenderJobManager();

        string jobId = manager.Enqueue("test", async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return (JsonNode)new JsonObject();
        }, new TestLease());

        Assert.That(SpinWait.SpinUntil(() => manager.Get(jobId)?.State == "running", 2000), Is.True);
        Assert.That(manager.Cancel(jobId), Is.True);

        RenderJobSnapshot snapshot = await WaitForTerminalAsync(manager, jobId);
        Assert.That(snapshot.State, Is.EqualTo("cancelled"));
    }

    [Test]
    public async Task Cancel_unknown_job_returns_false()
    {
        await using var manager = new RenderJobManager();
        Assert.That(manager.Cancel("nope"), Is.False);
    }

    [Test]
    public async Task Background_jobs_run_single_flight_in_order()
    {
        await using var manager = new RenderJobManager();
        var gateA = new TaskCompletionSource();
        var gateB = new TaskCompletionSource();
        bool startedA = false;
        bool startedB = false;

        string jobA = manager.Enqueue("test", async _ =>
        {
            startedA = true;
            await gateA.Task;
            return (JsonNode)new JsonObject();
        }, new TestLease());
        Assert.That(SpinWait.SpinUntil(() => startedA, 2000), Is.True);

        string jobB = manager.Enqueue("test", async _ =>
        {
            startedB = true;
            await gateB.Task;
            return (JsonNode)new JsonObject();
        }, new TestLease());

        // B must wait for A to release the single-flight gate before its work runs.
        Assert.That(SpinWait.SpinUntil(() => startedB, 300), Is.False);
        Assert.That(manager.Get(jobB)!.State, Is.EqualTo("running"));

        gateA.SetResult();
        await WaitForTerminalAsync(manager, jobA);

        Assert.That(SpinWait.SpinUntil(() => startedB, 2000), Is.True);
        gateB.SetResult();
        RenderJobSnapshot snapshotB = await WaitForTerminalAsync(manager, jobB);
        Assert.That(snapshotB.State, Is.EqualTo("completed"));
    }

    [Test]
    public async Task Enqueue_failure_leaves_the_output_lease_owned_by_the_caller()
    {
        var manager = new RenderJobManager();
        await manager.DisposeAsync();
        var lease = new TestLease();

        Assert.Throws<ObjectDisposedException>(() => manager.Enqueue(
            "test",
            _ => Task.FromResult<JsonNode>(new JsonObject()),
            lease));

        Assert.That(lease.DisposeCount, Is.EqualTo(0));
        lease.Dispose();
        Assert.That(lease.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Background_jobs_hold_enqueue_time_leases_and_release_every_terminal_path_once()
    {
        var manager = new RenderJobManager();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queuedWorkStarted = false;
        var completedLease = new TestLease();
        var cancelledLease = new TestLease();
        var failedLease = new TestLease();

        string completedJob = manager.Enqueue("completed", async _ =>
        {
            firstStarted.TrySetResult();
            await releaseFirst.Task.ConfigureAwait(false);
            return new JsonObject { ["completed"] = true };
        }, completedLease);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        string queuedCancelledJob = manager.Enqueue("cancelled", _ =>
        {
            queuedWorkStarted = true;
            return Task.FromResult<JsonNode>(new JsonObject());
        }, cancelledLease);

        Assert.Multiple(() =>
        {
            Assert.That(completedLease.DisposeCount, Is.EqualTo(0));
            Assert.That(cancelledLease.DisposeCount, Is.EqualTo(0));
            Assert.That(manager.Cancel(queuedCancelledJob), Is.True);
        });

        RenderJobSnapshot cancelled = await WaitForTerminalAsync(manager, queuedCancelledJob);
        Assert.Multiple(() =>
        {
            Assert.That(cancelled.State, Is.EqualTo("cancelled"));
            Assert.That(queuedWorkStarted, Is.False);
            Assert.That(completedLease.DisposeCount, Is.EqualTo(0));
            Assert.That(cancelledLease.DisposeCount, Is.EqualTo(1));
        });

        releaseFirst.TrySetResult();
        RenderJobSnapshot completed = await WaitForTerminalAsync(manager, completedJob);
        Assert.Multiple(() =>
        {
            Assert.That(completed.State, Is.EqualTo("completed"));
            Assert.That(completedLease.DisposeCount, Is.EqualTo(1));
            Assert.That(cancelledLease.DisposeCount, Is.EqualTo(1));
        });

        string failedJob = manager.Enqueue(
            "failed",
            _ => throw new InvalidOperationException("expected failure"),
            failedLease);
        RenderJobSnapshot failed = await WaitForTerminalAsync(manager, failedJob);
        Assert.Multiple(() =>
        {
            Assert.That(failed.State, Is.EqualTo("failed"));
            Assert.That(completedLease.DisposeCount, Is.EqualTo(1));
            Assert.That(cancelledLease.DisposeCount, Is.EqualTo(1));
            Assert.That(failedLease.DisposeCount, Is.EqualTo(1));
        });

        await manager.DisposeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(completedLease.DisposeCount, Is.EqualTo(1));
            Assert.That(cancelledLease.DisposeCount, Is.EqualTo(1));
            Assert.That(failedLease.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DisposeAsync_cancels_and_drains_running_jobs_before_returning()
    {
        var manager = new RenderJobManager();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new TestLease();
        Task? disposal = null;
        string jobId = manager.Enqueue("test", async token =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                await releaseCancellation.Task.ConfigureAwait(false);
                throw;
            }

            return new JsonObject();
        }, lease);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            disposal = manager.DisposeAsync().AsTask();
            Task repeatedDisposal = manager.DisposeAsync().AsTask();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(repeatedDisposal, Is.SameAs(disposal));
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(lease.DisposeCount, Is.Zero);
                Assert.That(manager.Get(jobId)!.State, Is.EqualTo("running"));
            });

            releaseCancellation.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            RenderJobSnapshot snapshot = manager.Get(jobId)!;
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.State, Is.EqualTo("cancelled"));
                Assert.That(lease.DisposeCount, Is.EqualTo(1));
                Assert.That(manager.HasRunningJobs, Is.False);
            });
        }
        finally
        {
            releaseCancellation.TrySetResult();
            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                await manager.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task DisposeAsync_drains_all_jobs_before_reporting_cancellation_callback_failures()
    {
        var manager = new RenderJobManager();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLease = new TestLease();
        var secondLease = new TestLease();
        Task? disposal = null;

        string firstJob = manager.Enqueue("first", async token =>
        {
            using CancellationTokenRegistration registration = token.Register(
                static () => throw new InvalidOperationException("Expected cancellation callback failure."));
            firstStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                firstCancellationObserved.TrySetResult();
                await releaseFirstCleanup.Task.ConfigureAwait(false);
                throw;
            }

            return new JsonObject();
        }, firstLease);
        string secondJob = manager.Enqueue(
            "second",
            _ => Task.FromResult<JsonNode>(new JsonObject()),
            secondLease);

        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposal = manager.DisposeAsync().AsTask();
            await firstCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(firstLease.DisposeCount, Is.Zero);
                Assert.That(manager.Get(firstJob)!.State, Is.EqualTo("running"));
            });

            RenderJobSnapshot second = await WaitForTerminalAsync(manager, secondJob);
            Assert.Multiple(() =>
            {
                Assert.That(second.State, Is.EqualTo("cancelled"));
                Assert.That(secondLease.DisposeCount, Is.EqualTo(1));
            });

            releaseFirstCleanup.TrySetResult();
            Exception? failure = null;
            try
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.TypeOf<AggregateException>());
                Assert.That(failure!.ToString(), Does.Contain("Expected cancellation callback failure."));
                Assert.That(manager.Get(firstJob)!.State, Is.EqualTo("cancelled"));
                Assert.That(firstLease.DisposeCount, Is.EqualTo(1));
                Assert.That(manager.HasRunningJobs, Is.False);
            });
        }
        finally
        {
            releaseFirstCleanup.TrySetResult();
            if (disposal is null)
            {
                disposal = manager.DisposeAsync().AsTask();
            }

            try
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
        }
    }

    [Test]
    public async Task DisposeAsync_serializes_with_enqueue_and_drains_every_accepted_job()
    {
        var manager = new RenderJobManager();
        using var startRace = new ManualResetEventSlim();
        Task<(string? JobId, TestLease Lease)>[] enqueuers = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                startRace.Wait();
                var lease = new TestLease();
                try
                {
                    string jobId = manager.Enqueue(
                        "test",
                        async token =>
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                            return new JsonObject();
                        },
                        lease);
                    return (JobId: (string?)jobId, Lease: lease);
                }
                catch (ObjectDisposedException)
                {
                    return (JobId: (string?)null, Lease: lease);
                }
            }))
            .ToArray();
        Task disposal = Task.Run(async () =>
        {
            startRace.Wait();
            await manager.DisposeAsync();
        });

        startRace.Set();
        (string? JobId, TestLease Lease)[] outcomes = await Task.WhenAll(enqueuers);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.DisposeAsync();

        foreach ((string? jobId, TestLease lease) in outcomes)
        {
            if (jobId is null)
            {
                Assert.That(lease.DisposeCount, Is.Zero);
                lease.Dispose();
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(manager.Get(jobId)!.State, Is.EqualTo("cancelled"));
                    Assert.That(lease.DisposeCount, Is.EqualTo(1));
                });
            }
        }

        var rejectedLease = new TestLease();
        Assert.Throws<ObjectDisposedException>(() => manager.Enqueue(
            "test",
            _ => Task.FromResult<JsonNode>(new JsonObject()),
            rejectedLease));
        Assert.That(rejectedLease.DisposeCount, Is.Zero);
        rejectedLease.Dispose();
    }

    private static async Task<RenderJobSnapshot> WaitForTerminalAsync(RenderJobManager manager, string jobId)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            RenderJobSnapshot? snapshot = manager.Get(jobId);
            if (snapshot is not null && snapshot.State != "running")
            {
                return snapshot;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Job '{jobId}' did not reach a terminal state in time.");
        throw new InvalidOperationException("unreachable");
    }

    private sealed class TestLease : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }
    }
}
