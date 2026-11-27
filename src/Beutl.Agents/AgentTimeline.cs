using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Agents;

public sealed record AgentThreadMetadata(
    Guid Id,
    string ProviderId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AgentPermissionMode PermissionMode,
    string? Model = null,
    string? ExternalSessionId = null,
    string? ReasoningEffort = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool RestorePreviousMessages = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsPinned = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsArchived = false,
    DateTimeOffset? LastReadAt = null,
    DateTimeOffset? LastActivityAt = null)
{
    [JsonIgnore]
    public bool IsUnread => LastActivityAt is { } activity && (LastReadAt is null || activity > LastReadAt);
}

public sealed record AgentThreadSearchHit(AgentThreadMetadata Thread, string? MatchSnippet);

public sealed record AgentContextUsage(
    long? UsedTokens = null, long? ContextWindowTokens = null,
    long? InputTokens = null, long? OutputTokens = null, long? CachedInputTokens = null);

public enum AgentTurnStatus { Running, Completed, Failed, Cancelled }
public enum AgentTurnPhase { Thinking, Responding, RunningTool, Compacting, Retrying }
public enum AgentSessionState { Open, Closed }
public enum AgentToolStatus { Pending, Running, Completed, Failed, Cancelled }
public enum AgentPlanStepStatus { Pending, InProgress, Completed }

public sealed record AgentTurnSummary(Guid TurnId, AgentTurnStatus Status, string? Error = null);
public sealed record AgentPlanStep(string Text, AgentPlanStepStatus Status);

public sealed record AgentToolOutput(
    string Type, string? Text = null, string? MimeType = null, string? Data = null, string? Uri = null);

public sealed record AgentSubagent(string? Role = null, string? Description = null);

public sealed record AgentUserAttachment(string Path, string Label, string? MediaType, string? Detail, bool IsImage);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AgentUserMessage), "userMessage")]
[JsonDerivedType(typeof(AgentAssistantTextDelta), "assistantTextDelta")]
[JsonDerivedType(typeof(AgentAssistantMessageCompleted), "assistantMessageCompleted")]
[JsonDerivedType(typeof(AgentReasoningDelta), "reasoningDelta")]
[JsonDerivedType(typeof(AgentToolCallStarted), "toolCallStarted")]
[JsonDerivedType(typeof(AgentToolCallUpdated), "toolCallUpdated")]
[JsonDerivedType(typeof(AgentToolCallCompleted), "toolCallCompleted")]
[JsonDerivedType(typeof(AgentPlanUpdated), "planUpdated")]
[JsonDerivedType(typeof(AgentPermissionRequested), "permissionRequested")]
[JsonDerivedType(typeof(AgentPermissionResolved), "permissionResolved")]
[JsonDerivedType(typeof(AgentUserQuestionRequested), "userQuestionRequested")]
[JsonDerivedType(typeof(AgentUserQuestionResolved), "userQuestionResolved")]
[JsonDerivedType(typeof(AgentComposerSuggestionsChanged), "composerSuggestionsChanged")]
[JsonDerivedType(typeof(AgentTurnStarted), "turnStarted")]
[JsonDerivedType(typeof(AgentTurnPhaseChanged), "turnPhaseChanged")]
[JsonDerivedType(typeof(AgentTurnUsageUpdated), "turnUsageUpdated")]
[JsonDerivedType(typeof(AgentContextUsageUpdated), "contextUsageUpdated")]
[JsonDerivedType(typeof(AgentContextCompacted), "contextCompacted")]
[JsonDerivedType(typeof(AgentCompactionStarted), "compactionStarted")]
[JsonDerivedType(typeof(AgentCompactionCompleted), "compactionCompleted")]
[JsonDerivedType(typeof(AgentContextCleared), "contextCleared")]
[JsonDerivedType(typeof(AgentThreadModelChanged), "threadModelChanged")]
[JsonDerivedType(typeof(AgentThreadProviderChanged), "threadProviderChanged")]
[JsonDerivedType(typeof(AgentTurnCompleted), "turnCompleted")]
[JsonDerivedType(typeof(AgentTurnFailed), "turnFailed")]
[JsonDerivedType(typeof(AgentTurnCancelled), "turnCancelled")]
[JsonDerivedType(typeof(AgentTurnHistoryRecorded), "turnHistoryRecorded")]
[JsonDerivedType(typeof(AgentSessionStateChanged), "sessionStateChanged")]
[JsonDerivedType(typeof(AgentErrorNotice), "errorNotice")]
public abstract record AgentTimelineEvent(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp);

public sealed record AgentUserMessage(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Text, IReadOnlyList<AgentUserAttachment>? Attachments = null,
    bool? IncludeSceneContext = null, IReadOnlyList<AgentContextMention>? Mentions = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentAssistantTextDelta(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Text, string? MessageId = null, string? ParentToolCallId = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentAssistantMessageCompleted(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Text, string? MessageId = null, string? ParentToolCallId = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentReasoningDelta(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Text, string? ParentToolCallId = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentToolCallStarted(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string ToolCallId, string ToolName,
    string? McpServerName = null, JsonElement? Input = null, string? ParentToolCallId = null, AgentSubagent? Subagent = null,
    bool IsBackground = false, string? BackgroundTitle = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentToolCallUpdated(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string ToolCallId, AgentToolStatus Status, string? Message = null,
    string? ParentToolCallId = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentToolCallCompleted(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string ToolCallId, AgentToolStatus Status,
    IReadOnlyList<AgentToolOutput>? Outputs = null, string? ParentToolCallId = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentPlanUpdated(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, IReadOnlyList<AgentPlanStep> Steps, string? Explanation = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentPermissionRequested(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, AgentPermissionRequest Request)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentPermissionResolved(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string RequestId, AgentPermissionDecision Decision,
    bool? WasPresented = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentUserQuestionRequested(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, AgentUserQuestionRequest Request)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentUserQuestionResolved(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string RequestId, bool Answered)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentComposerSuggestionsChanged(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, IReadOnlyList<AgentComposerSuggestion> Suggestions)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnStarted(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnPhaseChanged(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, AgentTurnPhase Phase, string? ToolName = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnUsageUpdated(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp,
    long? InputTokens = null, long? OutputTokens = null, long? CachedInputTokens = null, string? Model = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentContextCompacted(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentContextUsageUpdated(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, AgentContextUsage Usage)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentCompactionStarted(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentCompactionCompleted(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, bool UsedHandoff = false, bool Succeeded = true,
    AgentContextUsage? ContextUsage = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentContextCleared(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentThreadModelChanged(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string ProviderId,
    string? PreviousModel, string? Model, string? PreviousEffort, string? Effort)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentThreadProviderChanged(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string PreviousProviderId, string ProviderId)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnCompleted(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, long? DurationMs = null,
    long? InputTokens = null, long? OutputTokens = null, long? CachedInputTokens = null, string? Model = null,
    AgentContextUsage? ContextUsage = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnFailed(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Error)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnCancelled(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnHistoryRecorded(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, int TransactionCount, IReadOnlyList<string>? Changes = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentSessionStateChanged(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, AgentSessionState State, string? ExternalSessionId = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentErrorNotice(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Message)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);
