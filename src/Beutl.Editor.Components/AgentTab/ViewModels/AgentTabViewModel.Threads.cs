using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private AgentThreadRow[] _threadRows = [];
    private Guid? _deletedSelection;

    public string? ThreadSearch
    {
        get;
        set
        {
            if (Set(ref field, value))
                FilterThreadRows();
        }
    }

    public IReadOnlyList<AgentThreadRow> FilteredThreads
    {
        get;
        private set => Set(ref field, value);
    } = [];

    public IReadOnlyList<AgentThreadRow> RecentThreads
    {
        get;
        private set => Set(ref field, value);
    } = [];

    private void UpdateThreadRows()
    {
        _threadRows = _threads.OrderByDescending(thread => thread.UpdatedAt).Select(thread =>
        {
            string providerName = _detectedProviders.FirstOrDefault(provider => provider.Descriptor.Id == thread.ProviderId)?.Name
                ?? thread.ProviderId;
            var row = new AgentThreadRow(thread, providerName, _selectedThread?.Id == thread.Id);
            row.UpdateState(_sessions.Find(thread.Id));
            return row;
        }).ToArray();
        RecentThreads = _threadRows.Take(3).ToArray();
        FilterThreadRows();
    }

    private void FilterThreadRows()
    {
        string? term = ThreadSearch?.Trim();
        FilteredThreads = string.IsNullOrEmpty(term)
            ? _threadRows
            : _threadRows.Where(row => row.Thread.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.ProviderName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.Thread.ProviderId.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void OnThreadStateChanged(Guid threadId)
        => _threadRows.FirstOrDefault(row => row.Thread.Id == threadId)?.UpdateState(_sessions.Find(threadId));

    private void OnThreadMetadataChanged(Guid threadId, AgentThreadMetadata? metadata)
    {
        if (_disposed)
            return;
        int index = _threads.ToList().FindIndex(thread => thread.Id == threadId);
        if (metadata is not null)
        {
            if (index < 0)
                _threads.Insert(0, metadata);
            else
                _threads[index] = metadata;
            if (_selectedThread?.Id == threadId)
                ApplyThread(metadata, preserveDraft: true);
            UpdateThreadRows();
            return;
        }

        if (index >= 0)
            _threads.RemoveAt(index);
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
}

internal sealed class AgentThreadRow(AgentThreadMetadata thread, string providerName, bool selected) : AgentBindable
{
    public AgentThreadMetadata Thread { get; } = thread;
    public string ProviderName { get; } = providerName;
    public bool IsSelected { get; } = selected;
    public string UpdatedAt => Thread.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public bool IsRunning { get; private set => Set(ref field, value); }
    public bool NeedsAttention { get; private set => Set(ref field, value); }
    public bool ShowsRunningIndicator { get; private set => Set(ref field, value); }
    public bool CanDelete { get; private set => Set(ref field, value); } = true;

    internal void UpdateState(AgentThreadController? controller)
    {
        IsRunning = controller?.IsRunning == true;
        NeedsAttention = controller?.NeedsAttention == true;
        ShowsRunningIndicator = IsRunning && !NeedsAttention;
        CanDelete = controller?.CanConfigure ?? true;
    }
}
