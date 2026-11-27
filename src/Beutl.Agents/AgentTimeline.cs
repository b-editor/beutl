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
    string? ReasoningEffort = null);

public enum AgentTurnStatus { Running, Completed, Failed, Cancelled }
public enum AgentSessionState { Open, Closed }
public enum AgentToolStatus { Pending, Running, Completed, Failed, Cancelled }
public enum AgentPlanStepStatus { Pending, InProgress, Completed }

public sealed record AgentTurnSummary(Guid TurnId, AgentTurnStatus Status, string? Error = null);
public sealed record AgentPlanStep(string Text, AgentPlanStepStatus Status);

public sealed record AgentToolOutput(
    string Type, string? Text = null, string? MimeType = null, string? Data = null, string? Uri = null);

public sealed record AgentSubagent(string? Role = null, string? Description = null);

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
[JsonDerivedType(typeof(AgentTurnCompleted), "turnCompleted")]
[JsonDerivedType(typeof(AgentTurnFailed), "turnFailed")]
[JsonDerivedType(typeof(AgentTurnCancelled), "turnCancelled")]
[JsonDerivedType(typeof(AgentTurnHistoryRecorded), "turnHistoryRecorded")]
[JsonDerivedType(typeof(AgentSessionStateChanged), "sessionStateChanged")]
[JsonDerivedType(typeof(AgentErrorNotice), "errorNotice")]
public abstract record AgentTimelineEvent(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp);

public sealed record AgentUserMessage(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Text)
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
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string RequestId, AgentPermissionDecision Decision)
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

public sealed record AgentTurnCompleted(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnFailed(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Error)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnCancelled(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentTurnHistoryRecorded(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, int TransactionCount)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentSessionStateChanged(
    Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, AgentSessionState State, string? ExternalSessionId = null)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);

public sealed record AgentErrorNotice(Guid ThreadId, Guid? TurnId, DateTimeOffset Timestamp, string Message)
    : AgentTimelineEvent(ThreadId, TurnId, Timestamp);
