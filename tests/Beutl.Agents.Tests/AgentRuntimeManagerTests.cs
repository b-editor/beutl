using Beutl.Agents;

namespace Beutl.Agents.Tests;

public sealed class AgentRuntimeManagerTests
{
    [Test]
    public async Task PermissionModesRouteSupervisedAndFullAccess()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-policy-");
        try
        {
            var bridge = new PermissionBridge();
            var store = new AgentThreadStore(directory.FullName);
            await using var manager = new AgentRuntimeManager(bridge, store);
            var seen = new List<string>();
            manager.PermissionHandler = (request, _) =>
            {
                seen.Add(request.ToolName);
                return Task.FromResult(AgentPermissionDecision.RejectOnce);
            };
            var descriptor = AgentProviderDescriptor.Codex with { Command = Environment.ProcessPath! };
            string project = Path.Combine(directory.FullName, "project.beutl");
            var thread = await manager.CreateThreadAsync(project, descriptor, "Thread", AgentPermissionMode.Supervised);

            await manager.RunTurnAsync(project, thread.Id, descriptor, "Edit the timeline", [new StdioMcpServerSpec("beutl", "beutl-mcp")]);
            Assert.Multiple(() =>
            {
                Assert.That(bridge.Decisions, Is.EqualTo(new[]
                {
                    AgentPermissionDecision.RejectOnce, AgentPermissionDecision.RejectOnce, AgentPermissionDecision.RejectOnce
                }));
                Assert.That(seen, Is.EqualTo(new[] { "Beutl write", "External write", "mcp__beutl__lookalike" }));
            });
            Assert.That((await store.GetAsync(project, thread.Id))!.ExternalSessionId, Is.EqualTo("saved-id"));
            Assert.That((await manager.LoadTimelineAsync(project, thread.Id)).Last(), Is.TypeOf<AgentTurnCompleted>());

            seen.Clear();
            await manager.SetPermissionModeAsync(project, thread.Id, AgentPermissionMode.FullAccess);
            await manager.RunTurnAsync(project, thread.Id, descriptor, "Run from prior session", []);
            Assert.Multiple(() =>
            {
                Assert.That(bridge.LastOpen!.ExternalSessionId, Is.EqualTo("saved-id"));
                Assert.That(seen, Is.Empty);
                Assert.That(bridge.Decisions, Has.All.EqualTo(AgentPermissionDecision.AllowOnce));
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class PermissionBridge : IAgentRuntimeBridge
    {
        public event Action<AgentTimelineEvent>? TimelineEventReceived;
        public event Action<Exception>? Disconnected { add { } remove { } }

        public Func<AgentPermissionRequest, CancellationToken, Task<AgentPermissionDecision>>? PermissionHandler { get; set; }
        public Func<AgentUserQuestionRequest, CancellationToken, Task<IReadOnlyDictionary<string, string[]>>>? QuestionHandler { get; set; }
        public List<AgentPermissionDecision> Decisions { get; } = [];
        public AgentSessionOpenRequest? LastOpen { get; private set; }

        public Task<AgentWorkerHealth> PingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentSessionInfo> OpenSessionAsync(AgentSessionOpenRequest request, CancellationToken cancellationToken = default)
        {
            LastOpen = request;
            return Task.FromResult(new AgentSessionInfo(request.ExternalSessionId));
        }

        public Task<IReadOnlyList<AgentModelDescriptor>> ListModelsAsync(AgentModelListRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentModelDescriptor>>([]);

        public Task<IReadOnlyList<AgentComposerSuggestion>> ListComposerSuggestionsAsync(
            AgentComposerSuggestionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentComposerSuggestion>>([]);

        public async Task<AgentTurnSummary> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            var (threadId, turnId, text, _) = request;
            TimelineEventReceived?.Invoke(new AgentUserMessage(threadId, turnId, DateTimeOffset.UtcNow, text));
            TimelineEventReceived?.Invoke(new AgentSessionStateChanged(threadId, turnId, DateTimeOffset.UtcNow, AgentSessionState.Open, "saved-id"));
            Decisions.Clear();
            foreach (var (name, server) in new[] { ("Beutl write", "beutl"), ("External write", "beutl-other"), ("mcp__beutl__lookalike", null) })
            {
                Decisions.Add(await PermissionHandler!(new AgentPermissionRequest(name, threadId, turnId, name, server), cancellationToken));
            }
            TimelineEventReceived?.Invoke(new AgentTurnCompleted(threadId, turnId, DateTimeOffset.UtcNow));
            return new AgentTurnSummary(turnId, AgentTurnStatus.Completed);
        }

        public Task<bool> SteerTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task CancelTurnAsync(Guid threadId, Guid turnId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPermissionModeAsync(Guid threadId, AgentPermissionMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseSessionAsync(Guid threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
