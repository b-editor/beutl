using System.Text.RegularExpressions;
using Beutl.Agents;
using Microsoft.Extensions.Logging;
using System.Resources;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private static readonly ResourceManager s_agentResources = new("Beutl.Language.AgentStrings", typeof(AgentStrings).Assembly);
    private AgentThreadRow[] _threadRows = [];
    private Guid? _deletedSelection;
    private CancellationTokenSource? _threadSearchCancellation;
    private IReadOnlyDictionary<Guid, string?> _threadSnippets = new Dictionary<Guid, string?>();

    public string? ThreadSearch
    {
        get;
        set
        {
            if (Set(ref field, value))
                StartThreadBodySearch(reset: true);
        }
    }

    public bool ShowArchived
    {
        get;
        set
        {
            if (Set(ref field, value))
                StartThreadBodySearch(reset: true);
        }
    }

    public IReadOnlyList<AgentThreadRow> FilteredThreads { get; private set => Set(ref field, value); } = [];
    public IReadOnlyList<AgentThreadRow> RecentThreads { get; private set => Set(ref field, value); } = [];

    public string ThreadPinLabel => _selectedThread?.IsPinned == true ? AgentStrings.UnpinThread : AgentStrings.PinThread;
    public string ThreadArchiveLabel => _selectedThread?.IsArchived == true ? AgentStrings.UnarchiveThread : AgentStrings.ArchiveThread;

    internal Task SetPinnedAsync(AgentThreadMetadata thread)
        => ManageAsync(async ct => ReplaceThread(await _host.Runtime.SetPinnedAsync(_projectPath, thread.Id, !thread.IsPinned, ct)));

    internal Task SetArchivedAsync(AgentThreadMetadata thread)
        => ManageAsync(async ct => ReplaceThread(await _host.Runtime.SetArchivedAsync(_projectPath, thread.Id, !thread.IsArchived, ct)));

    public Task ToggleCurrentPinAsync() => _selectedThread is { } thread ? SetPinnedAsync(thread) : Task.CompletedTask;
    public Task ToggleCurrentArchiveAsync() => _selectedThread is { } thread ? SetArchivedAsync(thread) : Task.CompletedTask;

    public Task RegenerateTitleAsync() => ManageAsync(async ct =>
    {
        if (_selectedThread is not { } thread)
            return;
        var entries = await _host.Runtime.LoadTimelineAsync(_projectPath, thread.Id, ct);
        var title = entries.OfType<AgentUserMessage>().Select(message => MakeTitle(message.Text)).FirstOrDefault(title => title is not null)
            ?? entries.OfType<AgentAssistantMessageCompleted>().Where(message => message.ParentToolCallId is null)
                .Select(message => MakeTitle(message.Text)).FirstOrDefault(title => title is not null);
        if (title is not null && title != thread.Title)
            ReplaceThread(await _host.Runtime.RenameThreadAsync(_projectPath, thread.Id, title, ct));
    });

    public Task ForkThreadAsync() => ManageAsync(async ct =>
    {
        if (_controller is { } controller && _selectedThread is { } source
            && await controller.ForkThreadAsync(null, ForkTitle(source.Title), true, ct) is { } fork)
            await LoadThreadAsync(fork, ct);
    });

    private void UpdateThreadRows()
    {
        var previous = _threadRows.ToDictionary(row => row.Thread.Id);
        _threadRows = _threads.OrderByDescending(thread => thread.IsPinned).ThenByDescending(thread => thread.UpdatedAt).Select(thread =>
        {
            string provider = ProviderDisplayName(thread.ProviderId);
            bool isSelected = _selectedThread?.Id == thread.Id;
            var row = previous.GetValueOrDefault(thread.Id) ?? new AgentThreadRow(thread, provider, isSelected);
            row.UpdateThread(thread, provider, isSelected);
            row.UpdateState(_sessions.Find(thread.Id));
            return row;
        }).ToArray();
        var recent = _threadRows.Where(row => !row.Thread.IsArchived).Take(3).ToArray();
        if (!RecentThreads.SequenceEqual(recent))
            RecentThreads = recent;
        FilterThreadRows();
        Notify(nameof(HasBackgroundThreadActivity));
        Notify(nameof(ThreadPickerTooltip));
    }

    private string ProviderDisplayName(string id)
        => _detectedProviders.FirstOrDefault(provider => provider.Descriptor.Id == id)?.Name ?? id;

    private void FilterThreadRows()
    {
        string? term = ThreadSearch?.Trim();
        var rows = _threadRows.Where(row => ShowArchived || !row.Thread.IsArchived);
        var filtered = rows.Where(row =>
        {
            _threadSnippets.TryGetValue(row.Thread.Id, out string? snippet);
            row.MatchSnippet = snippet;
            return string.IsNullOrEmpty(term) || snippet is not null
                || row.Thread.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.ProviderName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.Thread.ProviderId.Contains(term, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (!FilteredThreads.SequenceEqual(filtered))
            FilteredThreads = filtered;
    }

    private void StartThreadBodySearch(bool reset = false)
    {
        CancelThreadBodySearch();
        if (reset)
        {
            _threadSnippets = new Dictionary<Guid, string?>();
            FilterThreadRows();
        }
        if (!_disposed && ThreadSearch?.Trim() is { Length: > 0 } query)
        {
            var cancellation = _threadSearchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _sessions.Lifetime);
            _sessions.TrackMetadataWork(SearchThreadBodiesAsync(query, ShowArchived, cancellation));
        }
    }

    internal void CancelThreadBodySearch()
    {
        _threadSearchCancellation?.Cancel();
        _threadSearchCancellation = null;
    }

    private async Task SearchThreadBodiesAsync(string query, bool includeArchived, CancellationTokenSource cancellation)
    {
        var ct = cancellation.Token;
        try
        {
            await Task.Delay(120, ct);
            var results = await _host.Runtime.SearchThreadsAsync(_projectPath, query, includeArchived, ct);
            if (!ct.IsCancellationRequested)
            {
                _threadSnippets = results.Where(hit => hit.MatchSnippet is not null).ToDictionary(hit => hit.Thread.Id, hit => hit.MatchSnippet);
                FilterThreadRows();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not search agent thread messages.");
        }
        finally
        {
            if (_threadSearchCancellation == cancellation)
                _threadSearchCancellation = null;
            cancellation.Dispose();
        }
    }

    private void OnThreadStateChanged(Guid threadId)
    {
        _threadRows.FirstOrDefault(row => row.Thread.Id == threadId)?.UpdateState(_sessions.Find(threadId));
        Notify(nameof(HasBackgroundThreadActivity));
        Notify(nameof(ThreadPickerTooltip));
        UpdateNotificationHeader();
    }

    private void OnThreadActivityChanged(Guid threadId, DateTimeOffset? activity, DateTimeOffset? read)
    {
        if (_disposed)
            return;
        int index = FindThreadIndex(threadId);
        if (index < 0)
            return;

        var current = _threads[index];
        var updated = MergeActivity(current, activity, read);
        if (updated == current)
            return;
        _threads[index] = updated;
        if (_selectedThread?.Id == threadId)
        {
            _selectedThread = updated;
            Notify(nameof(SelectedThread));
        }
        UpdateThreadRows();
        if (updated.LastActivityAt != current.LastActivityAt)
            StartThreadBodySearch();
    }

    private static AgentThreadMetadata MergeActivity(AgentThreadMetadata thread, DateTimeOffset? activity, DateTimeOffset? read)
        => thread with
        {
            LastActivityAt = thread.LastActivityAt is null || activity > thread.LastActivityAt ? activity : thread.LastActivityAt,
            LastReadAt = thread.LastReadAt is null || read > thread.LastReadAt ? read : thread.LastReadAt,
            UpdatedAt = activity is { } timestamp && timestamp > thread.UpdatedAt ? timestamp : thread.UpdatedAt
        };

    private int FindThreadIndex(Guid threadId)
    {
        for (int index = 0; index < _threads.Count; index++)
            if (_threads[index].Id == threadId)
                return index;
        return -1;
    }

    private void OnThreadMetadataChanged(Guid threadId, AgentThreadMetadata? metadata)
    {
        if (_disposed)
            return;
        int index = FindThreadIndex(threadId);
        if (metadata is not null)
        {
            if (index < 0)
                _threads.Insert(0, metadata);
            else
            {
                var current = _threads[index];
                metadata = MergeActivity(metadata, current.LastActivityAt, current.LastReadAt);
                _threads[index] = metadata;
            }
            if (_selectedThread?.Id == threadId)
                ApplyThread(metadata, preserveDraft: true);
            UpdateThreadRows();
            if (index < 0)
                StartThreadBodySearch();
            return;
        }

        if (index >= 0)
            _threads.RemoveAt(index);
        if (_notifiedThreads.Remove(threadId))
            UpdateNotificationHeader();
        UpdateThreadRows();
        if (_selectedThread?.Id == threadId)
        {
            _draftTimer.Stop();
            _draftLoaded = _draftDirty = false;
            _deletedSelection = threadId;
            if (CanManage)
                _ = ManageAsync(ct => LoadThreadAsync(RecentThreads.FirstOrDefault()?.Thread, ct));
        }
    }

    private static bool IsPlaceholderTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == AgentStrings.PastedText)
            return true;
        return new[] { "", "ja", "es", "ko-KR", "zh-CN" }.Any(locale =>
            string.Equals(text, s_agentResources.GetString(nameof(AgentStrings.NewThread), CultureInfo.GetCultureInfo(locale)),
                StringComparison.OrdinalIgnoreCase));
    }

    private static string? MakeTitle(string text)
    {
        bool inFence = false;
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence || line.StartsWith('/') || line.StartsWith('$') || line.StartsWith('@') && !line.Contains(' '))
                continue;
            line = Regex.Replace(line, @"^\s*(?:#{1,6}\s+|>\s*|[-+*]\s+|\d+[.)]\s+)+", "");
            line = Regex.Replace(line, @"!?\[([^\]]*)\]\([^\r\n)]*\)", "$1");
            line = Regex.Replace(line, @"(?<!\S)(?:https?://\S+|[$@][\p{L}\p{N}_.:-]+)(?=\s|$)", "");
            line = Regex.Replace(line.Replace("`", "").Replace("**", "").Replace("__", "").Replace("~~", ""), @"\s+", " ")
                .Trim(' ', '*', '_', '-', '.', '!', '?', '。', '！', '？');
            if (IsPlaceholderTitle(line) || Regex.IsMatch(line, @"^(?:hi|hello|hey|test|hola|prueba|こんにちは|テスト|안녕하세요|你好|测试)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;
            int[] characters = StringInfo.ParseCombiningCharacters(line);
            return characters.Length > 60 ? line[..characters[59]].TrimEnd() + "…" : line;
        }
        return null;
    }
}

