using Avalonia.Controls;

namespace Beutl.Editor.Components.WebBrowserTab;

internal static class BrowserWebViewRegistry
{
    private static readonly List<WeakReference<NativeWebView>> s_views = [];

    internal static void Register(NativeWebView view)
    {
        s_views.RemoveAll(item => !item.TryGetTarget(out _));
        s_views.Add(new(view));
    }

    internal static void Unregister(NativeWebView view) =>
        s_views.RemoveAll(item => !item.TryGetTarget(out var target) || ReferenceEquals(target, view));

    internal static NativeWebViewCookieManager? GetCookieManager()
    {
        foreach (var reference in s_views)
        {
            if (reference.TryGetTarget(out var view) && view.TryGetCookieManager() is { } manager)
                return manager;
        }
        return null;
    }
}
