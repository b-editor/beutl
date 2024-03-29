﻿namespace Beutl.Controls.Navigation;

public abstract class PageContext
{
    private readonly TaskCompletionSource<INavigationProvider> _tcs = new();

    protected Task<INavigationProvider> GetNavigation()
    {
        return _tcs.Task;
    }

    internal void SetNavigation(INavigationProvider navigation)
    {
        _tcs.TrySetResult(navigation);
    }
}