internal sealed class AgentThreadRow(AgentThreadMetadata thread, string providerName, bool selected) : AgentBindable
{
    public AgentThreadMetadata Thread { get; private set => Set(ref field, value); } = thread;
    public string ProviderName { get; private set => Set(ref field, value); } = providerName;
    public bool IsSelected { get; private set => Set(ref field, value); } = selected;
    public string PinLabel => Thread.IsPinned ? AgentStrings.UnpinThread : AgentStrings.PinThread;
    public string ArchiveLabel => Thread.IsArchived ? AgentStrings.UnarchiveThread : AgentStrings.ArchiveThread;
    public bool IsUnread => !IsSelected && Thread.IsUnread;
    public string? MatchSnippet { get; set => Set(ref field, value); }
    public string UpdatedAt => Thread.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public bool IsRunning { get; private set => Set(ref field, value); }
    public bool NeedsAttention { get; private set => Set(ref field, value); }
    public bool ShowsRunningIndicator { get; private set => Set(ref field, value); }
    public bool CanDelete { get; private set => Set(ref field, value); } = true;

    internal void UpdateThread(AgentThreadMetadata metadata, string provider, bool isSelected)
    {
        bool threadChanged = Thread != metadata;
        bool selectionChanged = IsSelected != isSelected;
        Thread = metadata;
        ProviderName = provider;
        IsSelected = isSelected;
        if (threadChanged)
        {
            Notify(nameof(PinLabel));
            Notify(nameof(ArchiveLabel));
            Notify(nameof(UpdatedAt));
        }
        if (threadChanged || selectionChanged)
            Notify(nameof(IsUnread));
    }

    internal void UpdateState(AgentThreadController? controller)
    {
        IsRunning = controller?.IsRunning == true;
        NeedsAttention = controller?.NeedsAttention == true;
        ShowsRunningIndicator = IsRunning && !NeedsAttention;
        CanDelete = controller?.CanConfigure ?? true;
    }
}
