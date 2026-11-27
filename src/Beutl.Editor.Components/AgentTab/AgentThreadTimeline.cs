using System.Collections.ObjectModel;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.AgentTab;

internal sealed partial class AgentThreadTimeline : AgentBindable
{
    private readonly ObservableCollection<AgentTimelineItemViewModel> _timeline = [];
    private readonly Dictionary<(Guid? TurnId, string? ParentId, string? MessageId), AgentAssistantItem> _messages = new();
    private readonly Dictionary<(Guid? TurnId, string ToolCallId), AgentToolItem> _tools = new();
    private readonly Dictionary<(Guid? TurnId, string ToolCallId), List<AgentToolItem>> _waitingTools = new();
    private readonly Dictionary<Guid, AgentPlanItem> _plans = new();
    public AgentThreadTimeline() => Items = new(_timeline);

    public ReadOnlyObservableCollection<AgentTimelineItemViewModel> Items { get; }

    public void AddInteraction(AgentInteractionItem item) => _timeline.Add(item);

    public void Restore(IReadOnlyList<AgentTimelineEvent> entries)
    {
        Clear();
        foreach (var entry in entries)
        {
            if (entry is AgentUserQuestionResolved { TurnId: { } turnId } question)
                _timeline.Add(new AgentQuestionItem(turnId, question.Answered));
            else
                ApplyEntry(entry);
        }
    }

    public void Clear()
    {
        ResetBackgroundWork(null);
        foreach (var tool in _tools.Values)
            tool.Dispose();
        _tools.Clear();
        _waitingTools.Clear();
        _messages.Clear();
        _plans.Clear();
        _timeline.Clear();
    }

    public void ApplyEntry(AgentTimelineEvent entry)
    {
        // Approval and question cards come from runtime callbacks. The persisted stream also includes automatically decided requests.
        switch (entry)
        {
            case AgentTurnStarted turnStarted:
                ResetBackgroundWork(turnStarted.TurnId);
                break;
            case AgentUserMessage user:
                ResetBackgroundWork(user.TurnId);
                _timeline.Add(new AgentUserItem(user));
                break;
            case AgentAssistantTextDelta text:
                var streaming = GetAssistantItem(text, text.ParentToolCallId, text.MessageId, out var streamParent);
                streaming.Append(text.Text);
                streamParent?.UpdateScopedActivity(text.Text);
                break;
            case AgentAssistantMessageCompleted message:
                GetAssistantItem(message, message.ParentToolCallId, message.MessageId, out var messageParent).Complete(message.Text);
                messageParent?.UpdateScopedActivity(message.Text);
                break;
            case AgentReasoningDelta reasoning:
                var reasoningParent = GetScope(reasoning, reasoning.ParentToolCallId);
                var items = reasoningParent?.Children ?? _timeline;
                if (items.LastOrDefault() is AgentReasoningItem previous && previous.TurnId == reasoning.TurnId)
                    previous.Text += reasoning.Text;
                else
                    items.Add(new AgentReasoningItem(reasoning.TurnId) { Text = reasoning.Text });
                if (reasoningParent is not null)
                {
                    reasoningParent.UpdateScopedActivity(reasoning.Text);
                    TrackBackgroundWork(reasoningParent);
                }
                break;
            case AgentToolCallStarted started:
                GetTool(started, started.ToolCallId, started.ParentToolCallId, started);
                break;
            case AgentToolCallUpdated updated:
                GetTool(updated, updated.ToolCallId, updated.ParentToolCallId).Update(updated.Status, updated.Message);
                break;
            case AgentToolCallCompleted completed:
                GetTool(completed, completed.ToolCallId, completed.ParentToolCallId).Complete(completed);
                break;
            case AgentPlanUpdated plan:
                if (plan.TurnId is Guid id && _plans.TryGetValue(id, out var existing))
                    existing.Entry = plan;
                else
                {
                    var item = new AgentPlanItem(plan);
                    if (plan.TurnId is Guid turnId)
                        _plans.Add(turnId, item);
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
                foreach (var item in _messages.Values.Where(item => item.TurnId == turnCompleted.TurnId && !item.CanCopy))
                    item.Finish();
                break;
            case AgentTurnFailed failed:
                FinishTools(failed.TurnId, AgentToolStatus.Failed);
                AddNotice(failed.TurnId, failed.Error, FAInfoBarSeverity.Error);
                break;
            case AgentTurnCancelled cancelled:
                FinishTools(cancelled.TurnId, AgentToolStatus.Cancelled);
                AddNotice(cancelled.TurnId, AgentStrings.TurnStopped, FAInfoBarSeverity.Informational);
                break;
        }
    }

    private AgentAssistantItem GetAssistantItem(AgentTimelineEvent entry, string? parentId, string? messageId, out AgentToolItem? parent)
    {
        parent = GetScope(entry, parentId);
        var key = (entry.TurnId, parentId, messageId);
        if (!_messages.TryGetValue(key, out var item))
        {
            item = new AgentAssistantItem(entry.TurnId);
            _messages.Add(key, item);
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

        _timeline.Remove(child);
        child.Parent = parent;
        parent.Children.Add(child);
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
            tool.FinishIfRunning(status);
    }
}
