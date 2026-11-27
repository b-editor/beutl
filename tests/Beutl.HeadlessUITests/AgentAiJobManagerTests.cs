using System.Text.Json;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentAiJobManagerTests
{
    [Test]
    public async Task AStatusIsWrittenByNameAsTheContractSays()
    {
        using var jobs = new AgentAiJobManager();
        string jobId = jobs.Start("image.generate", (_, _) => Task.FromResult(new AgentAiJobOutput("result.png", "image", null, null)));
        AgentAiJobSnapshot snapshot = (await jobs.WaitAsync(jobId, TimeSpan.FromSeconds(10), CancellationToken.None))!;

        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.That(json, Does.Contain("\"status\":\"Succeeded\""));
    }

    [Test]
    public async Task ConcurrentCallsShareAnExistingJobOnlyWithinTheSameAccountAndOperation()
    {
        using var jobs = new AgentAiJobManager();
        var gate = new TaskCompletionSource<AgentAiJobOutput>();
        var key = new AgentAiRequestKey("account-a", "image.generate", "request");
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            () => jobs.GetOrStart(key.Operation, (_, token) => gate.Task.WaitAsync(token), null, key))));
        var (otherAccount, _) = jobs.GetOrStart(key.Operation,
            (_, token) => gate.Task.WaitAsync(token), null, key with { AccountId = "account-b" });
        var (otherOperation, _) = jobs.GetOrStart("video.generate",
            (_, token) => gate.Task.WaitAsync(token), null, key with { Operation = "video.generate" });
        gate.SetResult(new AgentAiJobOutput("result.png", "image", null, null));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(result => result.Started), Is.EqualTo(1));
            Assert.That(results.Select(result => result.Job.Id), Is.All.EqualTo(results[0].Job.Id));
            Assert.That(new[] { results[0].Job.Id, otherAccount.Id, otherOperation.Id }, Is.Unique);
        });
    }

    [Test]
    public async Task FinishedJobsArePrunedAsTheyFinish()
    {
        using var jobs = new AgentAiJobManager();
        var gate = new TaskCompletionSource();
        // All of them start while the earlier ones still run, so pruning at start removes none.
        string[] ids = [.. Enumerable.Range(0, AgentAiJobManager.RetainedFinishedJobs + 2).Select(_ => jobs.Start(
            "image.generate",
            async (_, token) =>
            {
                await gate.Task.WaitAsync(token);
                return new AgentAiJobOutput("result.png", "image", null, null);
            }))];

        gate.SetResult();
        foreach (string id in ids)
            await jobs.WaitAsync(id, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.That(ids.Count(id => jobs.Get(id) is not null), Is.EqualTo(AgentAiJobManager.RetainedFinishedJobs));
    }

    [Test]
    public async Task AJobThatFinishesLastKeepsItsResultThoughItStartedFirst()
    {
        using var jobs = new AgentAiJobManager();
        var gate = new TaskCompletionSource();
        string slow = jobs.Start("video.generate", async (_, token) =>
        {
            await gate.Task.WaitAsync(token);
            return new AgentAiJobOutput("slow.mp4", "video", null, null);
        });
        var key = new AgentAiRequestKey("account", "image.generate", "first");
        var (first, _) = jobs.GetOrStart(key.Operation,
            (_, _) => Task.FromResult(new AgentAiJobOutput("first.png", "image", null, null)), null, key);
        await first.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        AgentAiJobManager.Job replay = jobs.FindReplay(key)!;
        string[] quick = [first.Id, .. Enumerable.Range(0, AgentAiJobManager.RetainedFinishedJobs - 1).Select(_ => jobs.Start(
            "image.generate",
            (_, _) => Task.FromResult(new AgentAiJobOutput("quick.png", "image", null, null))))];
        foreach (string id in quick)
            await jobs.WaitAsync(id, TimeSpan.FromSeconds(10), CancellationToken.None);

        gate.SetResult();
        AgentAiJobSnapshot? finished = await jobs.WaitAsync(slow, TimeSpan.FromSeconds(10), CancellationToken.None);
        AgentAiJobSnapshot retainedReplay = await replay.WaitAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(finished?.Output?.OutputPath, Is.EqualTo("slow.mp4"));
            Assert.That(jobs.Get(slow), Is.Not.Null, "the newest result is kept");
            Assert.That(quick.Count(id => jobs.Get(id) is null), Is.EqualTo(1), "the earliest finished one makes room");
            Assert.That(jobs.FindReplay(key), Is.Null);
            Assert.That(retainedReplay.Output?.OutputPath, Is.EqualTo("first.png"), "a replay already in progress keeps its job");
        });
    }

    [Test]
    public async Task AnUnexpectedFailureKeepsItsMessageOutOfTheResult()
    {
        using var jobs = new AgentAiJobManager();
        var key = new AgentAiRequestKey("account", AgentHostAiTools.TranscriptionOperation, "request");
        var (job, _) = jobs.GetOrStart(key.Operation,
            (_, _) => Task.FromException<AgentAiJobOutput>(new InvalidOperationException("/Users/someone/private/notes.wav")), null, key);

        AgentAiJobSnapshot snapshot = await job.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        var (retried, started) = jobs.GetOrStart(key.Operation,
            (_, _) => Task.FromResult(new AgentAiJobOutput(null, "transcript", null, null)), null, key);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Status, Is.EqualTo(AgentAiJobStatus.Failed));
            Assert.That(snapshot.ErrorCode, Is.EqualTo(ErrorCode.AiGenerationFailed));
            Assert.That(snapshot.ErrorMessage, Does.Not.Contain("/Users/someone"));
            Assert.That(started, Is.True);
            Assert.That(retried.Id, Is.Not.EqualTo(job.Id));
        });
    }
}
