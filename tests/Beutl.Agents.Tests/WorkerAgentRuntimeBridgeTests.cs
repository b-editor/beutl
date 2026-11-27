using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Beutl.Agents;
using Microsoft.Extensions.Logging;

namespace Beutl.Agents.Tests;

public sealed class WorkerAgentRuntimeBridgeTests
{
    private DirectoryInfo _directory = null!;
    private CancellationTokenSource _timeout = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("beutl-agents-tests-");
        _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    }

    [TearDown]
    public void TearDown()
    {
        _timeout.Dispose();
        _directory.Delete(recursive: true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LocatesWorkerInPublishedAndDevelopmentLayouts(bool nested)
    {
        const string name = "Beutl.Agents.Worker";
        string workerDirectory = nested ? _directory.CreateSubdirectory(name).FullName : _directory.FullName;
        string executable = Path.Combine(workerDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(executable, "");

        var info = WorkerAgentRuntimeBridge.CreateStartInfo(null, _directory.FullName);

        Assert.Multiple(() =>
        {
            Assert.That(info.FileName, Is.EqualTo(executable));
            Assert.That(info.WorkingDirectory, Is.EqualTo(workerDirectory));
        });
    }

    [Test]
    public async Task PingLoadsEmbeddedNodeAndDisposalExitsWorker()
    {
        var diagnostics = new WorkerDiagnostics();
        await using var bridge = new WorkerAgentRuntimeBridge(logger: diagnostics);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.Disconnected += _ => disconnected.TrySetResult();

        var health = await bridge.PingAsync(_timeout.Token);
        using var process = Process.GetProcessById(health.ProcessId);
        Assert.That(Version.Parse(health.NodeVersion).Major, Is.EqualTo(26));
        Assert.That(Path.GetFileNameWithoutExtension(health.NodeExecutablePath), Is.AnyOf("Beutl.Agents.Worker", "dotnet"));
        Assert.That((await bridge.PingAsync(_timeout.Token)).ProcessId, Is.EqualTo(health.ProcessId));

        await bridge.DisposeAsync();
        Assert.That(process.HasExited, Is.True);
        Assert.That(diagnostics.Lines, Does.Contain("Agent worker exited with code 0."));
        Assert.That(disconnected.Task.IsCompleted, Is.False);
    }

    [Test]
    public async Task AcpStreamsThroughSdkAndRoundsTripsPermissionsCancelAndLoad()
    {
        var diagnostics = new WorkerDiagnostics();
        await using var bridge = new WorkerAgentRuntimeBridge(logger: diagnostics);
        var timeline = Capture(bridge);
        var requests = new ConcurrentQueue<AgentPermissionRequest>();
        bridge.PermissionHandler = async (request, _) =>
        {
            await Task.Yield();
            requests.Enqueue(request);
            return AgentPermissionDecision.AllowOnce;
        };
        var session = Session(AgentProviderKind.Acp, "acp") with
        {
            PermissionModes = new Dictionary<AgentPermissionMode, string> { [AgentPermissionMode.Supervised] = "ask" }
        };
        var opened = await bridge.OpenSessionAsync(session, _timeout.Token);
        Assert.That(opened.ExternalSessionId, Is.EqualTo("acp-saved-session"));

        var result = await bridge.RunTurnAsync(Turn(session, "exercise"), _timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed));
            Assert.That(timeline.OfType<AgentAssistantTextDelta>().Select(item => item.Text), Is.EqualTo(new[] { "hello ", "from ACP" }));
            Assert.That(timeline.OfType<AgentAssistantMessageCompleted>().Single().Text, Is.EqualTo("hello from ACP"));
            Assert.That(timeline.OfType<AgentReasoningDelta>().Single().Text, Is.EqualTo("thinking"));
            Assert.That(timeline.OfType<AgentPlanUpdated>().Single().Steps.Single().Status, Is.EqualTo(AgentPlanStepStatus.InProgress));
            Assert.That(timeline.OfType<AgentToolCallStarted>().Single().McpServerName, Is.EqualTo("beutl"));
            Assert.That(timeline.OfType<AgentToolCallCompleted>().Single().Outputs!.Single().MimeType, Is.EqualTo("image/png"));
            Assert.That(requests.Single().McpServerName, Is.EqualTo("beutl"));
            Assert.That(timeline.OfType<AgentPermissionResolved>().Single().Decision, Is.EqualTo(AgentPermissionDecision.AllowOnce));
        });

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_timeout.Token);
        var waiting = WaitForDelta(bridge, "waiting");
        var waitTurn = Turn(session, "wait");
        var running = bridge.RunTurnAsync(waitTurn, cancel.Token);
        await waiting.WaitAsync(_timeout.Token);
        Assert.That(await bridge.SteerTurnAsync(waitTurn with { Text = "Try to steer" }, _timeout.Token), Is.False);
        await cancel.CancelAsync();
        await Assert.ThatAsync(async () => await running, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(timeline.OfType<AgentTurnCancelled>().Count(), Is.EqualTo(1));

        await bridge.OpenSessionAsync(session with { ExternalSessionId = opened.ExternalSessionId }, _timeout.Token);
        Assert.That(timeline.OfType<AgentAssistantTextDelta>().Any(item => item.Text == "replayed history"), Is.False);
        await bridge.CloseSessionAsync(session.ThreadId, _timeout.Token);
        await bridge.DisposeAsync();
        Assert.That(ReadMockLog(), Does.Contain("session/new").And.Contain("session/set_mode").And.Contain("session/cancel").And.Contain("session/load"));
        Assert.That(diagnostics.Lines, Does.Contain("Agent worker: mock ACP initialized"));
    }

    [Test]
    public async Task CancellingAcpTurnAlsoCancelsPendingHostPermission()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.PermissionHandler = async (_, token) =>
        {
            requested.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return AgentPermissionDecision.AllowOnce;
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        };

        var session = Session(AgentProviderKind.Acp, "acp");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var turn = Turn(session, "permission pending");
        var running = bridge.RunTurnAsync(turn, _timeout.Token);
        await requested.Task.WaitAsync(_timeout.Token);
        await bridge.CancelTurnAsync(turn.ThreadId, turn.TurnId, _timeout.Token);
        await cancelled.Task.WaitAsync(_timeout.Token);
        Assert.That((await running).Status, Is.EqualTo(AgentTurnStatus.Cancelled));
        Assert.That(ReadMockLog(), Does.Contain("session/cancel"));
    }

    [TestCase(AgentPermissionMode.Supervised)]
    [TestCase(AgentPermissionMode.Auto)]
    public async Task CodexUsesAppServerForApprovalQuestionsInterruptAndResume(AgentPermissionMode mode)
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        bridge.PermissionHandler = (request, _) => Task.FromResult(request.Description == "sample"
            ? AgentPermissionDecision.AllowAlways : AgentPermissionDecision.RejectOnce);
        bridge.QuestionHandler = (_, _) => Task.FromResult<IReadOnlyDictionary<string, string[]>>(
            new Dictionary<string, string[]> { ["choice"] = ["A"], ["secret"] = ["mock-code"] });

        var session = Session(AgentProviderKind.Codex, "codex") with { PermissionMode = mode, ReasoningEffort = "max" };
        var info = await bridge.OpenSessionAsync(session, _timeout.Token);
        var result = await bridge.RunTurnAsync(Turn(session, "exercise"), _timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(info.ExternalSessionId, Is.EqualTo("codex-saved-session"));
            Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed));
            Assert.That(timeline.OfType<AgentAssistantTextDelta>().Single().Text, Is.EqualTo("hello from Codex"));
            Assert.That(timeline.OfType<AgentToolCallUpdated>().Single().Message, Is.EqualTo("out"));
            Assert.That(timeline.OfType<AgentPermissionResolved>().Single().Decision, Is.EqualTo(AgentPermissionDecision.AllowAlways));
            var questions = timeline.OfType<AgentUserQuestionRequested>().Single().Request.Questions;
            Assert.That(questions[0].Text, Is.EqualTo("Choose a value"));
            Assert.That(questions[0].Header, Is.EqualTo("Value"));
            Assert.That(questions[0].IsOther, Is.False);
            Assert.That(questions[0].Options, Is.EqualTo(new AgentQuestionOption[] { new("A", "First"), new("B", "Second") }));
            Assert.That(questions[1].Header, Is.EqualTo("Access"));
            Assert.That(questions[1].IsSecret, Is.True);
            Assert.That(questions[1].IsOther, Is.True);
            Assert.That(questions[1].Options, Is.Null);
            Assert.That(timeline.OfType<AgentUserQuestionResolved>().Single().Answered, Is.True);
        });

        var waiting = WaitForDelta(bridge, "waiting");
        var turn = Turn(session, "wait");
        var running = bridge.RunTurnAsync(turn, _timeout.Token);
        await waiting.WaitAsync(_timeout.Token);
        await bridge.CancelTurnAsync(turn.ThreadId, turn.TurnId, _timeout.Token);
        Assert.That((await running).Status, Is.EqualTo(AgentTurnStatus.Cancelled));
        await bridge.OpenSessionAsync(session with { ExternalSessionId = info.ExternalSessionId }, _timeout.Token);
        await bridge.CloseSessionAsync(session.ThreadId, _timeout.Token);
        Assert.That(ReadMockLog(), Does.Contain("codex initialize").And.Contain("thread/start").And.Contain("turn/interrupt")
            .And.Contain("thread/resume").And.Contain("codex effort=max"));
    }

    [Test]
    public async Task ManagerRunsTwoCodexThreadsConcurrentlyAndPersistsSteeredPrompts()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        await using var runtime = new AgentRuntimeManager(bridge, new AgentThreadStore(_directory.FullName));
        var session = Session(AgentProviderKind.Codex, "codex");
        var provider = AgentProviderDescriptor.Codex with
        {
            Command = session.ExecutablePath, Arguments = session.Arguments, Environment = session.Environment
        };
        string project = Path.Combine(_directory.FullName, "project.beutl");
        var first = await runtime.CreateThreadAsync(project, provider, "First thread", cancellationToken: _timeout.Token);
        var second = await runtime.CreateThreadAsync(project, provider, "Second thread", cancellationToken: _timeout.Token);
        var firstTurn = new AgentTurnRequest(first.Id, Guid.NewGuid(), "wait");
        var secondTurn = new AgentTurnRequest(second.Id, Guid.NewGuid(), "wait");
        var started = new ConcurrentDictionary<Guid, byte>();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.TimelineEventReceived += entry =>
        {
            if (entry is AgentAssistantTextDelta { Text: "waiting" } && started.TryAdd(entry.ThreadId, 0) && started.Count == 2)
                bothStarted.TrySetResult();
        };

        var firstRun = runtime.RunTurnAsync(project, provider, firstTurn, session.McpServers, cancellationToken: _timeout.Token);
        var secondRun = runtime.RunTurnAsync(project, provider, secondTurn, session.McpServers, cancellationToken: _timeout.Token);
        await bothStarted.Task.WaitAsync(_timeout.Token);
        Assert.That(await runtime.SteerTurnAsync(firstTurn with { TurnId = secondTurn.TurnId }, _timeout.Token), Is.False);
        Assert.That(await runtime.SteerTurnAsync(firstTurn with { Text = "Focus on the opening" }, _timeout.Token), Is.True);
        Assert.That((await firstRun).Status, Is.EqualTo(AgentTurnStatus.Completed));
        Assert.That(secondRun.IsCompleted, Is.False);
        Assert.That(await runtime.SteerTurnAsync(secondTurn with { Text = "Focus on the opening" }, _timeout.Token), Is.True);
        Assert.That((await secondRun).Status, Is.EqualTo(AgentTurnStatus.Completed));

        foreach (var turn in new[] { firstTurn, secondTurn })
        {
            var entries = await runtime.LoadTimelineAsync(project, turn.ThreadId, _timeout.Token);
            var users = entries.OfType<AgentUserMessage>().ToArray();
            Assert.That(users.Select(entry => entry.Text), Is.EqualTo(new[] { "wait", "Focus on the opening" }));
            Assert.That(users.Select(entry => entry.TurnId), Is.All.EqualTo(turn.TurnId));
            Assert.That(entries.Last(), Is.TypeOf<AgentTurnCompleted>());
        }
        Assert.That(ReadMockLog().Count(line => line == "turn/steer"), Is.EqualTo(2));
    }

    [Test]
    public async Task ClaudeNormalizesCompactionRetryUsageAndSentAttachments()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        var session = Session(AgentProviderKind.ClaudeCode, "claude");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        AgentUserAttachment[] attachments = [new(Path.Combine(_directory.FullName, "ref.png"), "Reference", "image/png", "PNG", true)];
        var request = Turn(session, "phase3 metrics") with { Attachments = attachments, IncludeSceneContext = false };
        var result = await bridge.RunTurnAsync(request, _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
        var user = timeline.OfType<AgentUserMessage>().Single();
        var completed = timeline.OfType<AgentTurnCompleted>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(user.Attachments, Is.EqualTo(attachments));
            Assert.That(user.IncludeSceneContext, Is.False);
            Assert.That(timeline.OfType<AgentTurnPhaseChanged>().Select(entry => entry.Phase),
                Is.EqualTo(new[] { AgentTurnPhase.Compacting, AgentTurnPhase.Thinking, AgentTurnPhase.Retrying }));
            Assert.That(timeline.OfType<AgentTurnUsageUpdated>().Last().InputTokens, Is.EqualTo(1060));
            Assert.That(timeline.OfType<AgentContextCompacted>(), Has.Exactly(1).Items);
            Assert.That(completed.DurationMs, Is.EqualTo(3420));
            Assert.That(completed.InputTokens, Is.EqualTo(1060));
            Assert.That(completed.OutputTokens, Is.EqualTo(140));
            Assert.That(completed.CachedInputTokens, Is.EqualTo(800));
            Assert.That(completed.Model, Is.EqualTo("claude-opus-test"));
        });
    }

    [Test]
    public async Task CodexNormalizesCompactionRetryAndPerTurnUsageFromCumulativeReports()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        var session = Session(AgentProviderKind.Codex, "codex");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        for (int i = 0; i < 2; i++)
        {
            var request = Turn(session, "phase3 metrics");
            var result = await bridge.RunTurnAsync(request, _timeout.Token);
            Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
            var entries = timeline.Where(entry => entry.TurnId == request.TurnId).ToArray();
            var completed = entries.OfType<AgentTurnCompleted>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(entries.OfType<AgentTurnPhaseChanged>().Select(entry => entry.Phase),
                    Does.Contain(AgentTurnPhase.Compacting).And.Contain(AgentTurnPhase.Thinking).And.Contain(AgentTurnPhase.Retrying));
                Assert.That(entries.OfType<AgentErrorNotice>(), Is.Empty);
                Assert.That(entries.OfType<AgentContextCompacted>(), Has.Exactly(1).Items);
                Assert.That(completed.DurationMs, Is.EqualTo(2470));
                Assert.That(completed.InputTokens, Is.EqualTo(300));
                Assert.That(completed.OutputTokens, Is.EqualTo(100));
                Assert.That(completed.CachedInputTokens, Is.EqualTo(170));
                Assert.That(completed.Model, Is.EqualTo("gpt-6.2-sol"));
                Assert.That(completed.ContextUsage, Is.EqualTo(new AgentContextUsage(160, 250_000, 100, 60, 50)));
                Assert.That(entries.OfType<AgentContextUsageUpdated>().Select(entry => entry.Usage.UsedTokens), Does.Contain(240L));
            });
        }
    }

    [TestCase("phase4 usage", 150_000)]
    [TestCase("phase4 usage fallback", 200_000)]
    public async Task ClaudeMeasuresTheLatestAssistantRequestAndUpdatesTheWindowFromModelUsage(string text, int window)
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var entries = Capture(bridge);
        var session = Session(AgentProviderKind.ClaudeCode, "claude");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var result = await bridge.RunTurnAsync(Turn(session, text), _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
        var completed = entries.OfType<AgentTurnCompleted>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(entries.OfType<AgentContextUsageUpdated>().First().Usage.ContextWindowTokens, Is.EqualTo(200_000));
            Assert.That(completed.ContextUsage, Is.EqualTo(new AgentContextUsage(118_000, window, 113_000, 5_000, 20_000)));
            Assert.That(completed.InputTokens, Is.EqualTo(237_000));
        });

        Guid compactId = Guid.NewGuid();
        Assert.That((await bridge.CompactContextAsync(session.ThreadId, compactId, _timeout.Token)).Status,
            Is.EqualTo(AgentCompactionStatus.Completed));
        Assert.That(entries.OfType<AgentUserMessage>().Count(), Is.EqualTo(1));
        Assert.That(entries.OfType<AgentContextUsageUpdated>().Last().Usage,
            Is.EqualTo(new AgentContextUsage(20_000, 150_000, 18_000, 2_000, 3_000)));
        Assert.That(ReadMockLog(), Does.Contain("claude /compact").And.Contain("claude get_context_usage"));
    }

    [Test]
    public async Task AcpPublishesTheLiveWindowAndReportedTokenBreakdown()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var entries = Capture(bridge);
        var session = Session(AgentProviderKind.Acp, "acp");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var result = await bridge.RunTurnAsync(Turn(session, "phase4 usage"), _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
        Assert.That(entries.OfType<AgentContextUsageUpdated>().First().Usage, Is.EqualTo(new AgentContextUsage(80_000, 100_000)));
        Assert.That(entries.OfType<AgentTurnCompleted>().Single().ContextUsage,
            Is.EqualTo(new AgentContextUsage(80_000, 100_000, 75_000, 5_000, 28_000)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompactsCodexOrHandsOffThenClearsTheExternalSession(bool unsupported)
    {
        var store = new AgentThreadStore(_directory.FullName);
        await using var runtime = new AgentRuntimeManager(new WorkerAgentRuntimeBridge(), store);
        var session = Session(AgentProviderKind.Codex, "codex");
        var environment = new Dictionary<string, string>(session.Environment!);
        if (unsupported)
            environment["BEUTL_MOCK_UNSUPPORTED_COMPACT"] = "1";
        var provider = AgentProviderDescriptor.Codex with
        {
            Command = session.ExecutablePath, Arguments = session.Arguments, Environment = environment
        };
        string project = Path.Combine(_directory.FullName, "project.beutl");
        var ct = _timeout.Token;
        var thread = await runtime.CreateThreadAsync(project, provider, "Cut", cancellationToken: ct);
        var first = await runtime.RunTurnAsync(project, thread.Id, provider, "phase3 metrics", session.McpServers, cancellationToken: ct);
        Assert.That(first.Status, Is.EqualTo(AgentTurnStatus.Completed));
        var compacted = await runtime.CompactContextAsync(project, thread.Id, provider, session.McpServers, cancellationToken: ct);
        var entries = await new AgentThreadStore(_directory.FullName).ReadTimelineAsync(project, thread.Id, ct);
        var marker = entries.OfType<AgentCompactionCompleted>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(entries.OfType<AgentCompactionStarted>().Single().TurnId, Is.EqualTo(marker.TurnId));
            Assert.That(entries.OfType<AgentUserMessage>().Single().Text, Is.EqualTo("phase3 metrics"));
            Assert.That(entries.OfType<AgentTurnCompleted>().Single().ContextUsage?.UsedTokens, Is.EqualTo(160));
            Assert.That(marker.UsedHandoff, Is.EqualTo(unsupported));
            Assert.That(marker.ContextUsage?.UsedTokens, Is.EqualTo(unsupported ? (long?)null : 50));
            Assert.That(compacted.ExternalSessionId, Is.EqualTo(unsupported ? null : "codex-saved-session"));
            Assert.That(ReadMockLog(), Does.Contain("thread/compact/start"));
        });

        if (unsupported)
        {
            await runtime.RunTurnAsync(project, thread.Id, provider, "phase4 handoff before clearing", session.McpServers, cancellationToken: ct);
            Assert.That(ReadMockLog().Single(line => line.StartsWith("handoff=")), Does.Contain("phase3 metrics"));
        }
        var (cleared, divider) = await runtime.ClearContextAsync(project, thread.Id, ct);
        Assert.That(cleared.ExternalSessionId, Is.Null);
        Assert.That(cleared.RestorePreviousMessages, Is.False);
        Assert.That((await store.ReadTimelineAsync(project, thread.Id, ct)).Last(), Is.EqualTo(divider));
        await runtime.RunTurnAsync(project, thread.Id, provider, "phase4 handoff after clearing", session.McpServers, cancellationToken: ct);
        Assert.Multiple(() =>
        {
            Assert.That(ReadMockLog().Last(line => line.StartsWith("handoff=")), Is.EqualTo("handoff=\"phase4 handoff after clearing\""));
            Assert.That(ReadMockLog().Count(line => line == "thread/start"), Is.EqualTo(unsupported ? 3 : 2));
            Assert.That(ReadMockLog(), Does.Not.Contain("thread/resume"));
        });
    }

    [Test]
    public async Task ClaudeStopsTheMappedBackgroundTaskAndFinishesTheTurn()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.TimelineEventReceived += entry =>
        {
            if (entry is AgentToolCallStarted { ToolCallId: "visible-tool-id", IsBackground: true })
                started.TrySetResult();
        };
        var session = Session(AgentProviderKind.ClaudeCode, "claude");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var request = Turn(session, "stop background task");
        var run = bridge.RunTurnAsync(request, _timeout.Token);
        await started.Task.WaitAsync(_timeout.Token);
        Assert.That(await bridge.StopBackgroundTaskAsync(session.ThreadId, request.TurnId, "visible-tool-id", _timeout.Token),
            Is.EqualTo(AgentBackgroundStopResult.Requested));
        Assert.That((await run).Status, Is.EqualTo(AgentTurnStatus.Completed));
        Assert.That(timeline.OfType<AgentToolCallCompleted>().Single(entry => entry.ToolCallId == "visible-tool-id").Status,
            Is.EqualTo(AgentToolStatus.Cancelled));
        Assert.That(ReadMockLog(), Does.Contain("claude stop_task actual-task-id"));
    }

    [Test]
    public async Task ClaudeSteersStreamingInputAndCorrelatesThinkingWithOneReply()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var entries = Capture(bridge);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.TimelineEventReceived += entry =>
        {
            if (entry is AgentReasoningDelta { Text: "Waiting for a steer" })
                waiting.TrySetResult();
        };

        var session = Session(AgentProviderKind.ClaudeCode, "claude");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var turn = Turn(session, "wait to steer");
        var running = bridge.RunTurnAsync(turn, _timeout.Token);
        await waiting.Task.WaitAsync(_timeout.Token);
        var models = await bridge.ListModelsAsync(DiscoveryRequest(AgentProviderKind.ClaudeCode, "claude"), _timeout.Token);
        Assert.That(models.Single().ResolvedModelId, Is.EqualTo("claude-opus-test"));
        Assert.That(await bridge.SteerTurnAsync(turn with { Text = "Keep only the title" }, _timeout.Token), Is.True);
        var result = await running;
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
        var delta = entries.OfType<AgentAssistantTextDelta>().Single();
        var completed = entries.OfType<AgentAssistantMessageCompleted>().Single();
        Assert.That(completed.Text, Is.EqualTo("Single reply after thinking"));
        Assert.That(completed.MessageId, Is.EqualTo(delta.MessageId));
        Assert.That(entries.OfType<AgentUserMessage>().Select(entry => entry.Text), Is.EqualTo(new[] { "wait to steer", "Keep only the title" }));
        Assert.That(entries.OfType<AgentTurnCompleted>().Count(), Is.EqualTo(1));
        Assert.That(ReadMockLog(), Does.Contain("claude steer"));
        await bridge.CloseSessionAsync(session.ThreadId, _timeout.Token);
    }

    [Test]
    public async Task ClaudeUsesExplicitExecutableAndTranslatesSdkStream()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        var hostRequests = new ConcurrentQueue<AgentPermissionRequest>();
        bridge.PermissionHandler = (request, _) =>
        {
            hostRequests.Enqueue(request);
            return Task.FromResult(AgentPermissionDecision.AllowOnce);
        };

        var health = await bridge.PingAsync(_timeout.Token);
        var session = Session(AgentProviderKind.ClaudeCode, "claude") with { ReasoningEffort = "high" };
        await Assert.ThatAsync(async () =>
            await bridge.OpenSessionAsync(session with { ExecutablePath = health.NodeExecutablePath }, _timeout.Token),
            Throws.TypeOf<AgentRpcException>());
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var result = await bridge.RunTurnAsync(Turn(session, "exercise"), _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
        Assert.Multiple(() =>
        {
            var streamed = timeline.OfType<AgentAssistantTextDelta>().ToArray();
            var completed = timeline.OfType<AgentAssistantMessageCompleted>().ToArray();
            Assert.That(streamed.Select(message => message.Text), Is.EqualTo(new[] { "hello from Claude", "more from Claude" }));
            Assert.That(streamed.Select(message => message.MessageId), Is.Unique);
            Assert.That(completed.Select(message => message.MessageId), Is.EqualTo(streamed.Select(message => message.MessageId)));
            Assert.That(timeline.OfType<AgentToolCallCompleted>().Single().Outputs!.Single().Text, Is.EqualTo("created"));
            Assert.That(hostRequests.Single().McpServerName, Is.EqualTo("beutl"));
            Assert.That(timeline.OfType<AgentPermissionResolved>().Single().Decision, Is.EqualTo(AgentPermissionDecision.AllowOnce));
        });
        string? saved = timeline.OfType<AgentSessionStateChanged>().Last().ExternalSessionId;
        Assert.That(saved, Is.Not.Null);

        await bridge.CloseSessionAsync(session.ThreadId, _timeout.Token);
        await bridge.OpenSessionAsync(session with { ExternalSessionId = saved }, _timeout.Token);
        timeline.Clear();
        var thinkingResult = await bridge.RunTurnAsync(Turn(session, "thinking before text"), _timeout.Token);
        Assert.That(thinkingResult.Status, Is.EqualTo(AgentTurnStatus.Completed), thinkingResult.Error);
        Assert.Multiple(() =>
        {
            var streamed = timeline.OfType<AgentAssistantTextDelta>().Single();
            var completed = timeline.OfType<AgentAssistantMessageCompleted>().Single();
            Assert.That(streamed.Text, Is.EqualTo("Single reply after thinking"));
            Assert.That(completed.Text, Is.EqualTo("Single reply after thinking"));
            Assert.That(completed.MessageId, Is.EqualTo(streamed.MessageId));
        });

        await bridge.CloseSessionAsync(session.ThreadId, _timeout.Token);
        await bridge.OpenSessionAsync(session with { ExternalSessionId = saved }, _timeout.Token);
        bridge.PermissionHandler = (_, _) => Task.FromResult(AgentPermissionDecision.RejectAlways);
        var resumed = await bridge.RunTurnAsync(Turn(session, "reject"), _timeout.Token);
        Assert.That(resumed.Status, Is.EqualTo(AgentTurnStatus.Completed), resumed.Error);
        Assert.That(timeline.OfType<AgentToolCallCompleted>().Last().Status, Is.EqualTo(AgentToolStatus.Failed));
        Assert.That(timeline.OfType<AgentPermissionResolved>().Last().Decision, Is.EqualTo(AgentPermissionDecision.RejectAlways));
        Assert.That(ReadMockLog(), Does.Contain("claude initialize").And.Contain($"claude resume={saved}").And.Contain("claude effort=high"));
    }

    [TestCase(AgentPermissionMode.Auto)]
    [TestCase(AgentPermissionMode.FullAccess)]
    public async Task ClaudeRoundsTripsMultiSelectAndReportsSubagentAndBackgroundActivity(AgentPermissionMode mode)
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        bridge.QuestionHandler = (_, _) => Task.FromResult<IReadOnlyDictionary<string, string[]>>(
            new Dictionary<string, string[]> { ["0"] = ["Intro", "Credits", "Interview"], ["1"] = ["City at night"] });
        var session = Session(AgentProviderKind.ClaudeCode, "claude") with { PermissionMode = mode };
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var result = await bridge.RunTurnAsync(Turn(session, "rich interaction"), _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);

        var questions = timeline.OfType<AgentUserQuestionRequested>().Single().Request.Questions;
        Assert.Multiple(() =>
        {
            Assert.That(questions[0].Header, Is.EqualTo("Sections"));
            Assert.That(questions[0].MultiSelect, Is.True);
            Assert.That(questions[0].IsOther, Is.True);
            Assert.That(questions[0].Options![0], Is.EqualTo(new AgentQuestionOption("Intro", "Open with a title")));
            Assert.That(questions[1].MultiSelect, Is.False);
            Assert.That(timeline.OfType<AgentUserQuestionResolved>().Single().Answered, Is.True);
            Assert.That(timeline.OfType<AgentComposerSuggestionsChanged>().Single().Suggestions,
                Does.Contain(new AgentComposerSuggestion("look-chain", null, AgentComposerSuggestionKind.Skill, Source: "claude")));
            Assert.That(timeline.OfType<AgentToolCallStarted>().Single(item => item.ToolCallId == "claude-agent").Subagent,
                Is.EqualTo(new AgentSubagent("scene-reviewer", "Review shots")));
            Assert.That(timeline.OfType<AgentReasoningDelta>().Single().ParentToolCallId, Is.EqualTo("claude-agent"));
            Assert.That(timeline.OfType<AgentAssistantTextDelta>().Single().ParentToolCallId, Is.EqualTo("claude-agent"));
            Assert.That(timeline.OfType<AgentAssistantMessageCompleted>().Single().ParentToolCallId, Is.EqualTo("claude-agent"));
            Assert.That(timeline.OfType<AgentToolCallStarted>().Single(item => item.ToolCallId == "claude-read").ParentToolCallId, Is.EqualTo("claude-agent"));
            Assert.That(timeline.OfType<AgentToolCallUpdated>().Single(item => item.ToolCallId == "claude-read").ParentToolCallId, Is.EqualTo("claude-agent"));
            Assert.That(timeline.OfType<AgentToolCallCompleted>().Single(item => item.ToolCallId == "claude-read").ParentToolCallId, Is.EqualTo("claude-agent"));
            var background = timeline.OfType<AgentToolCallStarted>().Single(item => item.IsBackground);
            Assert.That(background.BackgroundTitle, Is.EqualTo("render-preview"));
            Assert.That(timeline.OfType<AgentToolCallUpdated>().Single(item => item.ToolCallId == background.ToolCallId).Message, Is.EqualTo("Rendering frames"));
            Assert.That(timeline.OfType<AgentToolCallCompleted>().Single(item => item.ToolCallId == background.ToolCallId).Outputs!.Single().Text,
                Is.EqualTo("Rendered 12 frames"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CodexRendersCollaborationAndInvokesSkillsWhenSupported(bool unsupportedSkills)
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        var session = Session(AgentProviderKind.Codex, "codex") with
        {
            Environment = new Dictionary<string, string> { ["BEUTL_MOCK_UNSUPPORTED_SKILLS"] = unsupportedSkills ? "1" : "0" }
        };
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var suggestions = await bridge.ListComposerSuggestionsAsync(new(session.Provider, session.ExecutablePath, session.WorkingDirectory,
            session.Arguments, session.Environment, session.ThreadId), _timeout.Token);
        Assert.That(suggestions, Has.Count.EqualTo(unsupportedSkills ? 0 : 1));
        if (!unsupportedSkills)
            Assert.That(suggestions.Single().Name, Is.EqualTo("editing-notes"));
        var result = await bridge.RunTurnAsync(Turn(session, "$editing-notes inspect the scene"), _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
        Assert.Multiple(() =>
        {
            Assert.That(timeline.OfType<AgentToolCallStarted>().First(item => item.ToolCallId == "collab-spawn").Subagent,
                Is.EqualTo(new AgentSubagent("reviewer", "Review the video")));
            Assert.That(timeline.OfType<AgentToolCallStarted>().First(item => item.IsBackground).ParentToolCallId, Is.EqualTo("collab-spawn"));
            Assert.That(timeline.OfType<AgentToolCallUpdated>().Single(item => item.ToolCallId == "child-command").ParentToolCallId, Is.EqualTo("collab-spawn"));
            Assert.That(timeline.OfType<AgentReasoningDelta>().Single().ParentToolCallId, Is.EqualTo("collab-spawn"));
            Assert.That(timeline.OfType<AgentAssistantMessageCompleted>().Select(item => (item.Text, item.ParentToolCallId)),
                Is.EqualTo(new[] { ("Subagent output", "collab-spawn"), ("Main output", (string?)null) }));
            Assert.That(timeline.OfType<AgentToolCallCompleted>().Single(item => item.ToolCallId == "collab-wait").ParentToolCallId, Is.EqualTo("collab-spawn"));
            Assert.That(timeline.OfType<AgentToolCallCompleted>().Single(item => item.ToolCallId == "collab-spawn").Outputs!.Single().Text,
                Is.EqualTo("Review finished"));
        });
    }

    [Test]
    public async Task ProvidersDiscoverCommandsAndSkillsWithoutSendingAPrompt()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var claude = await bridge.ListComposerSuggestionsAsync(SuggestionRequest(AgentProviderKind.ClaudeCode, "claude"), _timeout.Token);
        var codex = await bridge.ListComposerSuggestionsAsync(SuggestionRequest(AgentProviderKind.Codex, "codex"), _timeout.Token);
        var acp = await bridge.ListComposerSuggestionsAsync(SuggestionRequest(AgentProviderKind.Acp, "acp"), _timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(claude, Is.EquivalentTo(new AgentComposerSuggestion[]
            {
                new("compact", "Summarize the conversation", AgentComposerSuggestionKind.Command, "<focus>", "claude"),
                new("shot-review", "Review the timeline shots", AgentComposerSuggestionKind.Skill, "<scene>", "claude")
            }));
            Assert.That(codex.Single(), Is.EqualTo(new AgentComposerSuggestion("editing-notes", "Follow the scene editing guide",
                AgentComposerSuggestionKind.Skill, Source: "repo")));
            Assert.That(acp.Single(), Is.EqualTo(new AgentComposerSuggestion("build-plan", "Draft an edit plan",
                AgentComposerSuggestionKind.Command, "<scene>", "acp")));
            Assert.That(ReadMockLog(), Does.Contain("skills/list").And.Not.Contain("thread/start").And.Not.Contain("claude prompt"));
        });
    }

    [Test]
    public async Task AcpPreservesToolMetadataAndPublishesCommandUpdates()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var timeline = Capture(bridge);
        var session = Session(AgentProviderKind.Acp, "acp");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var result = await bridge.RunTurnAsync(Turn(session, "metadata"), _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);
        Assert.Multiple(() =>
        {
            var command = timeline.OfType<AgentToolCallStarted>().Single(item => item.IsBackground);
            Assert.That(command.BackgroundTitle, Is.EqualTo("render-preview"));
            Assert.That(command.ParentToolCallId, Is.EqualTo("coordinator"));
            Assert.That(timeline.OfType<AgentToolCallCompleted>().Single().ParentToolCallId, Is.EqualTo("coordinator"));
            Assert.That(timeline.OfType<AgentComposerSuggestionsChanged>().Select(item => item.Suggestions.Single().Name),
                Is.EqualTo(new[] { "build-plan", "render" }));
        });
    }

    [Test]
    public async Task CodexAndClaudeDiscoverModelsWithoutSendingAPrompt()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var codex = await bridge.ListModelsAsync(DiscoveryRequest(AgentProviderKind.Codex, "codex"), _timeout.Token);
        var claude = await bridge.ListModelsAsync(DiscoveryRequest(AgentProviderKind.ClaudeCode, "claude"), _timeout.Token);

        Assert.That(codex.Select(model => model.Id), Is.EqualTo(new[] { "gpt-6.2-sol", "gpt-6.2-astra" }));
        Assert.That(codex[0].ShortName, Is.EqualTo("6.2 Sol"));
        Assert.That(codex[0].DefaultReasoningEffort, Is.EqualTo("low"));
        Assert.That(codex[0].ReasoningEfforts, Is.EqualTo(new[] { "low", "max" }));
        Assert.That(codex[0].IsProviderDefault, Is.True);
        Assert.That(claude.Single().ResolvedModelId, Is.EqualTo("claude-opus-test"));
        Assert.That(claude[0].DisplayName, Is.EqualTo("Claude Opus Test"));
        Assert.That(claude[0].ReasoningEfforts, Is.EqualTo(new[] { "low", "high" }));
        Assert.That(ReadMockLog(), Does.Not.Contain("thread/start").And.Not.Contain("claude prompt"));
    }

    [Test]
    public async Task AcpDiscoversGroupedOptionsAndAppliesModelAndEffortOnTheExistingSession()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var models = await bridge.ListModelsAsync(DiscoveryRequest(AgentProviderKind.Acp, "acp"), _timeout.Token);
        Assert.That(models.Select(model => model.Id), Is.EqualTo(new[] { "draft", "cinema" }));
        Assert.That(models[0].IsProviderDefault, Is.True);
        Assert.That(models[1].ReasoningEfforts, Is.EqualTo(new[] { "fast", "deep" }));

        var session = Session(AgentProviderKind.Acp, "acp") with { Model = "cinema", ReasoningEffort = "deep" };
        var opened = await bridge.OpenSessionAsync(session, _timeout.Token);
        await bridge.OpenSessionAsync(session with { ExternalSessionId = opened.ExternalSessionId, ReasoningEffort = "fast" }, _timeout.Token);
        var log = ReadMockLog();
        Assert.That(log, Does.Contain("model-id=cinema").And.Contain("effort-id=deep").And.Contain("effort-id=fast"));
        Assert.That(log.Count(line => line == "session/new"), Is.EqualTo(2));
        Assert.That(log, Does.Not.Contain("session/load"));
    }

    [Test]
    public async Task CrashFailsActiveTurnReapsDescendantsAndNextPingRestarts()
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var disconnected = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.Disconnected += error => disconnected.TrySetResult(error);
        var health = await bridge.PingAsync(_timeout.Token);
        using var process = Process.GetProcessById(health.ProcessId);
        var session = Session(AgentProviderKind.Acp, "acp");
        await bridge.OpenSessionAsync(session, _timeout.Token);
        var waiting = WaitForDelta(bridge, "waiting");
        var running = bridge.RunTurnAsync(Turn(session, "crash"), _timeout.Token);
        await waiting.WaitAsync(_timeout.Token);
        using var provider = ReadProcess("provider");
        using var descendant = ReadProcess("descendant");

        process.Kill();
        await Assert.ThatAsync(async () => await running, Throws.TypeOf<AgentWorkerDisconnectedException>());
        Assert.That(await disconnected.Task.WaitAsync(_timeout.Token), Is.TypeOf<AgentWorkerDisconnectedException>());
        await Task.WhenAll(provider.WaitForExitAsync(_timeout.Token), descendant.WaitForExitAsync(_timeout.Token));
        Assert.That((await bridge.PingAsync(_timeout.Token)).ProcessId, Is.Not.EqualTo(health.ProcessId));
    }

    [TestCase(AgentProviderKind.Acp, "acp")]
    [TestCase(AgentProviderKind.Codex, "codex")]
    [TestCase(AgentProviderKind.ClaudeCode, "claude")]
    public async Task FormatsStructuredMentionsAndPastedTextForTheProvider(AgentProviderKind provider, string mockMode)
    {
        await using var bridge = new WorkerAgentRuntimeBridge();
        var session = Session(provider, mockMode);
        await bridge.OpenSessionAsync(session, _timeout.Token);
        string project = Path.Combine(_directory.FullName, "demo.bproj");
        string clip = Path.Combine(_directory.FullName, "media", "clip.mp4");
        string pastedText = Path.Combine(_directory.FullName, "pasted-text.txt");
        string sceneId = Guid.NewGuid().ToString("D");
        var mentions = new AgentContextMention[]
        {
            new(AgentContextMentionKind.Scene, "Opening", sceneId, Detail: "1920 × 1080 · 24 fps · 6 s", Token: "@Opening"),
            new(AgentContextMentionKind.Element, "Title", Guid.NewGuid().ToString("D"),
                Detail: "0.5–2.25 s · Layer 2", Token: "@Title", SceneId: sceneId),
            new(AgentContextMentionKind.File, "clip.mp4", Path: clip, Detail: "media/clip.mp4", Token: "@clip.mp4")
        };
        var context = new AgentContextSnapshot("instance", project, "Demo", null, null, null, null, null, null, null,
            [], [new(pastedText, "text/plain")], mentions);
        var request = Turn(session, "Review @Opening @Title @clip.mp4") with { Context = context };
        var result = await bridge.RunTurnAsync(request, _timeout.Token);
        Assert.That(result.Status, Is.EqualTo(AgentTurnStatus.Completed), result.Error);

        const string prefix = "mention-prompt=";
        string line = ReadMockLog().Single(entry => entry.StartsWith(prefix, StringComparison.Ordinal));
        string prompt = JsonSerializer.Deserialize<string>(line[prefix.Length..])!;
        using var payload = JsonDocument.Parse(prompt[(prompt.LastIndexOf('\n') + 1)..]);
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("[Beutl project context]").And.Contain("Resolve @tokens using mentions")
                .And.Contain("Read the text/plain files in attachedFiles"));
            Assert.That(AgentJson.Read<AgentContextSnapshot>(payload.RootElement).Mentions, Is.EqualTo(mentions));
            Assert.That(payload.RootElement.GetProperty("mentions")[1].GetProperty("kind").GetString(), Is.EqualTo("element"));
            Assert.That(payload.RootElement.GetProperty("attachedFiles")[0].GetProperty("path").GetString(), Is.EqualTo(pastedText));
        });
    }

    private AgentSessionOpenRequest Session(AgentProviderKind provider, string mockMode)
    {
        string node = AgentBinaryLocator.Resolve("node", Environment.GetEnvironmentVariable("PATH"))
            ?? throw new InvalidOperationException("The mock agent requires Node.js on PATH.");
        string fixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "MockAgent.mjs");
        return new AgentSessionOpenRequest(
            Guid.NewGuid(), provider, node, _directory.FullName, AgentPermissionMode.Supervised,
            [
                new StdioMcpServerSpec("beutl", "tool-host", ["--stdio"], new Dictionary<string, string> { ["PROFILE"] = "test" }),
                new HttpMcpServerSpec("external", new Uri("https://example.test/mcp"),
                    new Dictionary<string, string> { ["Authorization"] = "test-token" })
            ],
            [fixture, mockMode], new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = Path.Combine(_directory.FullName, "claude-config") });
    }

    private static AgentTurnRequest Turn(AgentSessionOpenRequest session, string text) => new(session.ThreadId, Guid.NewGuid(), text);

    private AgentModelListRequest DiscoveryRequest(AgentProviderKind kind, string mockMode)
    {
        var session = Session(kind, mockMode);
        return new AgentModelListRequest(mockMode, kind, session.ExecutablePath, session.WorkingDirectory, session.Arguments,
            new Dictionary<string, string>(session.Environment!) { ["BEUTL_MOCK_MODEL_DISCOVERY"] = "1" });
    }

    private AgentComposerSuggestionRequest SuggestionRequest(AgentProviderKind kind, string mockMode)
    {
        var request = DiscoveryRequest(kind, mockMode);
        return new AgentComposerSuggestionRequest(kind, request.ExecutablePath, request.WorkingDirectory, request.Arguments, request.Environment);
    }

    private static ConcurrentQueue<AgentTimelineEvent> Capture(IAgentRuntimeBridge bridge)
    {
        var entries = new ConcurrentQueue<AgentTimelineEvent>();
        bridge.TimelineEventReceived += entries.Enqueue;
        return entries;
    }

    private static Task WaitForDelta(IAgentRuntimeBridge bridge, string text)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.TimelineEventReceived += OnEvent;
        return completion.Task;

        void OnEvent(AgentTimelineEvent entry)
        {
            if (entry is AgentAssistantTextDelta delta && delta.Text == text)
            {
                bridge.TimelineEventReceived -= OnEvent;
                completion.TrySetResult();
            }
        }
    }

    private string[] ReadMockLog() => File.ReadAllLines(Path.Combine(_directory.FullName, "mock-agent.log"));

    private Process ReadProcess(string name)
    {
        string entry = ReadMockLog().Single(line => line.StartsWith(name + "=", StringComparison.Ordinal));
        return Process.GetProcessById(int.Parse(entry.AsSpan(name.Length + 1)));
    }

    private sealed class WorkerDiagnostics : ILogger
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Enqueue(formatter(state, exception));
    }
}
