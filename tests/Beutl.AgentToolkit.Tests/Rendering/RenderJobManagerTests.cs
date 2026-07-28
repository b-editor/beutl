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

        string jobId = manager.Enqueue("test", async (_, _) =>
        {
            await gate.Task;
            return new JsonObject { ["ok"] = true };
        });

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

        string jobId = manager.Enqueue("test", (_, _) =>
            throw new InvalidOperationException("boom"));

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

        string jobId = manager.Enqueue("test", async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return (JsonNode)new JsonObject();
        });

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

        string jobA = manager.Enqueue("test", async (_, _) =>
        {
            startedA = true;
            await gateA.Task;
            return (JsonNode)new JsonObject();
        });
        Assert.That(SpinWait.SpinUntil(() => startedA, 2000), Is.True);

        string jobB = manager.Enqueue("test", async (_, _) =>
        {
            startedB = true;
            await gateB.Task;
            return (JsonNode)new JsonObject();
        });

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
    [Test]
    public void A_running_job_reports_progress_so_slow_is_distinguishable_from_hung()
    {
        using var manager = new RenderJobManager();
        var gate = new TaskCompletionSource();

        string jobId = manager.Enqueue("test", async (progress, _) =>
        {
            progress.Report(3, 12, "rendering shots");
            await gate.Task;
            return (JsonNode)new JsonObject();
        });

        Assert.That(SpinWait.SpinUntil(() => manager.Get(jobId)?.Progress?.Completed == 3, 2000), Is.True);
        RenderJobSnapshot running = manager.Get(jobId)!;

        gate.SetResult();
        Assert.That(SpinWait.SpinUntil(() => manager.Get(jobId)?.State == "completed", 2000), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(running.State, Is.EqualTo("running"));
            Assert.That(running.Progress!.Completed, Is.EqualTo(3));
            Assert.That(running.Progress.Total, Is.EqualTo(12));
            Assert.That(running.Progress.Stage, Is.EqualTo("rendering shots"));
            Assert.That(running.Progress.Ratio, Is.EqualTo(0.25).Within(0.001));
        });
    }

    [Test]
    public void A_job_waiting_behind_another_reports_that_it_is_queued()
    {
        using var manager = new RenderJobManager();
        var gate = new TaskCompletionSource();
        bool startedFirst = false;

        manager.Enqueue("test", async (_, _) =>
        {
            startedFirst = true;
            await gate.Task;
            return (JsonNode)new JsonObject();
        });
        Assert.That(SpinWait.SpinUntil(() => startedFirst, 2000), Is.True);

        string queued = manager.Enqueue("test", (_, _) => Task.FromResult((JsonNode)new JsonObject()));

        Assert.That(SpinWait.SpinUntil(() => manager.Get(queued)?.Progress?.Stage == "queued", 2000), Is.True);
        gate.SetResult();
    }

}
