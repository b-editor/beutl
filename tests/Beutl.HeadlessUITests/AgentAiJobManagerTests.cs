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
        (string JobId, bool Started)[] results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            () => jobs.GetOrStart(key.Operation, (_, token) => gate.Task.WaitAsync(token), null, key))));
        (string otherAccount, _) = jobs.GetOrStart(key.Operation,
            (_, token) => gate.Task.WaitAsync(token), null, key with { AccountId = "account-b" });
        (string otherOperation, _) = jobs.GetOrStart("video.generate",
            (_, token) => gate.Task.WaitAsync(token), null, key with { Operation = "video.generate" });
        gate.SetResult(new AgentAiJobOutput("result.png", "image", null, null));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(result => result.Started), Is.EqualTo(1));
            Assert.That(results.Select(result => result.JobId), Is.All.EqualTo(results[0].JobId));
            Assert.That(new[] { results[0].JobId, otherAccount, otherOperation }, Is.Unique);
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
        string[] quick = [.. Enumerable.Range(0, AgentAiJobManager.RetainedFinishedJobs).Select(_ => jobs.Start(
            "image.generate",
            (_, _) => Task.FromResult(new AgentAiJobOutput("quick.png", "image", null, null))))];
        foreach (string id in quick)
            await jobs.WaitAsync(id, TimeSpan.FromSeconds(10), CancellationToken.None);

        gate.SetResult();
        AgentAiJobSnapshot? finished = await jobs.WaitAsync(slow, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(finished?.Output?.OutputPath, Is.EqualTo("slow.mp4"));
            Assert.That(jobs.Get(slow), Is.Not.Null, "the newest result is kept");
            Assert.That(quick.Count(id => jobs.Get(id) is null), Is.EqualTo(1), "the earliest finished one makes room");
        });
    }

    [Test]
    public async Task AnUnexpectedFailureKeepsItsMessageOutOfTheResult()
    {
        using var jobs = new AgentAiJobManager();
        var key = new AgentAiRequestKey("account", AgentHostAiTools.TranscriptionOperation, "request");
        (string jobId, _) = jobs.GetOrStart(key.Operation,
            (_, _) => Task.FromException<AgentAiJobOutput>(new InvalidOperationException("/Users/someone/private/notes.wav")), null, key);

        AgentAiJobSnapshot snapshot = (await jobs.WaitAsync(jobId, TimeSpan.FromSeconds(10), CancellationToken.None))!;
        (string retriedJobId, bool started) = jobs.GetOrStart(key.Operation,
            (_, _) => Task.FromResult(new AgentAiJobOutput(null, "transcript", null, null)), null, key);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Status, Is.EqualTo(AgentAiJobStatus.Failed));
            Assert.That(snapshot.ErrorCode, Is.EqualTo(ErrorCode.AiGenerationFailed));
            Assert.That(snapshot.ErrorMessage, Does.Not.Contain("/Users/someone"));
            Assert.That(started, Is.True);
            Assert.That(retriedJobId, Is.Not.EqualTo(jobId));
        });
    }
}
