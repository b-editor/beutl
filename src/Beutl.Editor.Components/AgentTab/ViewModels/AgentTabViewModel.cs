using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Agents;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.ProjectSystem;
using FluentIcons.Common;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel : AgentBindable, IToolContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentTabViewModel>();
    private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly IAgentWorkspaceHost _host;
    private readonly IEditorContext _editor;
    private readonly string _projectPath;
    private readonly AgentProjectThreadSessions _sessions;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ObservableCollection<AgentThreadMetadata> _threads = [];
    private readonly ObservableCollection<AgentAttachment> _attachments = [];
    private string? _sceneName;
    private AgentProviderChoice[] _detectedProviders = [];
    private AgentThreadMetadata? _selectedThread;
    private AgentPermissionChoiceItem _permissionMode;
    private Guid? _restoredThreadId;
    private bool _restoreNewThread;
    private Task? _initialization;
    private AgentThreadController? _controller;
    private bool _busy;
    private bool _disposed;

    public AgentTabViewModel(IAgentWorkspaceHost host, IEditorContext editor, string projectPath)
    {
        _host = host;
        _editor = editor;
        _sessions = host.ThreadSessions.ForProject(projectPath);
        _projectPath = _sessions.ProjectPath;
        _permissionMode = PermissionModes.First(item => item.Mode == host.DefaultPermissionMode);
        var scene = editor.Object as Scene;
        _sceneName = scene?.Name.Value;
        scene?.Name.GetObservable().Subscribe(name => Post(() =>
        {
            _sceneName = name;
            Notify(nameof(SceneContextTooltip));
        })).DisposeWith(_subscriptions);
        Threads = new ReadOnlyObservableCollection<AgentThreadMetadata>(_threads);
        Attachments = new ReadOnlyObservableCollection<AgentAttachment>(_attachments);
        Timeline = new ReadOnlyObservableCollection<AgentTimelineItemViewModel>(_timeline);
        _sessions.StateChanged += OnThreadStateChanged;
        _sessions.MetadataChanged += OnThreadMetadataChanged;
        _sessions.ActivityChanged += OnThreadActivityChanged;
        _sessions.ActivityNotified += OnActivityNotified;
        _sessions.FlushingDrafts += FlushDraft;
        _draftTimer.Tick += OnDraftTimerTick;
        _providerProbe.DisposeWith(_subscriptions);
        host.RuntimeSettingsChanged += OnRuntimeSettingsChanged;
        host.Runtime.ComposerSuggestionsChanged += OnComposerSuggestionsChanged;
        Disposable.Create(() => host.RuntimeSettingsChanged -= OnRuntimeSettingsChanged).DisposeWith(_subscriptions);
        Disposable.Create(() => host.Runtime.ComposerSuggestionsChanged -= OnComposerSuggestionsChanged).DisposeWith(_subscriptions);
        host.FollowLiveMcpEdits.GetObservable().Subscribe(_ => Post(() => Notify(nameof(FollowLiveMcpEdits)))).DisposeWith(_subscriptions);
        IsSelected.Subscribe(OnTabSelectionChanged).DisposeWith(_subscriptions);
        if (editor.GetService(typeof(HistoryManager)) is HistoryManager history)
            history.StateChanged.Subscribe(_ => Post(UpdateRevertButtons)).DisposeWith(_subscriptions);
    }

    public ToolTabExtension Extension => AgentTabExtension.Instance;
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public IReadOnlyReactiveProperty<string> Header => _header;
    public ReadOnlyObservableCollection<AgentThreadMetadata> Threads { get; }
    public ReadOnlyObservableCollection<AgentTimelineItemViewModel> Timeline { get; }
    public ReadOnlyObservableCollection<AgentAttachment> Attachments { get; }
    public string SceneContextTooltip => string.IsNullOrWhiteSpace(_sceneName)
        ? AgentStrings.SceneContextTooltip
        : $"{_sceneName} · {AgentStrings.SceneContextTooltip}";

    public IReadOnlyList<AgentPermissionChoiceItem> PermissionModes { get; } =
    [
        new(AgentPermissionMode.Supervised, AgentStrings.Supervised, AgentStrings.Supervised, AgentStrings.SupervisedDescription, Icon.ShieldLock),
        new(AgentPermissionMode.AutoAcceptEdits, AgentStrings.AutoAcceptEdits, AgentStrings.AutoAcceptEdits, AgentStrings.AutoAcceptEditsDescription, Icon.ShieldTask),
        new(AgentPermissionMode.Auto, AgentStrings.Auto, AgentStrings.Auto, AgentStrings.AutoDescription, Icon.ShieldCheckmark),
        new(AgentPermissionMode.FullAccess, AgentStrings.FullAccess, AgentStrings.FullAccess, AgentStrings.FullAccessDescription, Icon.ShieldProhibited)
    ];

    public IReadOnlyList<AgentStarterPrompt> StarterPrompts { get; } =
    [
        new(AgentStrings.StarterReviewFrame, AgentStrings.StarterReviewFramePrompt, Icon.Image),
        new(AgentStrings.StarterSuggestCuts, AgentStrings.StarterSuggestCutsPrompt, Icon.Cut),
        new(AgentStrings.StarterRefineTitle, AgentStrings.StarterRefineTitlePrompt, Icon.TextEditStyle)
    ];

    public AgentThreadMetadata? SelectedThread => _selectedThread;
    public string ThreadTitle => _selectedThread?.Title ?? AgentStrings.NewThread;

    public IReadOnlyList<AgentProviderChoice> Providers
    {
        get;
        private set => Set(ref field, value);
    } = [];

    public AgentProviderChoice? SelectedProvider
    {
        get;
        private set
        {
            if (!Set(ref field, value))
                return;
            Notify(nameof(CanSend));
            Notify(nameof(CanSendAlternate));
            Notify(nameof(ProviderName));
            Notify(nameof(ProviderIsAvailable));
            Notify(nameof(ProviderIsReady));
            Notify(nameof(ProviderStatusLabel));
            Notify(nameof(ProviderTooltip));
            Notify(nameof(ModelPickerTooltip));
            UpdateDiagnostic();
            ResetComposerSuggestions();
        }
    }

    public string ProviderName => SelectedProvider?.Name ?? AgentStrings.Provider;
    public bool ProviderIsAvailable => SelectedProvider?.IsAvailable == true;
    public bool ProviderIsReady => ProviderIsAvailable && SelectedProvider?.Diagnostic?.Kind != AgentDiagnosticKind.OutdatedCli;
    public string ProviderStatusLabel => SelectedProvider is { } provider
        ? $"{provider.Name} · {provider.Availability}" : AgentStrings.NoProvider;
    public string ProviderTooltip => ProviderStatusLabel;

    public AgentPermissionChoiceItem SelectedPermissionMode => _permissionMode;

    public bool FollowLiveMcpEdits
    {
        get => _host.FollowLiveMcpEdits.Value;
        set => _host.FollowLiveMcpEdits.Value = value;
    }

    public bool IncludeSceneContext
    {
        get;
        set
        {
            if (Set(ref field, value))
                MarkDraftChanged();
        }
    } = true;

    public string? Prompt
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;
            if (!_recallingPrompt)
            {
                ResetPromptHistory();
                MarkDraftChanged();
            }
            Notify(nameof(CanSend));
            Notify(nameof(CanSendAlternate));
            Notify(nameof(ShowSendButton));
            Notify(nameof(CanEditQueuedMessage));
        }
    }

    internal AgentThreadController? Controller => _controller;
    public bool IsRunning => _controller?.IsRunning == true;
    public bool ShowEmptyState => !IsRunning && _timeline.Count == 0;
    public string PromptPlaceholder => IsRunning ? AgentStrings.RunningPrompt : AgentStrings.Prompt;
    public bool CanStop => _controller is { IsRunning: true, IsStopping: false };
    public bool CanManage => !_disposed && !_busy && !_sessions.Lifetime.IsCancellationRequested;
    public bool CanManageSelected => CanManage && _selectedThread is not null;
    public bool CanDeleteSelected => CanManageSelected && _controller?.CanConfigure == true;
    public bool CanConfigure => CanManage && (_controller?.CanConfigure ?? true);
    public bool CanChooseModel => CanConfigure && SelectedProvider is not null;
    public AgentComposerSendKey ComposerSendKey => _host.ComposerSendKey;
    private bool HasPromptContent => !string.IsNullOrWhiteSpace(Prompt) || _attachments.Any(item => item.IsPastedText);
    private bool HasContextCommand => ContextCommand(Prompt) is not null;
    private bool CanSubmitPrompt => CanManage && ShowComposer && (_controller?.CanSubmit ?? true)
        && (HasContextCommand ? CanManageContext : SelectedProvider?.IsAvailable == true && HasPromptContent);
    private bool SendSteers => IsRunning && _host.SteerOnSend;
    public bool CanSend => CanSubmitPrompt && (!SendSteers || CanPromoteQueuedMessage);
    public bool CanSendAlternate => IsRunning && CanSubmitPrompt && (SendSteers || CanPromoteQueuedMessage);
    public bool ShowSendButton => !IsRunning || HasPromptContent;
    public string SendLabel => SendSteers ? AgentStrings.SteerNow : IsRunning ? AgentStrings.QueueMessage : AgentStrings.Send;
    public string SendTooltip
    {
        get
        {
            if (!IsRunning)
                return SendLabel;

            string modifier = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";
            string shortcut = ComposerSendKey == AgentComposerSendKey.CtrlEnter ? $"{modifier}+Shift+Enter" : $"{modifier}+Enter";
            return $"{SendLabel}\n{string.Format(SendSteers ? AgentStrings.QueuePromptShortcut : AgentStrings.SteerPromptShortcut, shortcut)}";
        }
    }
    public Icon SendIcon => SendSteers ? Icon.Flash : Icon.ArrowUp;

    internal void ApplyStarterPrompt(AgentStarterPrompt starter)
    {
        if (CanManage)
            Prompt = starter.Prompt;
    }

    public Task InitializeAsync() => _initialization ??= ManageAsync(async ct =>
    {
        var probe = RefreshProvidersAsync();
        var list = _host.Runtime.ListThreadsAsync(_projectPath, ct);
        await Task.WhenAll(probe, list);

        var threads = await list;
        var thread = _restoreNewThread ? null
            : threads.FirstOrDefault(thread => thread.Id == _restoredThreadId) ?? threads.FirstOrDefault(thread => !thread.IsArchived);
        ReplaceThreads(threads);
        await LoadThreadAsync(thread, ct);
    });

    private void ReplaceThreads(IReadOnlyList<AgentThreadMetadata> threads)
    {
        _threads.Clear();
        foreach (var thread in threads)
            _threads.Add(thread);
        UpdateThreadRows();
        StartThreadBodySearch();
    }

    public async Task RefreshThreadsAsync()
    {
        if (!CanManage)
            return;
        var ct = _lifetime.Token;
        var previous = _threads.ToArray();
        try
        {
            var threads = await _host.Runtime.ListThreadsAsync(_projectPath, ct);
            if (!CanManage || !_threads.SequenceEqual(previous) || previous.SequenceEqual(threads))
                return;

            var current = threads.FirstOrDefault(thread => thread.Id == _selectedThread?.Id);
            ReplaceThreads(threads);
            if (current?.Id == _selectedThread?.Id)
                ApplyThread(current, preserveDraft: true);
            else
                await ManageAsync(token => LoadThreadAsync(current, token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    public Task NewThreadAsync() => ManageAsync(async ct =>
    {
        await LoadThreadAsync(null, ct);
        ReplaceThreads(await _host.Runtime.ListThreadsAsync(_projectPath, ct));
    });

    internal Task SelectThreadAsync(AgentThreadMetadata thread)
        => ManageAsync(ct => LoadThreadAsync(_threads.FirstOrDefault(item => item.Id == thread.Id) ?? thread, ct, markRead: true));

    private async Task LoadThreadAsync(AgentThreadMetadata? thread, CancellationToken ct, bool sendingDraft = false, bool markRead = false)
    {
        if (_draftLoaded && thread?.Id == _selectedThread?.Id)
        {
            ApplyThread(thread, preserveDraft: true);
            if (thread is not null && (IsSelected.Value || markRead))
                await _sessions.MarkReadAsync(_host.Runtime, thread.Id, ct);
            return;
        }

        if (!sendingDraft)
            await SaveDraftAsync();
        var controller = thread is null ? null : _sessions.GetOrCreate(_host, thread.Id);
        if (controller is not null)
            await controller.LoadAsync(thread!, ct);
        var draft = await _sessions.LoadDraftAsync(thread?.Id, ct);
        ct.ThrowIfCancellationRequested();
        if (controller is not null && _sessions.Find(controller.ThreadId) != controller)
            return;
        if (sendingDraft)
            await _sessions.SaveDraftAsync(null, null);

        AttachController(controller);
        ApplyThread(thread is null ? null : _threads.FirstOrDefault(item => item.Id == thread.Id) ?? thread);
        await RestoreDraftAsync(draft.Draft, ct);
        if (thread is not null && (IsSelected.Value || markRead))
            await _sessions.MarkReadAsync(_host.Runtime, thread.Id, ct);
    }

    private void ApplyThread(AgentThreadMetadata? thread, bool preserveDraft = false)
    {
        bool selectionChanged = _selectedThread?.Id != thread?.Id;
        bool providerChanged = _selectedThread?.ProviderId != thread?.ProviderId;
        _selectedThread = thread;
        if (thread is not null)
        {
            if (providerChanged)
                _pickerProviderId = thread.ProviderId;
            UpdateProviderSelection();
            _permissionMode = PermissionModes.First(item => item.Mode == thread.PermissionMode);
        }
        else if (!preserveDraft)
        {
            _draftProviderId = null;
            _draftModel = null;
            _useDefaultPermission = true;
            UpdateProviderSelection();
            _permissionMode = PermissionModes.First(item => item.Mode == _host.DefaultPermissionMode);
        }

        if (selectionChanged)
        {
            ResetPromptHistory();
            ResetComposerSuggestions();
            UpdateThreadRows();
        }
        Notify(nameof(SelectedThread));
        Notify(nameof(ThreadTitle));
        Notify(nameof(ThreadPickerTooltip));
        Notify(nameof(ThreadPinLabel));
        Notify(nameof(ThreadArchiveLabel));
        Notify(nameof(ProviderTooltip));
        Notify(nameof(SelectedPermissionMode));
        Notify(nameof(PermissionModeTooltip));
        NotifyAvailability();
    }

    internal Task RenameThreadAsync(AgentThreadMetadata thread, string name) => ManageAsync(async ct =>
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        var renamed = await _host.Runtime.RenameThreadAsync(_projectPath, thread.Id, name.Trim(), ct);
        ReplaceThread(renamed);
    });

    internal Task DeleteThreadAsync(AgentThreadMetadata thread) => ManageAsync(async ct =>
    {
        var controller = _sessions.GetOrCreate(_host, thread.Id);
        if (!controller.TryBeginConfiguration())
            return;
        try
        {
            await _host.Runtime.DeleteThreadAsync(_projectPath, thread.Id, ct);
            await _sessions.RemoveAsync(thread.Id);
            if (_selectedThread?.Id == thread.Id)
                await LoadThreadAsync(RecentThreads.FirstOrDefault()?.Thread, ct);
        }
        finally
        {
            controller.EndConfiguration();
        }
    });

    internal Task SetPermissionModeAsync(AgentPermissionChoiceItem choice)
    {
        if (!CanConfigure)
            return Task.CompletedTask;
        if (_selectedThread is null)
        {
            _useDefaultPermission = false;
            _permissionMode = choice;
            Notify(nameof(SelectedPermissionMode));
            Notify(nameof(PermissionModeTooltip));
            return Task.CompletedTask;
        }
        return ConfigureThreadAsync((controller, ct) => _host.Runtime.SetPermissionModeAsync(_projectPath, controller.ThreadId, choice.Mode, ct));
    }

    private Task ConfigureThreadAsync(Func<AgentThreadController, CancellationToken, Task<AgentThreadMetadata>> configure) => ManageAsync(async ct =>
    {
        if (_controller is not { } controller || !controller.TryBeginConfiguration())
            return;
        try
        {
            ReplaceThread(await configure(controller, ct));
        }
        finally
        {
            controller.EndConfiguration();
        }
    });

    private void ReplaceThread(AgentThreadMetadata thread) => _sessions.PublishMetadata(thread);

    public Task CaptureFrameAsync() => ManageAsync(async ct =>
    {
        var file = await _host.CaptureFrameAsync(_editor, ct);
        await AddAttachmentAsync(file, ct);
    });

    internal Task PickFilesAsync(Func<Task<IReadOnlyList<string>>> pick) => ManageAsync(async ct =>
        await AddFilesAsync(await pick(), ct));

    internal Task AddFilesAsync(IReadOnlyList<string> paths) => ManageAsync(ct => AddFilesAsync(paths, ct));

    private async Task AddFilesAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        foreach (string path in paths)
        {
            ct.ThrowIfCancellationRequested();
            await AddAttachmentAsync(new AgentAttachedFile(Path.GetFullPath(path)), ct);
        }
    }

    internal Task PasteImageAsync(Bitmap image) => ManageAsync(async ct =>
        await AddAttachmentAsync(await _host.SavePastedImageAsync(image, ct), ct));

    internal Task PasteTextAsync(string text) => ManageAsync(async ct =>
        await AddAttachmentAsync(await _host.SavePastedTextAsync(text, ct), ct));

    internal void ReportPasteError(Exception ex)
    {
        s_logger.LogWarning(ex, "Could not read the agent composer clipboard.");
        if (!_disposed)
            SetError(AgentStrings.PasteFailed);
    }

    private async Task AddAttachmentAsync(AgentAttachedFile file, CancellationToken ct)
    {
        if (_disposed || _attachments.Any(item => s_pathComparer.Equals(item.Path, file.Path)))
            return;
        var attachment = await Task.Run(() => AgentAttachment.CreateFile(file, RemoveAttachment), ct);
        if (ct.IsCancellationRequested)
        {
            attachment.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        _attachments.Add(attachment);
        MarkAttachmentsChanged();
    }

    private void RemoveAttachment(AgentAttachment item)
    {
        if (_attachments.Remove(item))
        {
            item.Dispose();
            MarkAttachmentsChanged();
        }
    }

    private void ClearAttachments()
    {
        var attachments = _attachments.ToArray();
        _attachments.Clear();
        foreach (var attachment in attachments)
            attachment.Dispose();
        if (attachments.Length > 0)
            MarkAttachmentsChanged();
    }

    public Task SendAsync() => CanSend ? SubmitAsync(SendSteers) : Task.CompletedTask;

    public Task SendAlternateAsync() => CanSendAlternate ? SubmitAsync(!SendSteers) : Task.CompletedTask;

    private async Task SubmitAsync(bool steer)
    {
        if (ContextCommand(Prompt) is { } command)
        {
            Prompt = null;
            await (command == "/compact" ? CompactContextAsync() : ClearContextAsync());
            return;
        }
        if (SelectedProvider is not { } selected)
            return;

        string text = Prompt?.Trim() is { Length: > 0 } prompt ? prompt : AgentStrings.PastedText;
        var message = new AgentQueuedMessage(_editor, selected.Descriptor, text, IncludeSceneContext,
            _attachments.Select(attachment => attachment.Snapshot()).ToArray(), GetInvokedProviderSkills(text), GetAcceptedMentions(text));
        SetError(null);
        if (_controller is null)
        {
            await ManageAsync(async ct =>
            {
                var model = EffectiveModel;
                var thread = await _host.Runtime.CreateThreadAsync(_projectPath, selected.Descriptor, MakeTitle(text) ?? AgentStrings.NewThread,
                    _permissionMode.Mode, model.Id, ct, model.Effort);
                ReplaceThread(thread);
                await LoadThreadAsync(thread, ct, sendingDraft: true);
                IncludeSceneContext = message.IncludeSceneContext;
            });
        }
        else if (_selectedThread is { } thread && IsPlaceholderTitle(thread.Title) && MakeTitle(text) is { } title)
        {
            await RenameThreadAsync(thread, title);
        }

        if (_disposed || _controller is not { CanSubmit: true } controller || SelectedProvider is not { IsAvailable: true } currentProvider)
            return;
        message.SetProvider(currentProvider.Descriptor);
        Prompt = null;
        RestoreAcceptedMentions(null);
        ResetPromptHistory();
        ClearAttachments();
        if (steer)
            await controller.SteerAsync(message);
        else
            await controller.SubmitAsync(message);
    }

    public Task StopAsync() => _controller?.StopAsync() ?? Task.CompletedTask;

    public object? GetService(Type serviceType) => _editor.GetService(serviceType);

    public void ReadFromJson(JsonObject json)
    {
        if (json["AgentThreadId"] is JsonValue value && value.TryGetValue(out string? id))
        {
            _restoreNewThread = id == "draft";
            _restoredThreadId = Guid.TryParse(id, out Guid parsed) ? parsed : null;
        }
        if (json["AgentExpandedWork"] is JsonArray sections)
        {
            foreach (var node in sections.OfType<JsonObject>())
            {
                if (node["ThreadId"] is JsonValue thread && thread.TryGetValue(out Guid threadId)
                    && node["TurnId"] is JsonValue turn && turn.TryGetValue(out Guid turnId)
                    && node["Segment"] is JsonValue segment && segment.TryGetValue(out int index) && index >= 0)
                    _expandedWork.Add((threadId, turnId, index));
            }
        }
    }

    public void WriteToJson(JsonObject json)
    {
        var id = _draftLoaded ? _selectedThread?.Id : _restoredThreadId;
        if (id.HasValue)
            json["AgentThreadId"] = id.Value.ToString("D");
        else if (_draftLoaded || _restoreNewThread)
            json["AgentThreadId"] = "draft";
        json.Remove("AgentCollapsedWork");
        if (_expandedWork.Count > 0)
        {
            var sections = new JsonArray();
            foreach (var (threadId, turnId, segment) in _expandedWork)
                sections.Add(new JsonObject { ["ThreadId"] = threadId, ["TurnId"] = turnId, ["Segment"] = segment });
            json["AgentExpandedWork"] = sections;
        }
        else
        {
            json.Remove("AgentExpandedWork");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ClearEscapeStopHint();
        _draftTimer.Stop();
        _ = SaveDraftAsync();
        _sessions.StateChanged -= OnThreadStateChanged;
        _sessions.MetadataChanged -= OnThreadMetadataChanged;
        _sessions.ActivityChanged -= OnThreadActivityChanged;
        _sessions.ActivityNotified -= OnActivityNotified;
        _sessions.FlushingDrafts -= FlushDraft;
        CancelThreadBodySearch();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _subscriptions.Dispose();
        AttachController(null);
        ClearAttachments();
        _header.Dispose();
        IsSelected.Dispose();
    }

    private async Task ManageAsync(Func<CancellationToken, Task> operation)
    {
        if (!CanManage)
            return;
        _busy = true;
        SetError(null);
        NotifyAvailability();
        var ct = _lifetime.Token;
        try
        {
            await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
        finally
        {
            _busy = false;
            if (!_disposed)
            {
                NotifyAvailability();
                if (_selectedThread is { } selected && _deletedSelection == selected.Id)
                {
                    _deletedSelection = null;
                    _ = ManageAsync(token => LoadThreadAsync(RecentThreads.FirstOrDefault()?.Thread, token));
                }
            }
        }
    }

    private void ReportError(Exception ex)
    {
        s_logger.LogWarning(ex, "An agent tab operation failed.");
        if (!_disposed)
            SetError(ex.Message);
    }

    private void NotifyAvailability()
    {
        if (_escapeStopTurn.HasValue && (!CanStop || _escapeStopTurn != _controller?.ActiveTurnId))
            ClearEscapeStopHint();
        Notify(nameof(CanManage));
        Notify(nameof(CanManageSelected));
        Notify(nameof(CanDeleteSelected));
        Notify(nameof(CanConfigure));
        Notify(nameof(CanActOnMessages));
        Notify(nameof(CanChooseModel));
        Notify(nameof(CanManageContext));
        NotifySendActions();
        Notify(nameof(IsRunning));
        Notify(nameof(ShowEmptyState));
        Notify(nameof(PromptPlaceholder));
        Notify(nameof(CanStop));
        NotifyQueue();
        UpdateRevertButtons();
    }

    private void NotifySendActions()
    {
        Notify(nameof(CanSend));
        Notify(nameof(CanSendAlternate));
        Notify(nameof(ShowSendButton));
        Notify(nameof(SendLabel));
        Notify(nameof(SendTooltip));
        Notify(nameof(SendIcon));
    }

    private void Post(Action callback) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed)
            callback();
    }, DispatcherPriority.Normal);

}

internal sealed record AgentProviderChoice(AgentProviderDescriptor Descriptor, bool IsAvailable, string? CliVersion, AgentDiagnostic? Diagnostic)
{
    public string Name => Descriptor.DisplayName;
    public string Availability => Diagnostic?.Kind == AgentDiagnosticKind.OutdatedCli
        ? string.Format(AgentStrings.ProviderOutdatedVersion, CliVersion)
        : CliVersion is { } version ? string.Format(AgentStrings.ProviderAvailableVersion, version)
        : IsAvailable ? AgentStrings.ProviderAvailable : AgentStrings.ProviderUnavailable;
}

internal sealed record AgentPermissionChoiceItem(AgentPermissionMode Mode, string Label, string ShortLabel, string Description, Icon Icon);
internal sealed record AgentStarterPrompt(string Label, string Prompt, Icon Icon);

