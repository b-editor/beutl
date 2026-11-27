using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Logging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab;

// The project owns this state. Dispatcher affinity serializes changes for all subscribers.
internal sealed partial class AgentThreadController : AgentBindable
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentThreadController>();
    private readonly IAgentWorkspaceHost _host;
    private readonly AgentProjectThreadSessions _project;
    private readonly ObservableCollection<AgentQueuedMessage> _queue = [];
    private readonly ConcurrentQueue<(ActiveTurn Turn, AgentTimelineEvent Entry)> _pendingTimeline = new();
    private readonly AgentThreadTimeline _transcript;
    private int _drainScheduled;
    private Task? _loading;
    private Task? _run;
    private ActiveTurn? _active;
    private bool _configuring;
    private bool _closed;
    private string _title = AgentStrings.Title;

    public AgentThreadController(IAgentWorkspaceHost host, AgentProjectThreadSessions project, Guid threadId)
    {
        _host = host;
        _project = project;
        _transcript = new AgentThreadTimeline(StopBackgroundTaskAsync, AgentRuntimeManager.GetWorkingDirectory(project.ProjectPath));
        ThreadId = threadId;
        QueuedMessages = new(_queue);
        PendingPermissions = new(_permissions);
    }

    public Guid ThreadId { get; }
    public AgentThreadTimeline Transcript => _transcript;
    public ReadOnlyObservableCollection<AgentTimelineItemViewModel> Timeline => _transcript.Items;
    public ReadOnlyObservableCollection<AgentQueuedMessage> QueuedMessages { get; }
    public IReadOnlyList<string> PromptHistory { get; private set; } = [];
    public Guid? ActiveTurnId => _active?.Id;
    public bool IsRunning => _active is not null;
    public bool IsSteering => _active?.Steering is not null;
    public bool IsStopping => _active?.StopRequested == true;
    public bool CanConfigure => !_closed && !_configuring && !IsRunning;
    public bool CanSubmit => !_closed && !_configuring && !_project.Lifetime.IsCancellationRequested;
    public bool CanManageQueue => CanSubmit && !IsSteering;
    public AgentDiagnostic? Diagnostic { get; private set => Set(ref field, value); }
    public void UpdateTitle(string title) => _title = title;

    public Task LoadAsync(AgentThreadMetadata thread, CancellationToken cancellationToken)
    {
        if (_loading is null || _loading.IsCanceled || _loading.IsFaulted)
            _loading = LoadCoreAsync(thread);
        return _loading.WaitAsync(cancellationToken);
    }

    private async Task LoadCoreAsync(AgentThreadMetadata thread)
    {
        UpdateTitle(thread.Title);
        var entries = await _host.Runtime.LoadTimelineAsync(_project.ProjectPath, ThreadId, _project.Lifetime);
        var draft = await _project.LoadDraftAsync(ThreadId, _project.Lifetime);
        _project.Lifetime.ThrowIfCancellationRequested();
        PromptHistory = draft.History;
        if (PromptHistory.Count == 0 && ExtractPromptHistory(entries) is { Count: > 0 } history)
        {
            // Threads saved before draft persistence already have their prompts in JSONL.
            PromptHistory = history;
            _project.SeedHistory(ThreadId, history);
        }
        _transcript.Restore(entries);
        if (thread.RestorePreviousMessages && thread.ExternalSessionId is null)
            _transcript.ResetContextUsage();
    }

    private static List<string> ExtractPromptHistory(IReadOnlyList<AgentTimelineEvent> entries)
    {
        var history = new List<string>();
        for (int i = entries.Count - 1; i >= 0 && history.Count < AgentDraftStore.HistoryLimit; i--)
        {
            if (entries[i] is AgentUserMessage user && (history.Count == 0 || history[^1] != user.Text))
                history.Add(user.Text);
        }
        history.Reverse();
        return history;
    }

    public bool TryBeginConfiguration()
    {
        if (!CanConfigure)
            return false;
        _configuring = true;
        PublishState();
        return true;
    }

    public void EndConfiguration()
    {
        _configuring = false;
        PublishState();
    }

    public async Task<AgentThreadMetadata> SetModelAsync(string? model, string? effort, CancellationToken cancellationToken)
    {
        var (thread, change) = await _host.Runtime.SetModelAsync(_project.ProjectPath, ThreadId, model, effort, cancellationToken);
        if (change is not null)
            _transcript.ApplyEntry(change);
        return thread;
    }

    public async Task<AgentThreadMetadata> SetProviderAsync(
        AgentProviderDescriptor provider, string? model, CancellationToken cancellationToken)
    {
        var (thread, change) = await _host.Runtime.SetProviderAsync(_project.ProjectPath, ThreadId, provider.Id, model, null, cancellationToken);
        if (change is not null)
            _transcript.ApplyEntry(change);
        foreach (var message in _queue)
            message.SetProvider(provider);
        return thread;
    }

    public async Task<AgentThreadMetadata> ClearContextAsync(CancellationToken cancellationToken)
    {
        var (thread, marker) = await _host.Runtime.ClearContextAsync(_project.ProjectPath, ThreadId, cancellationToken);
        _transcript.ApplyEntry(marker);
        return thread;
    }

    public async Task<AgentThreadMetadata> CompactContextAsync(AgentProviderDescriptor provider, CancellationToken cancellationToken)
    {
        var callbacks = new AgentTurnCallbacks(
            entry => Dispatcher.UIThread.Post(() => _transcript.ApplyEntry(entry), DispatcherPriority.Normal),
            ShowPermissionAsync, ShowQuestionAsync);
        try
        {
            return await _host.CompactContextAsync(_project.ProjectPath, ThreadId, provider, callbacks, cancellationToken);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Normal);
        }
    }

    public async Task<AgentThreadMetadata?> ForkThreadAsync(
        Guid? turnId, string title, bool includeTurn, CancellationToken cancellationToken)
    {
        if (!TryBeginConfiguration())
            return null;

        try
        {
            var thread = await _host.Runtime.ForkThreadAsync(
                _project.ProjectPath, ThreadId, turnId, title, includeTurn, cancellationToken);
            _project.PublishMetadata(thread);
            return thread;
        }
        finally
        {
            EndConfiguration();
        }
    }

    public async Task RewindTurnAsync(Guid turnId, CancellationToken cancellationToken, AgentQueuedMessage? retry = null)
    {
        if (!TryBeginConfiguration())
            return;

        try
        {
            var (thread, entries) = await _host.Runtime.RewindTurnAsync(_project.ProjectPath, ThreadId, turnId, cancellationToken);
            _transcript.Restore(entries);
            _transcript.ResetContextUsage();
            _project.PublishMetadata(thread);
        }
        finally
        {
            EndConfiguration();
        }

        if (retry is not null && !cancellationToken.IsCancellationRequested)
            _ = SubmitAsync(retry);
    }

    public Task SubmitAsync(AgentQueuedMessage message)
    {
        if (!CanSubmit)
            return Task.CompletedTask;
        if (!IsRunning)
            return _run = RunQueueAsync(message);

        _queue.Add(message);
        UpdateQueue();
        return Task.CompletedTask;
    }

    public bool RemoveQueuedMessage(AgentQueuedMessage message)
    {
        if (!CanManageQueue || !_queue.Remove(message))
            return false;
        UpdateQueue();
        return true;
    }

    public void MoveQueuedMessage(AgentQueuedMessage message, int offset)
    {
        int index = _queue.IndexOf(message);
        int next = index + offset;
        if (!CanManageQueue || index < 0 || next < 0 || next >= _queue.Count)
            return;
        _queue.Move(index, next);
        UpdateQueue();
    }

    public async Task SteerAsync(AgentQueuedMessage message)
    {
        if (!CanManageQueue || IsStopping)
            return;
        if (_active is not { } turn)
        {
            await SubmitAsync(message);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        turn.Steering = completion.Task;
        if (!_queue.Contains(message))
            _queue.Add(message);
        UpdateQueue();
        PublishState();
        Diagnostic = null;
        try
        {
            var request = message.CreateRequest(ThreadId, turn.Id);
            if (await _host.SteerTurnAsync(message.Editor, _project.ProjectPath, request, message.IncludeSceneContext,
                message.Attachments, message.Mentions, _project.Lifetime))
            {
                _queue.Remove(message);
                UpdateQueue();
            }
            else if (!turn.StopRequested && !_project.Lifetime.IsCancellationRequested)
            {
                // The provider either doesn't support steering or finished during the request.
                // Keep the promoted message until it actually starts.
                turn.Promoted = message;
                await turn.Cancellation.CancelAsync();
            }
        }
        catch (OperationCanceledException) when (_project.Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not steer agent thread {ThreadId}.", ThreadId);
            Diagnostic = AgentDiagnosticClassifier.FromFailure(AgentStrings.SteerFailed, turn.Message.Provider);
        }
        finally
        {
            turn.Steering = null;
            completion.TrySetResult();
            PublishState();
        }
    }

    public Task StopAsync()
    {
        if (_active is not { StopRequested: false } turn)
            return Task.CompletedTask;
        turn.StopRequested = true;
        PublishState();
        return turn.Cancellation.CancelAsync();
    }

    private async Task StopBackgroundTaskAsync(AgentToolItem item)
    {
        if (_active is not { StopRequested: false } turn || turn.Id != item.TurnId || !item.IsInProgress)
            return;
        var token = turn.Cancellation.Token;
        try
        {
            var result = await _host.Runtime.StopBackgroundTaskAsync(ThreadId, turn.Id, item.ToolCallId, token);
            if (result == AgentBackgroundStopResult.Unsupported && _active == turn && item.IsInProgress)
                await StopAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not stop background work in agent thread {ThreadId}.", ThreadId);
            Diagnostic = AgentDiagnosticClassifier.FromFailure(AgentStrings.StopBackgroundTaskFailed, turn.Message.Provider);
        }
    }

    private async Task RunQueueAsync(AgentQueuedMessage first)
    {
        for (AgentQueuedMessage? next = first; next is not null;)
        {
            var message = next;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_project.Lifetime);
            using var liveStatusTick = DispatcherTimer.Run(() =>
            {
                _transcript.RefreshLiveStatus();
                return true;
            }, TimeSpan.FromSeconds(1));
            var turn = new ActiveTurn(cancellation, message);
            _active = turn;
            _queue.Remove(message);
            UpdateQueue();
            Diagnostic = null;
            PublishState();

            AgentTurnSummary? outcome = null;
            Exception? failure = null;
            try
            {
                var callbacks = new AgentTurnCallbacks(
                    entry => EnqueueTimeline(turn, entry),
                    ShowPermissionAsync, ShowQuestionAsync);
                outcome = await _host.RunTurnAsync(message.Editor, _project.ProjectPath, message.Provider,
                    message.CreateRequest(ThreadId, turn.Id), message.IncludeSceneContext, message.Attachments, message.Mentions,
                    callbacks, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // The runtime posts timeline events before returning. Drain those posts before
            // allowing another turn to take over the controller.
            await Dispatcher.UIThread.InvokeAsync(DrainPendingTimeline, DispatcherPriority.Normal);
            if (turn.Steering is { } steering)
                await steering;
            FinishInteractions();

            next = CompleteTurn(turn, outcome, failure);
        }
        _active = null;
        _run = null;
        PublishState();
    }

    private AgentQueuedMessage? CompleteTurn(ActiveTurn turn, AgentTurnSummary? outcome, Exception? failure)
    {
        var notices = Timeline.OfType<AgentNoticeItem>().Where(item => item.TurnId == turn.Id);
        if (failure is not null && !notices.Any(item => item.Message == failure.Message))
        {
            s_logger.LogWarning(failure, "Agent thread {ThreadId} could not complete a turn.", ThreadId);
            Diagnostic = AgentDiagnosticClassifier.FromFailure(failure.Message, turn.Message.Provider);
        }
        else if (outcome is { Status: AgentTurnStatus.Failed } && !notices.Any(item => item.Severity == FAInfoBarSeverity.Error))
        {
            string error = outcome.Error ?? AgentStrings.TurnFailed;
            Diagnostic = AgentDiagnosticClassifier.FromFailure(error, turn.Message.Provider);
            _transcript.AddNotice(turn.Id, error, FAInfoBarSeverity.Error);
        }

        if (!turn.StopRequested && !_project.Lifetime.IsCancellationRequested)
        {
            if (failure is not null || outcome is { Status: AgentTurnStatus.Failed })
                NotifyTurnFinished(turn, DateTimeOffset.UtcNow, AgentThreadNotificationKind.Failed);
            else if (outcome is { Status: AgentTurnStatus.Completed })
                NotifyTurnFinished(turn, DateTimeOffset.UtcNow, AgentThreadNotificationKind.Completed);
        }

        if (!turn.PromptPublished)
        {
            _queue.Insert(0, turn.Message);
            UpdateQueue();
        }

        return turn.StopRequested || _project.Lifetime.IsCancellationRequested ? null :
            turn.Promoted ?? (outcome is { Status: AgentTurnStatus.Completed } && failure is null && turn.PromptPublished
                ? _queue.FirstOrDefault() : null);
    }

    private void EnqueueTimeline(ActiveTurn turn, AgentTimelineEvent entry)
    {
        _pendingTimeline.Enqueue((turn, entry));
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            Dispatcher.UIThread.Post(DrainPendingTimeline, DispatcherPriority.Normal);
    }

    private void DrainPendingTimeline()
    {
        Interlocked.Exchange(ref _drainScheduled, 0);
        if (_pendingTimeline.IsEmpty)
            return;

        // Coalesce adjacent text/reasoning deltas within one UI pass to avoid re-parsing Markdown per token.
        var batch = new List<(ActiveTurn Turn, AgentTimelineEvent Entry)>();
        while (_pendingTimeline.TryDequeue(out var item))
            batch.Add(item);

        for (int i = 0; i < batch.Count; i++)
        {
            var (turn, entry) = batch[i];
            entry = entry switch
            {
                AgentAssistantTextDelta first => CoalesceDeltas(batch, ref i, turn, first,
                    static (a, b) => b.ThreadId == a.ThreadId && b.TurnId == a.TurnId
                        && b.ParentToolCallId == a.ParentToolCallId && b.MessageId == a.MessageId,
                    static item => item.Text,
                    static (item, text) => item with { Text = text }),
                AgentReasoningDelta first => CoalesceDeltas(batch, ref i, turn, first,
                    static (a, b) => b.ThreadId == a.ThreadId && b.TurnId == a.TurnId
                        && b.ParentToolCallId == a.ParentToolCallId,
                    static item => item.Text,
                    static (item, text) => item with { Text = text }),
                _ => entry
            };
            Receive(turn, entry);
        }

        if (!_pendingTimeline.IsEmpty && Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            Dispatcher.UIThread.Post(DrainPendingTimeline, DispatcherPriority.Normal);
    }

    private static TDelta CoalesceDeltas<TDelta>(
        List<(ActiveTurn Turn, AgentTimelineEvent Entry)> batch,
        ref int index,
        ActiveTurn turn,
        TDelta first,
        Func<TDelta, TDelta, bool> canMerge,
        Func<TDelta, string> getText,
        Func<TDelta, string, TDelta> withText)
        where TDelta : AgentTimelineEvent
    {
        StringBuilder? merged = null;
        while (index + 1 < batch.Count
            && batch[index + 1].Turn == turn
            && batch[index + 1].Entry is TDelta next
            && canMerge(first, next))
        {
            merged ??= new StringBuilder(getText(first));
            merged.Append(getText(next));
            index++;
        }
        return merged is not null ? withText(first, merged.ToString()) : first;
    }

    private void Receive(ActiveTurn turn, AgentTimelineEvent entry)
    {
        if (_active != turn || entry.ThreadId != ThreadId)
            return;
        if (entry is AgentUserMessage user)
        {
            turn.PromptPublished = true;
            if (PromptHistory.LastOrDefault() != user.Text)
            {
                PromptHistory = [.. PromptHistory.TakeLast(AgentDraftStore.HistoryLimit - 1), user.Text];
                _project.RecordPrompt(ThreadId, user.Text);
            }
        }
        RememberRequestOrder(entry);
        _transcript.ApplyEntry(entry);
        switch (entry)
        {
            case AgentErrorNotice { DiagnosticKind: { } kind } notice:
                Diagnostic = AgentDiagnosticClassifier.FromFailure(notice.Message, turn.Message.Provider, kind);
                break;
            case AgentTurnFailed failed:
                Diagnostic = AgentDiagnosticClassifier.FromFailure(failed.Error, turn.Message.Provider);
                NotifyTurnFinished(turn, failed.Timestamp, AgentThreadNotificationKind.Failed);
                break;
            case AgentTurnCompleted completed:
                NotifyTurnFinished(turn, completed.Timestamp, AgentThreadNotificationKind.Completed);
                break;
        }
    }

    private void NotifyTurnFinished(ActiveTurn turn, DateTimeOffset timestamp, AgentThreadNotificationKind kind)
    {
        if (turn.TerminalNotified)
            return;
        turn.TerminalNotified = true;
        NotifyActivity(turn, timestamp, kind);
    }

    private void NotifyActivity(ActiveTurn turn, DateTimeOffset timestamp, AgentThreadNotificationKind kind)
        => _project.NotifyActivity(_host, turn.Message.Editor, ThreadId, _title, timestamp, kind);

    private void UpdateQueue() => Notify(nameof(QueuedMessages));

    private void PublishState()
    {
        _transcript.SetStoppableTurn(_active is { StopRequested: false } turn ? turn.Id : null);
        // Related capabilities are derived from the same run/configuration state.
        Notify(null);
        _project.PublishState(ThreadId);
    }

    public async Task ShutdownAsync()
    {
        _closed = true;
        if (_active is not null)
            await StopAsync();
        if (_run is not null)
            await _run;
        if (_loading is not null)
        {
            try
            {
                await _loading;
            }
            catch (Exception ex)
            {
                s_logger.LogDebug(ex, "Agent thread {ThreadId} closed before its transcript loaded.", ThreadId);
            }
        }
        _queue.Clear();
        UpdateQueue();
        _transcript.Clear();
    }

    private sealed class ActiveTurn(CancellationTokenSource cancellation, AgentQueuedMessage message)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public AgentQueuedMessage Message { get; } = message;
        public bool TerminalNotified { get; set; }
        public HashSet<(AgentThreadNotificationKind Kind, string Id)> NotifiedRequests { get; } = [];
        public bool PromptPublished { get; set; }
        public bool StopRequested { get; set; }
        public Task? Steering { get; set; }
        public AgentQueuedMessage? Promoted { get; set; }
    }
}
