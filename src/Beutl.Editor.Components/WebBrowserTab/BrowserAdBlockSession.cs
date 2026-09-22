using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Beutl.Editor.Components.WebBrowserTab;

internal interface IBrowserAdBlockBackend : IDisposable
{
    Task EnableAsync();
}

internal sealed class BrowserAdBlockSession : IDisposable
{
    private readonly NativeWebView _webView;
    private readonly BrowserProfile _profile;
    private readonly Action<string> _reportError;
    private readonly Func<IPlatformHandle, BrowserAdBlockRules, Task<IBrowserAdBlockBackend>> _createBackend;
    private IBrowserAdBlockBackend? _backend;
    private BrowserAdBlockRules? _activeRules;
    private bool _refreshPending;
    private IPlatformHandle? _handle;
    private Uri? _pendingNavigation;
    private bool _enabled;
    private bool _disposed;
    private int _revision;
    internal bool IsPreparing { get; private set; }

    internal BrowserAdBlockSession(NativeWebView webView, BrowserProfile profile, Uri initialUri, Action<string> reportError,
        Func<IPlatformHandle, BrowserAdBlockRules, Task<IBrowserAdBlockBackend>>? createBackend = null)
    {
        _webView = webView;
        _profile = profile;
        _reportError = reportError;
        _createBackend = createBackend ?? ((handle, rules) => BrowserAdBlockBackend.CreateAsync(handle, rules, () => webView.Source));
        _enabled = profile.BlockAds;
        IsPreparing = _enabled;
        if (_enabled && BrowserMediaDownload.IsHttpUri(initialUri)) _pendingNavigation = initialUri;
        webView.AdapterCreated += OnAdapterCreated;
        webView.AdapterDestroyed += OnAdapterDestroyed;
        webView.NavigationStarted += OnNavigationStarted;
        webView.NavigationCompleted += OnNavigationCompleted;
        profile.SettingsChanged += OnSettingsChanged;
        profile.AdBlockFilters.Changed += OnFiltersChanged;
        if (webView.TryGetPlatformHandle() is { } handle) Attach(handle);
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
        if (e.TryGetPlatformHandle() is { } handle) Attach(handle);
    }

    internal void Attach(IPlatformHandle handle)
    {
        _handle = handle;
        _ = RefreshAsync();
    }

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        _revision++;
        _handle = null;
        _backend?.Dispose();
        _backend = null;
        _activeRules = null;
        IsPreparing = _enabled;
        if (_enabled && BrowserMediaDownload.IsHttpUri(_webView.Source)) _pendingNavigation = _webView.Source;
    }

    private void OnSettingsChanged()
    {
        if (_enabled == _profile.BlockAds) return;
        _enabled = _profile.BlockAds;
        _ = RefreshAsync();
    }

    private void OnFiltersChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || ReferenceEquals(_activeRules, _profile.AdBlockFilters.Current)) return;
        if (IsPreparing) _refreshPending = true;
        else _ = RefreshAsync();
    });

    internal async Task RefreshAsync()
    {
        if (_disposed) return;
        if (_enabled && _backend != null && ReferenceEquals(_activeRules, _profile.AdBlockFilters.Current)) return;
        int revision = ++_revision;
        if (!_enabled)
        {
            _backend?.Dispose();
            _backend = null;
            _activeRules = null;
            IsPreparing = false;
            ResumeNavigation();
            await ApplyCosmeticsAsync();
            return;
        }
        // Only defer an initial/reattached document. macOS reports subframe starts through
        // NavigationStarted, so a background iframe must never become a top-level replay
        // when filters are changed while an existing page is open.
        IsPreparing = _pendingNavigation != null || !BrowserMediaDownload.IsHttpUri(_webView.Source);
        if (_handle == null) return;
        IBrowserAdBlockBackend? candidate = null;
        try
        {
            BrowserAdBlockRules rules = await _profile.AdBlockFilters.GetAsync(_profile.AdBlockListUrls);
            if (_disposed || revision != _revision) return;
            candidate = await _createBackend(_handle, rules);
            if (_disposed || revision != _revision) return;
            await candidate.EnableAsync();
            if (_disposed || revision != _revision) return;
            _backend?.Dispose();
            _backend = candidate;
            _activeRules = rules;
            candidate = null;
        }
        catch (Exception ex)
        {
            if (!_disposed && revision == _revision)
                _reportError(string.Format(Strings.BrowserAdBlockFailed, ex.Message));
        }
        finally
        {
            candidate?.Dispose();
            if (!_disposed && revision == _revision)
            {
                IsPreparing = false;
                ResumeNavigation();
                if (_refreshPending)
                {
                    _refreshPending = false;
                    _ = RefreshAsync();
                }
            }
        }
        if (!_disposed && revision == _revision) await ApplyCosmeticsAsync();
    }

    internal void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (!_disposed && IsPreparing && e.Request is { } uri && BrowserMediaDownload.IsHttpUri(uri))
        {
            e.Cancel = true;
            _pendingNavigation = uri;
        }
    }

    private void ResumeNavigation()
    {
        Uri? pending = _pendingNavigation;
        _pendingNavigation = null;
        if (pending != null) _webView.Source = pending;
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (e.IsSuccess && !IsPreparing) _ = ApplyCosmeticsAsync();
    }

    private async Task ApplyCosmeticsAsync()
    {
        if (_disposed || (_activeRules ?? _profile.AdBlockFilters.Current) is not { } rules || !BrowserMediaDownload.IsHttpUri(_webView.Source)) return;
        // WebKit installs CSS rules natively, including subframes. WebView2 uses the same
        // parsed selectors in the loaded top-level page; network filtering remains native.
        if (_handle is not IWindowsWebView2PlatformHandle) return;
        try { await _webView.InvokeScript(rules.GetCosmeticScript(_webView.Source, _enabled && _backend != null)); }
        catch (Exception) { /* A page may navigate or close before script evaluation finishes. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _revision++;
        _pendingNavigation = null;
        _webView.AdapterCreated -= OnAdapterCreated;
        _webView.AdapterDestroyed -= OnAdapterDestroyed;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _profile.SettingsChanged -= OnSettingsChanged;
        _profile.AdBlockFilters.Changed -= OnFiltersChanged;
        _backend?.Dispose();
        _backend = null;
    }
}

internal static class BrowserAdBlockBackend
{
    internal static Task<IBrowserAdBlockBackend> CreateAsync(IPlatformHandle handle, BrowserAdBlockRules rules, Func<Uri> getPage) => handle switch
    {
        IAppleWKWebViewPlatformHandle apple when OperatingSystem.IsMacOS() => MacOSAdBlockBackend.CreateAsync(apple, rules),
        IWindowsWebView2PlatformHandle windows when OperatingSystem.IsWindows() => Task.FromResult<IBrowserAdBlockBackend>(new WindowsAdBlockBackend(windows, rules, getPage)),
        IGtkWebViewPlatformHandle gtk when OperatingSystem.IsLinux() => LinuxAdBlockBackend.CreateAsync(gtk.WebKitWebView, false, rules),
        ILinuxWpePlatformHandle wpe when OperatingSystem.IsLinux() => LinuxAdBlockBackend.CreateAsync(wpe.WebKitWebView, true, rules),
        _ => throw new NotSupportedException(Strings.BrowserAdBlockUnsupported)
    };
}
