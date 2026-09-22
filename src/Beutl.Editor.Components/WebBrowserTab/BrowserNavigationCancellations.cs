namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class BrowserNavigationCancellations(Action<nint> retain, Action<nint> release) : IDisposable
{
    private readonly HashSet<nint> _canceled = [];

    internal nint Current { get; private set; }

    internal void BeginNavigation(nint navigation)
    {
        if (Current == navigation) return;
        if (navigation != 0) retain(navigation);
        CommitNavigation(Current);
        Current = navigation;
    }

    internal bool MarkCanceled(nint navigation)
    {
        if (navigation == 0 || !_canceled.Add(navigation)) return false;
        retain(navigation);
        return true;
    }

    internal bool ForgetCancellation(nint navigation)
    {
        if (!_canceled.Remove(navigation)) return false;
        release(navigation);
        return true;
    }

    internal void CommitNavigation(nint navigation)
    {
        if (Current == 0 || Current != navigation) return;
        release(Current);
        Current = 0;
    }

    internal bool CompleteNavigation(nint navigation)
    {
        bool canceled = ForgetCancellation(navigation);
        CommitNavigation(navigation);
        return canceled;
    }

    public void Dispose()
    {
        CommitNavigation(Current);
        foreach (nint navigation in _canceled) release(navigation);
        _canceled.Clear();
    }
}
