using Avalonia.Controls;

namespace Beutl.Editor.Components.WebBrowserTab;

internal static class BrowserWebViewRegistry
{
    private static readonly List<WeakReference<NativeWebView>> s_views = [];
    internal static event Action? Changed;

    internal static void Register(NativeWebView view)
    {
        s_views.RemoveAll(item => !item.TryGetTarget(out _));
        if (!s_views.Any(item => item.TryGetTarget(out var target) && ReferenceEquals(target, view)))
        {
            s_views.Add(new(view));
            view.AdapterCreated += OnAdapterChanged;
            view.AdapterDestroyed += OnAdapterChanged;
        }
        Changed?.Invoke();
    }

    internal static void Unregister(NativeWebView view)
    {
        view.AdapterCreated -= OnAdapterChanged;
        view.AdapterDestroyed -= OnAdapterChanged;
        s_views.RemoveAll(item => !item.TryGetTarget(out var target) || ReferenceEquals(target, view));
        Changed?.Invoke();
    }

    private static void OnAdapterChanged(object? sender, WebViewAdapterEventArgs e) => Changed?.Invoke();

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
