using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Beutl.Agents;

public sealed partial class AgentRuntimeManager
{
    private void EnqueueEvent(AgentTimelineEvent entry) => _timeline.Writer.TryWrite(new(entry, null, Stopwatch.GetTimestamp()));

    private Task FlushTimelineAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _timeline.Writer.TryWrite(new(null, completion)) ? completion.Task : _timelineReader;
    }

    private async Task ReadTimelineAsync()
    {
        await foreach (var item in _timeline.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.Entry is AgentComposerSuggestionsChanged suggestions)
            {
                try
                {
                    ComposerSuggestionsChanged?.Invoke(suggestions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An agent composer suggestions consumer failed.");
                }
                continue;
            }
            if (item.Entry is { } entry && _sessions.TryGetValue(entry.ThreadId, out var session))
            {
                var turn = session.Active;
                if (turn is not null && turn.Id == entry.TurnId)
                    entry = ApplyTurnEvent(turn, entry, item.ReceivedTimestamp);
                try
                {
                    await _store.AppendAsync(session.ProjectPath, entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (turn != null)
                        turn.PersistenceError ??= ex;
                    _logger.LogError(ex, "Could not persist agent timeline for {ThreadId}.", entry.ThreadId);
                }
                PublishTimeline(entry, turn);
            }
            item.Completion?.TrySetResult();
        }
    }

    private static AgentTimelineEvent ApplyTurnEvent(ActiveTurn turn, AgentTimelineEvent entry, long receivedTimestamp)
    {
        switch (entry)
        {
            case AgentTurnStarted:
                turn.Started.TrySetResult();
                return entry;
            case AgentTurnUsageUpdated usage:
                turn.InputTokens = usage.InputTokens ?? turn.InputTokens;
                turn.OutputTokens = usage.OutputTokens ?? turn.OutputTokens;
                turn.CachedInputTokens = usage.CachedInputTokens ?? turn.CachedInputTokens;
                turn.Model = usage.Model ?? turn.Model;
                return entry;
            case AgentContextUsageUpdated context:
                turn.ContextUsage = context.Usage;
                return entry;
            case AgentPermissionResolved decision:
                return decision with { WasPresented = turn.PresentedPermissions.TryRemove(decision.RequestId, out _) };
            case AgentTurnCompleted completed:
                turn.TerminalEventReceived = true;
                return completed with
                {
                    DurationMs = completed.DurationMs
                        ?? (long)Stopwatch.GetElapsedTime(turn.StartTimestamp, receivedTimestamp).TotalMilliseconds,
                    InputTokens = completed.InputTokens ?? turn.InputTokens,
                    OutputTokens = completed.OutputTokens ?? turn.OutputTokens,
                    CachedInputTokens = completed.CachedInputTokens ?? turn.CachedInputTokens,
                    Model = completed.Model ?? turn.Model,
                    ContextUsage = completed.ContextUsage ?? turn.ContextUsage
                };
            case AgentTurnFailed or AgentTurnCancelled:
                turn.TerminalEventReceived = true;
                return entry;
            default:
                return entry;
        }
    }

    private void PublishTimeline(AgentTimelineEvent entry, ActiveTurn? turn)
    {
        try
        {
            if (turn is not null && (entry.TurnId is null || turn.Id == entry.TurnId))
                turn.Callbacks?.Timeline(entry);
            TimelineEventReceived?.Invoke(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An agent timeline consumer failed.");
        }
    }
}
