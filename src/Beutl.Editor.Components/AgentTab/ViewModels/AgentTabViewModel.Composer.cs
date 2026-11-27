using Avalonia.Media.Imaging;
using Beutl.Agents;
using Beutl.Configuration;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal event Action? FocusRequested;
    internal bool HasPendingComposerFocus { get; set; }

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
            NotifyPromptActions();
        }
    }

    public string PromptPlaceholder => IsRunning ? AgentStrings.RunningPrompt : AgentStrings.Prompt;
    public bool CanStop => _controller is { IsRunning: true, IsStopping: false };
    public AgentComposerSendKey ComposerSendKey => _host.ComposerSendKey;
    private bool HasPromptContent => !string.IsNullOrWhiteSpace(Prompt) || _attachments.Any(item => item.IsPastedText);
    private bool HasContextCommand => ContextCommand(Prompt) is not null;
    private bool CanSubmitPrompt => CanManage && ShowComposer && (_controller?.CanSubmit ?? true)
        && (HasContextCommand ? CanManageContext : SelectedProvider?.IsAvailable == true && HasPromptContent);
    private bool SendSteers => IsRunning && _host.SteerOnSend;
    public bool CanSend => CanSubmitPrompt && (!SendSteers || CanPromoteQueuedMessage);
    private bool CanSendAlternate => IsRunning && CanSubmitPrompt && (SendSteers || CanPromoteQueuedMessage);
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

    internal void ApplyStarterPrompt(AgentStarterPrompt starter)
    {
        if (CanManage)
            Prompt = starter.Prompt;
    }

    internal async Task PopulateComposerAsync(IReadOnlyList<AgentContextMention>? mentions, bool captureFrame, string? filePath)
    {
        await InitializeAsync();
        // A thread change can still be restoring its draft. Apply this input to the draft once it is ready.
        while (_managementCompletion is { } pending)
            await pending.Task;

        await ManageAsync(async ct =>
        {
            if (mentions is { Count: > 0 })
            {
                string text = Prompt ?? string.Empty;
                string separator = text.Length == 0 || char.IsWhiteSpace(text[^1]) ? string.Empty : " ";
                var tokens = mentions.Select(mention => RegisterMention(mention, MentionToken(mention)));
                Prompt = text + separator + string.Join(' ', tokens) + " ";
            }
            if (captureFrame)
            {
                await CaptureFrameIntoDraftAsync(ct);
                IncludeSceneContext = true;
            }
            if (filePath is not null)
                await AddAttachmentAsync(new AgentAttachedFile(filePath), ct);
        });

        if (!_disposed)
        {
            HasPendingComposerFocus = true;
            FocusRequested?.Invoke();
        }
    }

    public Task CaptureFrameAsync() => ManageAsync(CaptureFrameIntoDraftAsync);

    private async Task CaptureFrameIntoDraftAsync(CancellationToken ct)
    {
        var file = await _host.CaptureFrameAsync(_editor, ct);
        await AddAttachmentAsync(file, ct);
    }

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
        RemovePendingSyncedAttachment(attachment.Path);
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
        bool hadPending = _pendingSyncedAttachments.Count > 0;
        CancelDraftSync();
        var attachments = _attachments.ToArray();
        _attachments.Clear();
        foreach (var attachment in attachments)
            attachment.Dispose();
        if (attachments.Length > 0 || hadPending)
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
        var snapshots = _attachments.Select(attachment => attachment.Snapshot()).ToList();
        if (_pendingSyncedAttachments.Count > 0)
        {
            var pendingFiles = _pendingSyncedAttachments
                .Where(pending => !snapshots.Any(item => s_pathComparer.Equals(item.Path, pending.Path)))
                .ToArray();
            if (pendingFiles.Length > 0)
            {
                var loaded = await Task.Run(() =>
                {
                    var built = new List<AgentUserAttachment>(pendingFiles.Length);
                    foreach (var pending in pendingFiles)
                    {
                        using var attachment = AgentAttachment.CreateFile(pending, static _ => { });
                        built.Add(attachment.Snapshot());
                    }
                    return built;
                }, _lifetime.Token);
                if (_disposed)
                    return;
                snapshots.AddRange(loaded);
            }
        }
        var message = new AgentQueuedMessage(_editor, selected.Descriptor, text, IncludeSceneContext,
            snapshots, GetInvokedProviderSkills(text), GetAcceptedMentions(text));
        SetError(null);
        if (_controller is null)
        {
            await ManageAsync(async ct =>
            {
                var model = EffectiveModel;
                var thread = await _host.Runtime.CreateThreadAsync(_projectPath, selected.Descriptor,
                    AgentThreadTitleGenerator.MakeTitle(text) ?? AgentStrings.NewThread,
                    _permissionMode.Mode, model.Id, ct, model.Effort);
                ReplaceThread(thread);
                await LoadThreadAsync(thread, ct, sendingDraft: true);
                IncludeSceneContext = message.IncludeSceneContext;
            });
        }
        else if (_selectedThread is { } thread
            && AgentThreadTitleGenerator.IsPlaceholderTitle(thread.Title)
            && AgentThreadTitleGenerator.MakeTitle(text) is { } title)
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

    private void NotifyPromptActions() => Notify(nameof(CanSend), nameof(ShowSendButton), nameof(CanEditQueuedMessage));

    private void NotifySendActions() => Notify(nameof(CanSend), nameof(ShowSendButton), nameof(SendLabel), nameof(SendTooltip));
}
