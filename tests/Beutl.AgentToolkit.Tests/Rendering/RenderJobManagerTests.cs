using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Rendering;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class RenderJobManagerTests
{
    [Test]
    public void Get_returns_null_for_unknown_job()
    {
        using var manager = new RenderJobManager();
        Assert.That(manager.Get("does-not-exist"), Is.Null);
    }

    [Test]
    public async Task Enqueue_reports_running_then_completed_with_result()
    {
        using var manager = new RenderJobManager();
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
        using var manager = new RenderJobManager();

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
        using var manager = new RenderJobManager();

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
    public void Cancel_unknown_job_returns_false()
    {
        using var manager = new RenderJobManager();
        Assert.That(manager.Cancel("nope"), Is.False);
    }

    [Test]
    public async Task Background_jobs_run_single_flight_in_order()
    {
        using var manager = new RenderJobManager();
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
    public void Enqueue_failure_leaves_the_output_lease_owned_by_the_caller()
    {
        var manager = new RenderJobManager();
        manager.Dispose();
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

        manager.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(completedLease.DisposeCount, Is.EqualTo(1));
            Assert.That(cancelledLease.DisposeCount, Is.EqualTo(1));
            Assert.That(failedLease.DisposeCount, Is.EqualTo(1));
        });
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
