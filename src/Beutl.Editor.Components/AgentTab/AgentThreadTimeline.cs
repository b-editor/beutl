using System.Collections.ObjectModel;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab;

internal sealed partial class AgentThreadTimeline : AgentBindable
{
    private readonly ObservableCollection<AgentTimelineItemViewModel> _timeline = [];
    private readonly Dictionary<(Guid? TurnId, string? ParentId, string? MessageId), AgentAssistantItem> _messages = new();
    private readonly Dictionary<(Guid? TurnId, string ToolCallId), AgentToolItem> _tools = new();
    private readonly Dictionary<(Guid? TurnId, string ToolCallId), List<AgentToolItem>> _waitingTools = new();
    private readonly Dictionary<Guid, AgentPlanItem> _plans = new();
    private readonly Dictionary<Guid, AgentTurnStatsItem> _turnHeaders = new();
    private readonly Dictionary<Guid, AgentAssistantItem> _replyCandidates = new();
    private readonly Func<AgentToolItem, Task>? _stopWork;
    private Guid? _stopWorkTurn;

    public AgentThreadTimeline(Func<AgentToolItem, Task>? stopWork = null)
    {
        _stopWork = stopWork;
        Items = new(_timeline);
    }

    public ReadOnlyObservableCollection<AgentTimelineItemViewModel> Items { get; }
    public event Action? TurnLayoutChanged;
    public AgentContextUsage? ContextUsage { get; private set => Set(ref field, value); }

    public void ResetContextUsage(bool keepWindow = true)
    {
        ContextUsage = new AgentContextUsage(0, keepWindow ? ContextUsage?.ContextWindowTokens : null, 0, 0, 0);
    }

    public void AddInteraction(AgentInteractionItem item) => _timeline.Add(item);

    public void SetStoppableTurn(Guid? turnId)
    {
        if (_stopWorkTurn == turnId)
            return;
        foreach (var tool in _tools.Values)
        {
            if (tool.TurnId == _stopWorkTurn || tool.TurnId == turnId)
                tool.StopAction = turnId.HasValue && tool.TurnId == turnId ? _stopWork : null;
        }
        _stopWorkTurn = turnId;
    }

