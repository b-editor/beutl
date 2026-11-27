using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab;

internal sealed partial class AgentThreadTimeline
{
    private readonly Dictionary<(Guid? TurnId, string? ParentId), AgentReasoningItem> _reasoning = [];
    private Guid? _activityTurn;
    private DateTimeOffset _activityStartedAt;
    private DateTimeOffset _phaseSince;
    private AgentTurnPhase _phase;
    private string? _phaseTool;
    public bool IsCompacting => _phase == AgentTurnPhase.Compacting;
    public Icon LiveStatusIcon => _phase switch
    {
        AgentTurnPhase.RunningTool => Icon.Wrench,
        AgentTurnPhase.Responding => Icon.Chat,
        AgentTurnPhase.Compacting => Icon.ArrowMinimize,
        AgentTurnPhase.Retrying => Icon.ArrowSync,
        _ => Icon.BrainCircuit
    };

    public string LiveStatus => _phase switch
    {
        AgentTurnPhase.Responding => AgentStrings.Working,
        AgentTurnPhase.RunningTool when _phaseTool is not null =>
            string.Format(CultureInfo.CurrentCulture, AgentStrings.RunningTool, _phaseTool),
        AgentTurnPhase.Compacting => AgentStrings.CompactingContext,
        AgentTurnPhase.Retrying => AgentStrings.RetryingTurn,
        _ when _activityTurn.HasValue && DateTimeOffset.UtcNow - _phaseSince >= TimeSpan.FromSeconds(1) =>
            string.Format(CultureInfo.CurrentCulture, AgentStrings.ThinkingForSeconds,
                Math.Floor((DateTimeOffset.UtcNow - _phaseSince).TotalSeconds)),
        _ => AgentStrings.ThinkingInProgress
    };

    public void RefreshLiveStatus()
    {
        if (_phase == AgentTurnPhase.Thinking)
            Notify(nameof(LiveStatus));
        if (_activityTurn is { } turnId && _turnHeaders.TryGetValue(turnId, out var header))
            header.RefreshElapsed();
    }

    private void ClearActivity()
    {
        _reasoning.Clear();
        _activityTurn = null;
        _phase = AgentTurnPhase.Thinking;
        _phaseTool = null;
        NotifyPhase();
    }

    private void StartActivity(AgentTimelineEvent entry)
    {
        if (!entry.TurnId.HasValue || entry.TurnId == _activityTurn)
            return;
        _activityTurn = entry.TurnId;
        _phase = AgentTurnPhase.Thinking;
        _activityStartedAt = _phaseSince = entry.Timestamp;
        _phaseTool = null;
        NotifyPhase();
    }

    private void ChangePhase(AgentTimelineEvent entry, AgentTurnPhase phase, string? tool = null)
    {
        if (!_activityTurn.HasValue || _activityTurn != entry.TurnId || (_phase == phase && _phaseTool == tool))
            return;
        if (phase != AgentTurnPhase.Thinking)
            FinishReasoning(entry, null);
        _phase = phase;
        _phaseTool = tool;
        _phaseSince = entry.Timestamp;
        NotifyPhase();
    }

    private void NotifyPhase() => Notify(nameof(LiveStatus), nameof(LiveStatusIcon), nameof(IsCompacting));

    private void RecordReasoning(AgentReasoningDelta entry, AgentToolItem? parent)
    {
        if (parent is null)
            ChangePhase(entry, AgentTurnPhase.Thinking);
        var key = (entry.TurnId, entry.ParentToolCallId);
        if (!_reasoning.TryGetValue(key, out var item))
        {
            var start = parent is null && _activityTurn == entry.TurnId && _phaseSince != default ? _phaseSince : entry.Timestamp;
            _reasoning.Add(key, item = new AgentReasoningItem(entry.TurnId, start));
            (parent?.Children ?? _timeline).Add(item);
        }
        item.Text += entry.Text;
    }

    private void RecordResponse(AgentTimelineEvent entry, string? parentId)
    {
        FinishReasoning(entry, parentId);
        if (parentId is null)
            ChangePhase(entry, AgentTurnPhase.Responding);
    }

    private void RecordToolActivity(AgentTimelineEvent entry, AgentToolItem tool, string? parentId)
    {
        if (entry is AgentToolCallStarted)
            FinishReasoning(entry, parentId);
        if (parentId is not null || tool.Parent is not null || tool.IsBackgroundWork)
            return;
        if (entry is AgentToolCallStarted)
            ClearReplyCandidate(entry);
        if (tool.IsInProgress)
            ChangePhase(entry, AgentTurnPhase.RunningTool, tool.ToolName);
        else if (_phase == AgentTurnPhase.RunningTool)
        {
            var running = _tools.Values.LastOrDefault(item => item.TurnId == entry.TurnId && item.Parent is null
                && item.IsInProgress && !item.IsBackgroundWork);
            ChangePhase(entry, running is null ? AgentTurnPhase.Thinking : AgentTurnPhase.RunningTool, running?.ToolName);
        }
    }

    private void FinishReasoning(AgentTimelineEvent entry, string? parentId)
    {
        if (_reasoning.Remove((entry.TurnId, parentId), out var item))
            item.Finish(entry.Timestamp);
    }

    private void FinishActivity(AgentTimelineEvent entry)
    {
        foreach (var scope in _reasoning.Keys.Where(scope => scope.TurnId == entry.TurnId).ToArray())
            FinishReasoning(entry, scope.ParentId);
        bool finishedHeader = false;
        if (entry.TurnId is { } turnId && _turnHeaders.TryGetValue(turnId, out var header) && header.IsRunning)
        {
            header.Finish(entry);
            finishedHeader = true;
        }
        if (_activityTurn == entry.TurnId)
        {
            _activityTurn = null;
            _phase = AgentTurnPhase.Thinking;
            _phaseTool = null;
            NotifyPhase();
        }
        if (finishedHeader)
            TurnLayoutChanged?.Invoke();
    }
}
