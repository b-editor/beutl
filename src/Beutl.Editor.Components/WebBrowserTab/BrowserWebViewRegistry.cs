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

    internal static Func<Task>? CreateCookieClearAction()
    {
        if (OperatingSystem.IsLinux())
        {
            if (CreateLinuxClearAction() == null) return null;
            // Resolve the handles again when invoked, since the registered views may have changed.
            return static () => (CreateLinuxClearAction() ?? throw new InvalidOperationException(Strings.BrowserCookiesUnsupported))();
        }

        if (GetCookieManager() == null) return null;
        return static async () =>
        {
            var manager = GetCookieManager() ?? throw new InvalidOperationException(Strings.BrowserCookiesUnsupported);
            foreach (var cookie in await manager.GetCookiesAsync())
                manager.DeleteCookie(cookie);
        };
    }

    private static Func<Task>? CreateLinuxClearAction() => LinuxWebKitCookies.CreateClearAction(s_views.Select(reference =>
        reference.TryGetTarget(out var view) ? view.TryGetPlatformHandle() : null));

    private static NativeWebViewCookieManager? GetCookieManager()
    {
        foreach (var reference in s_views)
        {
            if (reference.TryGetTarget(out var view) && view.TryGetCookieManager() is { } manager)
                return manager;
        }
        return null;
    }
}
