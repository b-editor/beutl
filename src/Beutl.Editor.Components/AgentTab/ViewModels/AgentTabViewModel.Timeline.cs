using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Beutl.Agents;
using Beutl.Services;
using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private static readonly AgentThreadTimeline s_emptyTranscript = new();
    private readonly ObservableCollection<AgentTimelineItemViewModel> _timeline = [];
    private readonly Dictionary<AgentTurnStatsItem, AgentTurnWorkItem> _workSections = [];
    private readonly HashSet<(Guid ThreadId, Guid TurnId, int Segment)> _expandedWork = [];
    public ObservableCollection<AgentTimelineItemViewModel> DisplayTimeline { get; } = [];
    private AgentThreadTimeline Transcript => _controller?.Transcript ?? s_emptyTranscript;

    public AgentBackgroundWorkGroup BackgroundCommands => Transcript.BackgroundCommands;
    public AgentBackgroundWorkGroup Subagents => Transcript.Subagents;
    public bool HasSubagents => Transcript.HasSubagents;
    public bool HasBackgroundWork => Transcript.HasBackgroundWork;
    public string BackgroundWorkCount => Transcript.BackgroundWorkCount;
    public string LiveStatus => Transcript.LiveStatus;
    public Icon LiveStatusIcon => Transcript.LiveStatusIcon;
    public bool IsCompacting => Transcript.IsCompacting;
    public bool ShowLiveStatus => IsRunning || IsCompacting;
    public bool ShowStandaloneLiveStatus => ShowLiveStatus && !_workSections.Values.Any(section => section.Header.IsRunning);
    public bool CanActOnMessages => CanManage && _controller?.CanConfigure == true;
    public AgentPermissionItem? ActivePermission => _controller?.ActivePermission;
    private int ActivePermissionIndex => _controller?.ActivePermissionIndex ?? 0;
    public IReadOnlyList<AgentPermissionItem> PendingPermissions => _controller is { } controller ? controller.PendingPermissions : [];
    public bool HasActivePermission => ActivePermission is not null;
    public bool HasMultiplePermissions => PendingPermissions.Count > 1;
    public string PermissionQueuePosition => string.Format(CultureInfo.CurrentCulture, AgentStrings.ApprovalQueuePosition,
        ActivePermissionIndex + 1, PendingPermissions.Count);
    public bool CanShowPreviousPermission => ActivePermissionIndex > 0;
    public bool CanShowNextPermission => ActivePermissionIndex + 1 < PendingPermissions.Count;
    public AgentQuestionItem? ActiveQuestion => _controller?.ActiveQuestion;
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
            await ApplyComposerDraftAsync(message.Text, message.Mentions, message.IncludeSceneContext, message.Attachments, ct);
        });
    }

    public Task ForkFromMessageAsync(AgentMessageItem item)
        => ChangeConversationAsync(item, includeTurn: true);

    public Task EditFromMessageAsync(AgentUserItem item)
        => ChangeConversationAsync(item, includeTurn: false);

    private Task ChangeConversationAsync(AgentMessageItem item, bool includeTurn)
    {
        if (!CanActOnMessages || item is AgentAssistantItem { IsFinalTurnResponse: false }
            || item.TurnId is not Guid turnId || !_timeline.Contains(item)
            || _controller is not { } controller || _selectedThread is not { } source)
            return Task.CompletedTask;
        return ManageAsync(async ct =>
        {
            var fork = await controller.ForkThreadAsync(turnId, AgentThreadTitleGenerator.ForkTitle(source.Title), includeTurn, ct);
            if (fork is null)
                return;
            await LoadThreadAsync(fork, ct);
            if (!includeTurn && item is AgentUserItem user)
            {
                var files = (user.Entry.Attachments ?? [])
                    .Select(attachment => new AgentAttachedFile(attachment.Path, attachment.MediaType));
                await ApplyComposerDraftAsync(user.Text, user.Entry.Mentions, user.Entry.IncludeSceneContext ?? true, files, ct);
                await SaveDraftAsync();
            }
        });
    }

    public Task RetryMessageAsync(AgentAssistantItem item)
    {
        if (!CanActOnMessages || !item.IsFinalTurnResponse || item.TurnId is not Guid turnId || !_timeline.Contains(item)
            || _controller is not { } controller || _selectedThread is not { } source || SelectedProvider is not { } provider)
            return Task.CompletedTask;
        var turnUsers = _timeline.OfType<AgentUserItem>().Where(user => user.TurnId == turnId).ToArray();
        if (turnUsers.Length == 0)
            return Task.CompletedTask;
        return ManageAsync(async ct =>
        {
            var retry = BuildRetryMessage(provider.Descriptor, turnUsers);
            if (_timeline.OfType<AgentUserItem>().Last().TurnId == turnId)
            {
                await controller.RewindTurnAsync(turnId, ct, retry);
            }
            else if (await controller.ForkThreadAsync(turnId, AgentThreadTitleGenerator.ForkTitle(source.Title), includeTurn: false, ct) is { } fork)
            {
                await LoadThreadAsync(fork, ct);
                _ = _controller?.SubmitAsync(retry);
            }
        });
    }

    private AgentQueuedMessage BuildRetryMessage(AgentProviderDescriptor provider, AgentUserItem[] turnUsers)
    {
        if (turnUsers.Length == 1)
        {
            var entry = turnUsers[0].Entry;
            return new AgentQueuedMessage(_editor, provider, entry.Text, entry.IncludeSceneContext ?? true,
                entry.Attachments ?? [], GetInvokedProviderSkills(entry.Text), entry.Mentions);
        }

        string text = string.Join("\n\n", turnUsers
            .Select(user => user.Entry.Text)
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrEmpty(text))
            text = turnUsers[0].Entry.Text;

        bool includeSceneContext = turnUsers.Any(user => user.Entry.IncludeSceneContext ?? true);
        var attachments = new List<AgentUserAttachment>();
        var seenPaths = new HashSet<string>(s_pathComparer);
        var mentions = new List<AgentContextMention>();
        var seenMentions = new HashSet<AgentContextMention>();
        foreach (var user in turnUsers)
        {
            if (user.Entry.Attachments is { Count: > 0 } entryAttachments)
            {
                foreach (var attachment in entryAttachments)
                {
                    if (seenPaths.Add(attachment.Path))
                        attachments.Add(attachment);
                }
            }
            if (user.Entry.Mentions is { Count: > 0 } entryMentions)
            {
                foreach (var mention in entryMentions)
                {
                    if (seenMentions.Add(mention))
                        mentions.Add(mention);
                }
            }
        }

        return new AgentQueuedMessage(_editor, provider, text, includeSceneContext,
            attachments, GetInvokedProviderSkills(text), mentions.Count > 0 ? mentions : null);
    }

    internal async Task OpenMarkdownLinkAsync(AgentMarkdownLink link)
    {
        if (_disposed) return;
        var ct = _lifetime.Token;
        try
        {
            if (!await _host.TryOpenMarkdownLinkAsync(_editor, _projectPath, link, ct) && !ct.IsCancellationRequested)
                _editor.GetService<INotificationService>()?.ShowWarning(AgentStrings.Title, AgentStrings.LinkUnavailable);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void AttachController(AgentThreadController? controller)
    {
        if (_controller is not null)
        {
            _controller.PropertyChanged -= OnControllerChanged;
            _controller.Transcript.PropertyChanged -= OnTranscriptChanged;
            _controller.Transcript.TurnLayoutChanged -= SyncDisplayTimeline;
            ((INotifyCollectionChanged)_controller.Timeline).CollectionChanged -= OnTimelineChanged;
        }
        _controller = controller;
        UpdateThreadViewer();
        _timeline.Clear();
        _workSections.Clear();
        if (_disposed)
            return;
        if (controller is not null)
        {
            foreach (var item in controller.Timeline)
            {
                _timeline.Add(item is AgentQuestionItem { TurnId: { } turnId } question
                    ? new AgentQuestionItem(turnId, question.WasAnswered) : BindTimelineItem(item));
            }
            ((INotifyCollectionChanged)controller.Timeline).CollectionChanged += OnTimelineChanged;
            controller.Transcript.PropertyChanged += OnTranscriptChanged;
            controller.Transcript.TurnLayoutChanged += SyncDisplayTimeline;
            controller.PropertyChanged += OnControllerChanged;
        }
        SyncDisplayTimeline();
        Notify(nameof(BackgroundCommands), nameof(Subagents));
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
        else if (e.Action == NotifyCollectionChangedAction.Replace)
        {
            int index = e.NewStartingIndex;
            foreach (AgentTimelineItemViewModel item in e.NewItems!)
                _timeline[index++] = BindTimelineItem(item);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _timeline.Clear();
            _workSections.Clear();
        }
        SyncDisplayTimeline();
        Notify(nameof(ShowEmptyState));
        UpdateRevertButtons();
    }

    private AgentTimelineItemViewModel BindTimelineItem(AgentTimelineItemViewModel item) => item switch
    {
        AgentHistoryRecordItem history => new AgentHistoryItem(history.Entry, RevertTurnAsync),
        AgentModelChangeItem change => BindModelChange(change.Entry),
        AgentProviderChangeItem change => new AgentTimelineDividerItem(change.TurnId,
            string.Format(CultureInfo.CurrentCulture, AgentStrings.ProviderChangeNotice,
                ProviderDisplayName(change.Entry.PreviousProviderId), ProviderDisplayName(change.Entry.ProviderId)), Icon.ArrowSwap),
        _ => item
    };

    private void SyncDisplayTimeline()
    {
        var display = new List<AgentTimelineItemViewModel>();
        var latest = new Dictionary<Guid, List<AgentTimelineItemViewModel>>();
        var work = new Dictionary<AgentTurnWorkItem, List<AgentTimelineItemViewModel>>();
        foreach (var item in _timeline)
        {
            if (item is AgentTurnStatsItem header && header.TurnId is { } id && _controller is { } controller)
            {
                var key = (controller.ThreadId, id, header.Segment);
                if (!_workSections.TryGetValue(header, out var section))
                {
                    section = new AgentTurnWorkItem(header, header.IsRunning || _expandedWork.Contains(key), expanded =>
                    {
                        if (expanded && !header.IsRunning)
                            _expandedWork.Add(key);
                        else if (!expanded)
                            _expandedWork.Remove(key);
                    });
                    _workSections.Add(header, section);
                }
                else
                {
                    section.SyncRunningState(_expandedWork.Contains(key));
                }
                var entries = new List<AgentTimelineItemViewModel>();
                latest[id] = entries;
                work.Add(section, entries);
                display.Add(section);
            }
            else if (item is AgentUserItem && item.TurnId is { } userTurn)
            {
                latest.Remove(userTurn);
                display.Add(item);
            }
            else if (item is not AgentAssistantItem { IsFinalTurnResponse: true } and not AgentTimelineDividerItem
                && item.TurnId is { } turnId && latest.TryGetValue(turnId, out var entries))
            {
                entries.Add(item);
            }
            else
            {
                display.Add(item);
            }
        }

        foreach (var (section, entries) in work)
            SyncItems(section.Entries, entries);
        display.RemoveAll(item => item is AgentTurnWorkItem { Header.IsRunning: false, Entries.Count: 0 });
        SyncItems(DisplayTimeline, display);
        Notify(nameof(ShowStandaloneLiveStatus));
    }

    private static void SyncItems(ObservableCollection<AgentTimelineItemViewModel> current, List<AgentTimelineItemViewModel> updated)
    {
        int sharedStart = 0;
        while (sharedStart < current.Count && sharedStart < updated.Count && current[sharedStart] == updated[sharedStart])
            sharedStart++;
        int oldEnd = current.Count;
        int newEnd = updated.Count;
        while (oldEnd > sharedStart && newEnd > sharedStart && current[oldEnd - 1] == updated[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }
        for (int i = oldEnd - 1; i >= sharedStart; i--)
            current.RemoveAt(i);
        for (int i = sharedStart; i < newEnd; i++)
            current.Insert(i, updated[i]);
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentThreadController.Diagnostic))
        {
            UpdateDiagnostic();
            return;
        }
        if (e.PropertyName == nameof(AgentThreadController.QueuedMessages))
        {
            NotifyQueue();
            return;
        }
        UpdateDiagnostic();
        Notify(
            nameof(ShowLiveStatus),
            nameof(ShowStandaloneLiveStatus),
            nameof(ActivePermission),
            nameof(PendingPermissions),
            nameof(HasActivePermission),
            nameof(HasMultiplePermissions),
            nameof(PermissionQueuePosition),
            nameof(CanShowPreviousPermission),
            nameof(CanShowNextPermission),
            nameof(ActiveQuestion),
            nameof(HasActiveQuestion),
            nameof(ShowQuestionCard),
            nameof(ShowComposer));
        NotifyAvailability();
    }

    private void OnTranscriptChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentThreadTimeline.ContextUsage))
            NotifyContextMeter();
        else if (e.PropertyName is { } name)
        {
            Notify(name);
            if (name == nameof(IsCompacting))
                Notify(nameof(ShowLiveStatus), nameof(ShowStandaloneLiveStatus));
        }
        else
        {
            Notify(
                nameof(HasSubagents),
                nameof(HasBackgroundWork),
                nameof(BackgroundWorkCount),
                nameof(LiveStatus),
                nameof(LiveStatusIcon),
                nameof(IsCompacting),
                nameof(ShowLiveStatus),
                nameof(ShowStandaloneLiveStatus));
            NotifyContextMeter();
        }
    }

    private void NotifyQueue() => Notify(
        nameof(QueuedMessages),
        nameof(HasQueuedMessages),
        nameof(CanManageQueue),
        nameof(CanReorderQueuedMessages),
        nameof(CanPromoteQueuedMessage),
        nameof(CanEditQueuedMessage),
        nameof(PromoteQueuedMessageLabel));

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
