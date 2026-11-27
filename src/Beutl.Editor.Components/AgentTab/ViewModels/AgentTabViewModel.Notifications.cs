using Avalonia.Threading;
using Reactive.Bindings;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private readonly HashSet<Guid> _notifiedThreads = [];
    private readonly ReactivePropertySlim<string> _header = new(AgentStrings.Title);
    private Guid? _viewedThread;

    public bool HasBackgroundThreadActivity => _threadRows.Any(row => !row.IsSelected && (row.NeedsAttention || row.IsUnread));

    private void OnTabSelectionChanged(bool selected)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Post(() => OnTabSelectionChanged(IsSelected.Value));
            return;
        }

        UpdateThreadViewer();
        if (selected)
        {
            _notifiedThreads.Clear();
            _header.Value = AgentStrings.Title;
            if (_viewedThread is { } threadId)
                _ = _sessions.MarkReadAsync(_host.Runtime, threadId, _lifetime.Token);
        }
    }

    private void UpdateThreadViewer()
    {
        Guid? next = !_disposed && IsSelected.Value ? _controller?.ThreadId : null;
        if (_viewedThread == next)
            return;

        if (_viewedThread is { } previous)
            _sessions.DetachViewer(previous);
        _viewedThread = next;
        if (next is { } id)
            _sessions.AttachViewer(id);
    }

    private void OnActivityNotified(Guid threadId)
    {
        if (!IsSelected.Value)
        {
            _notifiedThreads.Add(threadId);
            UpdateNotificationHeader();
        }
    }

    private void UpdateNotificationHeader()
    {
        if (_notifiedThreads.Count == 0 || IsSelected.Value)
            _header.Value = AgentStrings.Title;
        else
        {
            bool needsAttention = _notifiedThreads.Any(id => _sessions.Find(id)?.NeedsAttention == true);
            _header.Value = string.Format(CultureInfo.CurrentCulture,
                needsAttention ? AgentStrings.AttentionTabHeader : AgentStrings.UnreadTabHeader, AgentStrings.Title);
        }
    }
}