    public void Restore(IReadOnlyList<AgentTimelineEvent> entries)
    {
        Clear();
        var permissions = new Dictionary<string, AgentPermissionRequest>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case AgentPermissionRequested permission:
                    permissions[permission.Request.RequestId] = permission.Request;
                    ClearReplyCandidate(permission);
                    break;
                case AgentPermissionResolved resolution:
                    if (permissions.Remove(resolution.RequestId, out var request) && resolution.WasPresented != false)
                    {
                        var item = new AgentPermissionItem(request);
                        item.Respond(resolution.Decision);
                        _timeline.Add(item);
                    }
                    break;
                case AgentUserQuestionResolved { TurnId: { } turnId } question:
                    _timeline.Add(new AgentQuestionItem(turnId, question.Answered));
                    break;
                default:
                    ApplyEntry(entry);
                    break;
            }
        }
        if (_activityTurn is { } unfinished && entries.LastOrDefault(entry => entry.TurnId == unfinished) is { } last)
        {
            ClearReplyCandidate(last);
            FinishActivity(last);
        }
    }

    public void Clear()
    {
        ResetBackgroundWork(null);
        ClearActivity();
        foreach (var tool in _tools.Values)
            tool.Dispose();
        foreach (var user in _timeline.OfType<AgentUserItem>())
            user.Dispose();
        _tools.Clear();
        _waitingTools.Clear();
        _messages.Clear();
        _plans.Clear();
        _turnHeaders.Clear();
        _replyCandidates.Clear();
        ContextUsage = null;
        _timeline.Clear();
    }

    public void ApplyEntry(AgentTimelineEvent entry)
    {
        // Approval and question cards come from runtime callbacks. The persisted stream also includes automatically decided requests.
        switch (entry)
        {
            case AgentTurnStarted turnStarted:
                ResetBackgroundWork(turnStarted.TurnId);
                StartActivity(turnStarted);
                break;
            case AgentUserMessage user:
                ResetBackgroundWork(user.TurnId);
                StartActivity(user);
                _timeline.Add(new AgentUserItem(user));
                if (user.TurnId is { } turnId && !_turnHeaders.ContainsKey(turnId))
                {
                    var header = new AgentTurnStatsItem(turnId, _activityStartedAt);
                    _turnHeaders.Add(turnId, header);
                    _timeline.Add(header);
                }
                break;
            case AgentAssistantTextDelta text:
                RecordResponse(text, text.ParentToolCallId);
                var streaming = GetAssistantItem(text, text.ParentToolCallId, text.MessageId, out var streamParent);
                streaming.Append(text.Text);
                RecordReplyCandidate(text, streaming, streamParent);
                streamParent?.UpdateScopedActivity(text.Text);
                break;
            case AgentAssistantMessageCompleted message:
                RecordResponse(message, message.ParentToolCallId);
                var reply = GetAssistantItem(message, message.ParentToolCallId, message.MessageId, out var messageParent);
                reply.Complete(message.Text);
                RecordReplyCandidate(message, reply, messageParent);
                messageParent?.UpdateScopedActivity(message.Text);
                break;
            case AgentReasoningDelta reasoning:
                var reasoningParent = GetScope(reasoning, reasoning.ParentToolCallId);
                if (reasoningParent is null)
                    ClearReplyCandidate(reasoning);
                RecordReasoning(reasoning, reasoningParent);
                if (reasoningParent is not null)
                {
                    reasoningParent.UpdateScopedActivity(reasoning.Text);
                    TrackBackgroundWork(reasoningParent);
                }
                break;
            case AgentToolCallStarted started:
                var startingTool = GetTool(started, started.ToolCallId, started.ParentToolCallId, started);
                UpdateGrouping(startingTool);
                RecordToolActivity(started, startingTool, started.ParentToolCallId);
                break;
            case AgentToolCallUpdated updated:
                var updatingTool = GetTool(updated, updated.ToolCallId, updated.ParentToolCallId);
                updatingTool.Update(updated.Status, updated.Message);
                UpdateGrouping(updatingTool);
                RecordToolActivity(updated, updatingTool, updated.ParentToolCallId);
                break;
            case AgentToolCallCompleted completed:
                var completedTool = GetTool(completed, completed.ToolCallId, completed.ParentToolCallId);
                completedTool.Complete(completed);
                UpdateGrouping(completedTool);
                RecordToolActivity(completed, completedTool, completed.ParentToolCallId);
                break;
            case AgentTurnPhaseChanged phase:
                ChangePhase(phase, phase.Phase, phase.ToolName);
                break;
            case AgentPermissionRequested or AgentUserQuestionRequested:
                ClearReplyCandidate(entry);
                break;
            case AgentContextUsageUpdated usage:
                ContextUsage = usage.Usage;
                break;
            case AgentContextCompacted compacted:
                ChangePhase(compacted, AgentTurnPhase.Thinking);
                _timeline.Add(new AgentTimelineDividerItem(compacted.TurnId, AgentStrings.ContextCompacted, Icon.ArrowMinimize));
                break;
            case AgentThreadModelChanged changed:
                _timeline.Add(new AgentModelChangeItem(changed));
                break;
            case AgentThreadProviderChanged changed:
                ResetContextUsage(keepWindow: false);
                _timeline.Add(new AgentProviderChangeItem(changed));
                break;
            case AgentContextCleared cleared:
                ResetContextUsage();
                _timeline.Add(new AgentTimelineDividerItem(cleared.TurnId, AgentStrings.ContextCleared, Icon.ArrowReset));
                break;
            case AgentCompactionStarted started:
                StartActivity(started);
                ChangePhase(started, AgentTurnPhase.Compacting);
                break;
            case AgentCompactionCompleted compacted:
                FinishActivity(compacted);
                if (compacted.Succeeded)
                {
                    if (compacted.UsedHandoff)
                        ResetContextUsage();
                    else
                        ContextUsage = compacted.ContextUsage ?? new AgentContextUsage(ContextWindowTokens: ContextUsage?.ContextWindowTokens);
                    _timeline.Add(new AgentTimelineDividerItem(compacted.TurnId, AgentStrings.ContextCompacted, Icon.ArrowMinimize));
                }
                break;
            case AgentPlanUpdated plan:
                if (plan.TurnId is Guid id && _plans.TryGetValue(id, out var existing))
                    existing.Entry = plan;
                else
                {
                    var item = new AgentPlanItem(plan);
                    if (plan.TurnId is Guid planTurnId)
                        _plans.Add(planTurnId, item);
                    _timeline.Add(item);
                }
                break;
            case AgentTurnHistoryRecorded { TransactionCount: > 0 } history:
                _timeline.Add(new AgentHistoryRecordItem(history));
                break;
            case AgentErrorNotice notice:
                AddNotice(notice.TurnId, notice.Message, FAInfoBarSeverity.Warning);
                break;
            case AgentTurnCompleted turnCompleted:
                FinishActivity(turnCompleted);
                if (turnCompleted.ContextUsage is { } contextUsage)
                    ContextUsage = contextUsage;
                if (turnCompleted.TurnId is { } completedId && _replyCandidates.Remove(completedId, out var assistant)
                    && !string.IsNullOrWhiteSpace(assistant.Text))
                    assistant.MarkFinal();
                TurnLayoutChanged?.Invoke();
                break;
            case AgentTurnFailed failed:
                ClearReplyCandidate(failed);
                FinishActivity(failed);
                FinishTools(failed.TurnId, AgentToolStatus.Failed);
                AddNotice(failed.TurnId, failed.Error, FAInfoBarSeverity.Error);
                break;
            case AgentTurnCancelled cancelled:
                ClearReplyCandidate(cancelled);
                FinishActivity(cancelled);
                FinishTools(cancelled.TurnId, AgentToolStatus.Cancelled);
                AddNotice(cancelled.TurnId, AgentStrings.TurnStopped, FAInfoBarSeverity.Informational);
                break;
        }
    }

    private void RecordReplyCandidate(AgentTimelineEvent entry, AgentAssistantItem message, AgentToolItem? parent)
    {
        if (parent is null && entry.TurnId is { } turnId && !string.IsNullOrWhiteSpace(message.Text))
            _replyCandidates[turnId] = message;
    }

    private void ClearReplyCandidate(AgentTimelineEvent entry)
    {
        if (entry.TurnId is { } turnId)
            _replyCandidates.Remove(turnId);
    }

    private AgentAssistantItem GetAssistantItem(AgentTimelineEvent entry, string? parentId, string? messageId, out AgentToolItem? parent)
    {
        parent = GetScope(entry, parentId);
        var key = (entry.TurnId, parentId, messageId);
        if (!_messages.TryGetValue(key, out var item) || (messageId is null && item.ReceivedCompletion))
        {
            item = new AgentAssistantItem(entry.TurnId, entry.Timestamp);
            _messages[key] = item;
            (parent?.Children ?? _timeline).Add(item);
            if (parent is not null)
                TrackBackgroundWork(parent);
        }
        return item;
    }

    // A transcript can begin before the corresponding tool_use message. Keep it scoped immediately.
    private AgentToolItem? GetScope(AgentTimelineEvent entry, string? parentId)
        => string.IsNullOrEmpty(parentId) ? null : GetTool(entry, parentId, null);

    private AgentToolItem GetTool(AgentTimelineEvent entry, string toolCallId, string? parentId, AgentToolCallStarted? description = null)
    {
        var key = (entry.TurnId, toolCallId);
        bool exists = _tools.TryGetValue(key, out var tool);
        if (tool is null)
        {
            tool = new AgentToolItem(description ?? new AgentToolCallStarted(
                entry.ThreadId, entry.TurnId, entry.Timestamp, toolCallId, AgentStrings.Tool));
            if (_stopWorkTurn.HasValue && entry.TurnId == _stopWorkTurn)
                tool.StopAction = _stopWork;
            _tools.Add(key, tool);
        }
        else if (description is not null)
            tool.Describe(description);

        if (tool.Parent is null && !string.IsNullOrEmpty(parentId) && parentId != toolCallId)
        {
            var parentKey = (entry.TurnId, parentId);
            if (_tools.TryGetValue(parentKey, out var parent))
                AttachTool(tool, parent);
            else
            {
                if (!_waitingTools.TryGetValue(parentKey, out var waiting))
                    _waitingTools.Add(parentKey, waiting = []);
                if (!waiting.Contains(tool))
                    waiting.Add(tool);
            }
        }

        if (!exists && tool.Parent is null)
            _timeline.Add(tool);
        if (_waitingTools.Remove(key, out var children))
        {
            foreach (var child in children)
                AttachTool(child, tool);
        }

        TrackBackgroundWork(tool);
        return tool;
    }

    private void AttachTool(AgentToolItem child, AgentToolItem parent)
    {
        if (child.Parent is not null)
            return;
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
                return;
        }

        child.Parent = parent;
        UpdateGrouping(child);
        int index = _timeline.IndexOf(child);
        if (index >= 0)
        {
            _timeline.RemoveAt(index);
            if (index > 0 && _timeline[index - 1] is AgentToolItem previous)
                UpdateGrouping(previous);
        }
        parent.Children.Add(child);
        UpdateGrouping(parent);
        TrackBackgroundWork(parent);
    }

    public void AddNotice(Guid? turnId, string message, FAInfoBarSeverity severity)
    {
        if (_timeline.LastOrDefault() is AgentNoticeItem previous && previous.TurnId == turnId && previous.Message == message)
            return;
        _timeline.Add(new AgentNoticeItem(turnId, message, severity));
    }

    private void FinishTools(Guid? turnId, AgentToolStatus status)
    {
        foreach (var tool in _tools.Values.Where(tool => tool.TurnId == turnId))
        {
            tool.FinishIfRunning(status);
            UpdateGrouping(tool);
        }
    }
}
