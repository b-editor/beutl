using System.Collections.ObjectModel;
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
    private readonly AgentThreadTimeline _transcript = new();
    private Task? _loading;
    private Task? _run;
    private ActiveTurn? _active;
    private bool _configuring;
    private bool _closed;

    public AgentThreadController(IAgentWorkspaceHost host, AgentProjectThreadSessions project, Guid threadId)
    {
        _host = host;
        _project = project;
        ThreadId = threadId;
        QueuedMessages = new(_queue);
        PendingPermissions = new(_permissions);
        PendingQuestions = new(_questions);
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
    public string? Error { get; private set => Set(ref field, value); }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_loading is null || _loading.IsCanceled || _loading.IsFaulted)
            _loading = LoadCoreAsync();
        return _loading.WaitAsync(cancellationToken);
    }

    private async Task LoadCoreAsync()
    {
        var entries = await _host.Runtime.LoadTimelineAsync(_project.ProjectPath, ThreadId, _project.Lifetime);
        var draft = await _project.LoadDraftAsync(ThreadId, _project.Lifetime);
        _project.Lifetime.ThrowIfCancellationRequested();
        PromptHistory = draft.History;
        if (PromptHistory.Count == 0)
        {
            // Threads saved before draft persistence already have their prompts in JSONL.
            var history = new List<string>();
            for (int i = entries.Count - 1; i >= 0 && history.Count < AgentDraftStore.HistoryLimit; i--)
            {
                if (entries[i] is AgentUserMessage user && (history.Count == 0 || history[^1] != user.Text))
                    history.Add(user.Text);
            }
            if (history.Count > 0)
            {
                history.Reverse();
                PromptHistory = history;
                _project.SeedHistory(ThreadId, history);
            }
        }
        _transcript.Restore(entries);
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
        Error = null;
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
            Error = AgentStrings.SteerFailed;
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

    private async Task RunQueueAsync(AgentQueuedMessage first)
    {
        for (AgentQueuedMessage? next = first; next is not null;)
        {
            var message = next;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_project.Lifetime);
            var turn = new ActiveTurn(cancellation);
            _active = turn;
            _queue.Remove(message);
            UpdateQueue();
            Error = null;
            PublishState();

            AgentTurnSummary? outcome = null;
            Exception? failure = null;
            try
            {
                var callbacks = new AgentTurnCallbacks(
                    entry => Dispatcher.UIThread.Post(() => Receive(turn, entry), DispatcherPriority.Normal),
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
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Normal);
            if (turn.Steering is { } steering)
                await steering;
            FinishInteractions();

            var notices = Timeline.OfType<AgentNoticeItem>().Where(item => item.TurnId == turn.Id);
            if (failure is not null && !notices.Any(item => item.Message == failure.Message))
            {
                s_logger.LogWarning(failure, "Agent thread {ThreadId} could not complete a turn.", ThreadId);
                Error = failure.Message;
            }
            else if (outcome is { Status: AgentTurnStatus.Failed } && !notices.Any(item => item.Severity == FAInfoBarSeverity.Error))
            {
                _transcript.AddNotice(turn.Id, outcome.Error ?? AgentStrings.TurnFailed, FAInfoBarSeverity.Error);
            }

            if (!turn.PromptPublished)
            {
                _queue.Insert(0, message);
                UpdateQueue();
            }

            next = turn.StopRequested || _project.Lifetime.IsCancellationRequested ? null :
                turn.Promoted ?? (outcome is { Status: AgentTurnStatus.Completed } && failure is null && turn.PromptPublished
                    ? _queue.FirstOrDefault() : null);
        }
        _active = null;
        _run = null;
        PublishState();
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
    }

    private void UpdateQueue() => Notify(nameof(QueuedMessages));

    private void PublishState()
    {
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

    private sealed class ActiveTurn(CancellationTokenSource cancellation)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public bool PromptPublished { get; set; }
        public bool StopRequested { get; set; }
        public Task? Steering { get; set; }
        public AgentQueuedMessage? Promoted { get; set; }
    }
}
