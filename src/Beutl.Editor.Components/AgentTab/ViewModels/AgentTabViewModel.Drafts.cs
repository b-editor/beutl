using Avalonia.Threading;
using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private readonly DispatcherTimer _draftTimer = new(DispatcherPriority.Background, Dispatcher.UIThread)
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };
    private bool _draftLoaded;
    private bool _draftDirty;
    private bool _restoringDraft;
    private int _historyIndex;
    private IReadOnlyList<string>? _browsedHistory;
    private string? _savedPrompt;
    private bool _recallingPrompt;

    private void MarkDraftChanged()
    {
        if (!_draftLoaded || _restoringDraft || _disposed)
            return;
        _draftDirty = true;
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private void MarkAttachmentsChanged()
    {
        MarkDraftChanged();
        NotifyPromptActions();
    }

    private async void OnDraftTimerTick(object? sender, EventArgs e) => await SaveDraftAsync();

    private void FlushDraft() => _ = SaveDraftAsync();

    private async Task SaveDraftAsync()
    {
        _draftTimer.Stop();
        if (!_draftLoaded || !_draftDirty)
            return;
        _draftDirty = false;

        Guid? threadId = _selectedThread?.Id;
        string? text = _browsedHistory is null ? Prompt : _savedPrompt;
        AgentDraft? draft = string.IsNullOrEmpty(text) && _attachments.Count == 0 && IncludeSceneContext
            ? null : new AgentDraft(text, _attachments.Select(item => item.Path).ToArray(), IncludeSceneContext,
                _attachments.Select(item => item.File.MediaType).ToArray(), GetAcceptedMentions(text));
        try
        {
            await _sessions.SaveDraftAsync(threadId, draft);
        }
        catch (Exception ex)
        {
            if (threadId == _selectedThread?.Id)
                _draftDirty = true;
            ReportError(ex);
        }
    }

    private async Task RestoreDraftAsync(AgentDraft? draft, CancellationToken ct)
    {
        _restoringDraft = true;
        try
        {
            var files = draft is null
                ? []
                : draft.AttachmentPaths.Select((path, index) =>
                    new AgentAttachedFile(path, draft.AttachmentMediaTypes?.ElementAtOrDefault(index)));
            await ApplyComposerDraftAsync(draft?.Prompt, draft?.Mentions, draft?.IncludeSceneContext ?? true, files, ct, clearExisting: true);
            _draftLoaded = true;
            _draftDirty = false;
        }
        finally
        {
            _restoringDraft = false;
        }
    }

    private async Task ApplyComposerDraftAsync(
        string? prompt,
        IReadOnlyList<AgentContextMention>? mentions,
        bool includeSceneContext,
        IEnumerable<AgentAttachedFile> attachments,
        CancellationToken ct,
        bool clearExisting = false)
    {
        if (clearExisting)
        {
            ClearAttachments();
            ResetPromptHistory();
        }
        Prompt = prompt;
        RestoreAcceptedMentions(mentions);
        IncludeSceneContext = includeSceneContext;
        foreach (var attachment in attachments)
        {
            if (!string.IsNullOrWhiteSpace(attachment.Path))
                await AddAttachmentAsync(attachment, ct);
        }
    }

    internal bool RecallPrompt(int direction, bool caretAtBoundary)
    {
        if (!CanManage || !ShowComposer || !caretAtBoundary)
            return false;
        if (_browsedHistory is null)
        {
            if (direction > 0 || !string.IsNullOrEmpty(Prompt) || _controller is not { PromptHistory.Count: > 0 } controller)
                return false;
            _browsedHistory = controller.PromptHistory;
            _historyIndex = _browsedHistory.Count;
            _savedPrompt = Prompt;
        }

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _browsedHistory.Count);
        _recallingPrompt = true;
        try
        {
            if (_historyIndex < _browsedHistory.Count)
                Prompt = _browsedHistory[_historyIndex];
            else
            {
                Prompt = _savedPrompt;
                ResetPromptHistory();
            }
        }
        finally
        {
            _recallingPrompt = false;
        }
        return true;
    }

    private void ResetPromptHistory()
    {
        _browsedHistory = null;
        _savedPrompt = null;
    }
}
