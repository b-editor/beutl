using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private static readonly AgentThreadTimeline s_emptyTranscript = new();
    private readonly ObservableCollection<AgentTimelineItemViewModel> _timeline = [];
    private AgentThreadTimeline Transcript => _controller?.Transcript ?? s_emptyTranscript;

    public AgentBackgroundWorkGroup BackgroundCommands => Transcript.BackgroundCommands;
    public AgentBackgroundWorkGroup Subagents => Transcript.Subagents;
    public bool HasSubagents => Transcript.HasSubagents;
    public bool HasBackgroundWork => Transcript.HasBackgroundWork;
    public string BackgroundWorkCount => Transcript.BackgroundWorkCount;
    public AgentPermissionItem? ActivePermission => _controller?.ActivePermission;
    public int ActivePermissionIndex => _controller?.ActivePermissionIndex ?? 0;
    public IReadOnlyList<AgentPermissionItem> PendingPermissions => _controller is { } controller ? controller.PendingPermissions : [];
    public bool HasActivePermission => ActivePermission is not null;
    public bool HasMultiplePermissions => PendingPermissions.Count > 1;
    public string PermissionQueuePosition => string.Format(CultureInfo.CurrentCulture, AgentStrings.ApprovalQueuePosition,
        ActivePermissionIndex + 1, PendingPermissions.Count);
    public bool CanShowPreviousPermission => ActivePermissionIndex > 0;
    public bool CanShowNextPermission => ActivePermissionIndex + 1 < PendingPermissions.Count;
    public AgentQuestionItem? ActiveQuestion => _controller?.ActiveQuestion;
    public IReadOnlyList<AgentQuestionItem> PendingQuestions => _controller is { } controller ? controller.PendingQuestions : [];
    public bool HasActiveQuestion => ActiveQuestion is not null;
    public bool ShowQuestionCard => !HasActivePermission && HasActiveQuestion;
    public bool ShowComposer => !HasActivePermission && !HasActiveQuestion;

    public IReadOnlyList<AgentQueuedMessage> QueuedMessages => _controller is { } controller ? controller.QueuedMessages : [];
    public bool HasQueuedMessages => QueuedMessages.Count > 0;
    public bool CanManageQueue => CanManage && _controller?.CanManageQueue == true;
    public bool CanReorderQueuedMessages => CanManageQueue && QueuedMessages.Count > 1;
    public bool CanPromoteQueuedMessage => CanManageQueue && _controller?.IsStopping == false;
    public bool CanEditQueuedMessage => CanManageQueue && ShowComposer && string.IsNullOrWhiteSpace(Prompt) && _attachments.Count == 0;
    public string PromoteQueuedMessageLabel => IsRunning ? AgentStrings.SteerNow : AgentStrings.SendQueuedMessage;

    public void PreviousPermission() => _controller?.NavigatePermission(-1);
    public void NextPermission() => _controller?.NavigatePermission(1);
    public Task PromoteQueuedMessageAsync(AgentQueuedMessage message)
        => CanPromoteQueuedMessage && QueuedMessages.Contains(message) ? _controller!.SteerAsync(message) : Task.CompletedTask;
    public void MoveQueuedMessage(AgentQueuedMessage message, int direction)
    {
        if (CanManageQueue)
            _controller!.MoveQueuedMessage(message, direction);
    }

    public void RemoveQueuedMessage(AgentQueuedMessage message)
    {
        if (CanManageQueue)
            _controller!.RemoveQueuedMessage(message);
    }

    public Task EditQueuedMessageAsync(AgentQueuedMessage message)
    {
        if (!CanEditQueuedMessage || _controller is not { } controller)
            return Task.CompletedTask;
        return ManageAsync(async ct =>
        {
            if (!controller.RemoveQueuedMessage(message))
                return;
            Prompt = message.Text;
            RestoreAcceptedMentions(message.Mentions);
            IncludeSceneContext = message.IncludeSceneContext;
            foreach (var attachment in message.Attachments)
                await AddAttachmentAsync(attachment, ct);
        });
    }

    private void AttachController(AgentThreadController? controller)
    {
        if (_controller is not null)
        {
            _controller.PropertyChanged -= OnControllerChanged;
            _controller.Transcript.PropertyChanged -= OnTranscriptChanged;
            ((INotifyCollectionChanged)_controller.Timeline).CollectionChanged -= OnTimelineChanged;
        }
        _controller = controller;
        _timeline.Clear();
        if (controller is not null)
        {
            foreach (var item in controller.Timeline)
            {
                _timeline.Add(item is AgentQuestionItem { TurnId: { } turnId } question
                    ? new AgentQuestionItem(turnId, question.WasAnswered) : BindTimelineItem(item));
            }
            ((INotifyCollectionChanged)controller.Timeline).CollectionChanged += OnTimelineChanged;
            controller.Transcript.PropertyChanged += OnTranscriptChanged;
            controller.PropertyChanged += OnControllerChanged;
        }
        Notify(nameof(BackgroundCommands));
        Notify(nameof(Subagents));
        OnTranscriptChanged(null, new PropertyChangedEventArgs(null));
        OnControllerChanged(null, new PropertyChangedEventArgs(null));
    }

    private void OnTimelineChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            int index = e.NewStartingIndex;
            foreach (AgentTimelineItemViewModel item in e.NewItems!)
                _timeline.Insert(index++, BindTimelineItem(item));
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            for (int i = 0; i < e.OldItems!.Count; i++)
                _timeline.RemoveAt(e.OldStartingIndex);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _timeline.Clear();
        }
        Notify(nameof(ShowEmptyState));
        UpdateRevertButtons();
    }

    private AgentTimelineItemViewModel BindTimelineItem(AgentTimelineItemViewModel item) => item is AgentHistoryRecordItem history
        ? new AgentHistoryItem(history.Entry, RevertTurnAsync) : item;

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentThreadController.Error))
        {
            Notify(nameof(Error));
            return;
        }
        if (e.PropertyName == nameof(AgentThreadController.QueuedMessages))
        {
            NotifyQueue();
            return;
        }
        Notify(nameof(Error));
        Notify(nameof(ActivePermission));
        Notify(nameof(PendingPermissions));
        Notify(nameof(HasActivePermission));
        Notify(nameof(HasMultiplePermissions));
        Notify(nameof(PermissionQueuePosition));
        Notify(nameof(CanShowPreviousPermission));
        Notify(nameof(CanShowNextPermission));
        Notify(nameof(ActiveQuestion));
        Notify(nameof(PendingQuestions));
        Notify(nameof(HasActiveQuestion));
        Notify(nameof(ShowQuestionCard));
        Notify(nameof(ShowComposer));
        NotifyAvailability();
    }

    private void OnTranscriptChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name)
            Notify(name);
        else
        {
            Notify(nameof(HasSubagents));
            Notify(nameof(HasBackgroundWork));
            Notify(nameof(BackgroundWorkCount));
        }
    }

    private void NotifyQueue()
    {
        Notify(nameof(QueuedMessages));
        Notify(nameof(HasQueuedMessages));
        Notify(nameof(CanManageQueue));
        Notify(nameof(CanReorderQueuedMessages));
        Notify(nameof(CanPromoteQueuedMessage));
        Notify(nameof(CanEditQueuedMessage));
        Notify(nameof(PromoteQueuedMessageLabel));
    }

    private void UpdateRevertButtons()
    {
        foreach (var item in _timeline.OfType<AgentHistoryItem>())
            item.CanRevert = CanConfigure && item.TurnId is Guid id && _host.CanRevertTurn(_editor, id);
    }

    private Task RevertTurnAsync(AgentHistoryItem item) => ManageAsync(async ct =>
    {
        if (IsRunning || item.TurnId is not Guid id)
            return;

        item.Result = (await _host.RevertTurnAsync(_editor, id, ct)) switch
        {
            AgentTurnRevertResult.Reverted => AgentStrings.TurnReverted,
            AgentTurnRevertResult.HistoryChanged => AgentStrings.HistoryChanged,
            _ => AgentStrings.RevertUnavailable
        };
    });
}
