namespace Beutl.Agents;

public sealed record AgentWorkerHealth(int ProcessId, string NodeVersion, string NodeExecutablePath);

public sealed record AgentSessionOpenRequest(
    Guid ThreadId,
    AgentProviderKind Provider,
    string ExecutablePath,
    string WorkingDirectory,
    AgentPermissionMode PermissionMode,
    IReadOnlyList<McpServerSpec> McpServers,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<AgentPermissionMode, string>? PermissionModes = null,
    string? Model = null,
    string? ExternalSessionId = null,
    string? ReasoningEffort = null);

public sealed record AgentSessionInfo(string? ExternalSessionId);
public enum AgentBackgroundStopResult { Unavailable, Requested, Unsupported }
public enum AgentCompactionStatus { Completed, Unsupported, Failed, Cancelled }
public sealed record AgentCompactionResult(AgentCompactionStatus Status, string? Error = null);

public sealed record AgentSkillInstruction(string Name, string Instructions);
public sealed record AgentConversationMessage(string Role, string Text);

public sealed record AgentTurnRequest(Guid ThreadId, Guid TurnId, string Text, AgentContextSnapshot? Context = null)
{
    public IReadOnlyList<AgentSkillInstruction>? BundledSkills { get; init; }
    public IReadOnlyList<string>? ProviderSkills { get; init; }
    public IReadOnlyList<AgentUserAttachment>? Attachments { get; init; }
    public bool? IncludeSceneContext { get; init; }
    public IReadOnlyList<AgentContextMention>? Mentions { get; init; }
    public IReadOnlyList<AgentConversationMessage>? PreviousMessages { get; init; }
}

public interface IAgentRuntimeBridge : IAsyncDisposable
{
    event Action<AgentTimelineEvent>? TimelineEventReceived;
    event Action<Exception>? Disconnected;
    Func<AgentPermissionRequest, CancellationToken, Task<AgentPermissionDecision>>? PermissionHandler { get; set; }
    Func<AgentUserQuestionRequest, CancellationToken, Task<IReadOnlyDictionary<string, string[]>>>? QuestionHandler { get; set; }

    Task<AgentWorkerHealth> PingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentModelDescriptor>> ListModelsAsync(AgentModelListRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentComposerSuggestion>> ListComposerSuggestionsAsync(
        AgentComposerSuggestionRequest request, CancellationToken cancellationToken = default);
    Task<AgentSessionInfo> OpenSessionAsync(AgentSessionOpenRequest request, CancellationToken cancellationToken = default);
    Task<AgentTurnSummary> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);
    Task<AgentCompactionResult> CompactContextAsync(Guid threadId, Guid turnId, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentCompactionResult(AgentCompactionStatus.Unsupported));
    Task<bool> SteerTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);
    Task<AgentBackgroundStopResult> StopBackgroundTaskAsync(
        Guid threadId, Guid turnId, string toolCallId, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentBackgroundStopResult.Unsupported);
    Task CancelTurnAsync(Guid threadId, Guid turnId, CancellationToken cancellationToken = default);
    Task SetPermissionModeAsync(Guid threadId, AgentPermissionMode mode, CancellationToken cancellationToken = default);
    Task CloseSessionAsync(Guid threadId, CancellationToken cancellationToken = default);
}
