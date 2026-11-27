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
    private CancellationTokenSource? _draftSyncCancellation;
    private readonly List<AgentAttachedFile> _pendingSyncedAttachments = [];
    private int _historyIndex;
    private IReadOnlyList<string>? _browsedHistory;
    private string? _savedPrompt;
    private bool _recallingPrompt;

    private string CurrentDraftKey => _selectedThread is { } thread
        ? AgentDraftStore.Key(thread.Id)
        : _newThreadDraftKey ?? AgentDraftStore.DefaultDraftKey;

    private void MarkDraftChanged()
    {
        if (!_draftLoaded || _restoringDraft || _disposed)
            return;
        _draftDirty = true;
        _sessions.PublishDraft(CurrentDraftKey, CreateDraftSnapshot(), this);
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

    private AgentDraft? CreateDraftSnapshot()
    {
        string? text = _browsedHistory is null ? Prompt : _savedPrompt;
        var paths = new List<string>(_attachments.Count + _pendingSyncedAttachments.Count);
        var mediaTypes = new List<string?>(_attachments.Count + _pendingSyncedAttachments.Count);
        var seen = new HashSet<string>(s_pathComparer);
        foreach (var item in _attachments)
        {
            if (seen.Add(item.Path))
            {
                paths.Add(item.Path);
                mediaTypes.Add(item.File.MediaType);
            }
        }
        foreach (var pending in _pendingSyncedAttachments)
        {
            if (seen.Add(pending.Path))
            {
                paths.Add(pending.Path);
                mediaTypes.Add(pending.MediaType);
            }
        }

        return string.IsNullOrEmpty(text) && paths.Count == 0 && IncludeSceneContext
            ? null
            : new AgentDraft(text, paths.ToArray(), IncludeSceneContext,
                mediaTypes.ToArray(), GetAcceptedMentions(text));
    }

    private async Task SaveDraftAsync()
    {
        _draftTimer.Stop();
        if (!_draftLoaded || !_draftDirty)
            return;
        _draftDirty = false;

        string draftKey = CurrentDraftKey;
        AgentDraft? draft = CreateDraftSnapshot();
        try
        {
            await _sessions.SaveDraftByKeyAsync(draftKey, draft, this);
        }
        catch (Exception ex)
        {
            if (draftKey == CurrentDraftKey)
                _draftDirty = true;
            ReportError(ex);
        }
    }

    private void OnDraftChanged(string draftKey, AgentDraft? draft, object? source)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Post(() => OnDraftChanged(draftKey, draft, source));
            return;
        }
        if (_disposed || ReferenceEquals(source, this) || !_draftLoaded || draftKey != CurrentDraftKey)
            return;

        _draftTimer.Stop();
        _draftDirty = false;
        CancelDraftSync();

        var incomingFiles = draft is null
            ? []
            : draft.AttachmentPaths
                .Select((path, index) => new AgentAttachedFile(path, draft.AttachmentMediaTypes?.ElementAtOrDefault(index)))
                .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                .ToArray();
        var incomingPaths = new HashSet<string>(incomingFiles.Select(file => file.Path), s_pathComparer);

        _restoringDraft = true;
        try
        {
            if (Prompt != draft?.Prompt)
                ResetPromptHistory();
            Prompt = draft?.Prompt;
            RestoreAcceptedMentions(draft?.Mentions);
            IncludeSceneContext = draft?.IncludeSceneContext ?? true;
            bool removedAny = false;
            for (int index = _attachments.Count - 1; index >= 0; index--)
            {
                if (!incomingPaths.Contains(_attachments[index].Path))
                {
                    var removed = _attachments[index];
                    _attachments.RemoveAt(index);
                    removed.Dispose();
                    removedAny = true;
                }
            }
            if (removedAny)
                NotifyPromptActions();
        }
        finally
        {
            _restoringDraft = false;
        }

        var missing = incomingFiles
            .Where(file => !_attachments.Any(item => s_pathComparer.Equals(item.Path, file.Path)))
            .ToArray();
        if (missing.Length > 0)
        {
            _pendingSyncedAttachments.AddRange(missing);
            var cancellation = _draftSyncCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _ = SyncMissingAttachmentsAsync(draftKey, missing, cancellation);
        }
    }

    private async Task SyncMissingAttachmentsAsync(
        string draftKey, AgentAttachedFile[] files, CancellationTokenSource cancellation)
    {
        var ct = cancellation.Token;
        try
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var attachment = await Task.Run(() => AgentAttachment.CreateFile(file, RemoveAttachment), ct);
                if (ct.IsCancellationRequested || _draftSyncCancellation != cancellation || _disposed || draftKey != CurrentDraftKey)
                {
                    attachment.Dispose();
                    return;
                }
                bool stillPending = RemovePendingSyncedAttachment(file.Path);
                if (!stillPending || _attachments.Any(item => s_pathComparer.Equals(item.Path, attachment.Path)))
                {
                    attachment.Dispose();
                    continue;
                }
                _restoringDraft = true;
                try
                {
                    _attachments.Add(attachment);
                    NotifyPromptActions();
                }
                finally
                {
                    _restoringDraft = false;
                }
            }
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
            if (_draftSyncCancellation == cancellation)
                _draftSyncCancellation = null;
            cancellation.Dispose();
        }
    }

    private bool RemovePendingSyncedAttachment(string path)
    {
        for (int index = 0; index < _pendingSyncedAttachments.Count; index++)
        {
            if (s_pathComparer.Equals(_pendingSyncedAttachments[index].Path, path))
            {
                _pendingSyncedAttachments.RemoveAt(index);
                return true;
            }
        }
        return false;
    }

    private void CancelDraftSync()
    {
        _pendingSyncedAttachments.Clear();
        _draftSyncCancellation?.Cancel();
        _draftSyncCancellation = null;
    }

    private async Task RestoreDraftAsync(AgentDraft? draft, CancellationToken ct)
    {
        CancelDraftSync();
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
