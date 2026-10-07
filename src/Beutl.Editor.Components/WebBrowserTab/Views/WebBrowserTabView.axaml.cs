using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView : UserControl, IDisposable, IWebViewReparentingContent
{
    internal static readonly Uri LinuxWebViewSetupGuide = new(
        "https://docs.avaloniaui.net/docs/app-development/embedding-web-content#linux");

    private readonly Func<Uri, NativeWebView> _createWebView;
    private readonly Func<(bool IsAvailable, string? Detail, bool ShowLinuxRuntimeHelp)> _getWebViewAvailability;
    private readonly Func<Uri, Task<bool>>? _launchInDefaultBrowser;
    private readonly Func<NativeWebView, Task<string?>> _getPageTitle;
    private readonly Func<NativeWebView, bool> _canReparentWebView;
    private readonly bool _navigationStartedIncludesSubframes;
    private NativeWebView? _webView;
    private BrowserAdBlockSession? _adBlockSession;
    private IDisposable? _nativeDownloadHandler;
    private int _nativeDownloadHandlerVersion;
    private WebBrowserTabViewModel? _viewModel;
    private bool _disposed;

    public WebBrowserTabView()
        : this(static uri => new NativeWebView { Source = uri }, GetWebViewAvailability, faviconLoader: BrowserFaviconLoader.Default)
    {
    }

    internal WebBrowserTabView(
        Func<Uri, NativeWebView> createWebView,
        Func<(bool IsAvailable, string? Detail, bool ShowLinuxRuntimeHelp)> getWebViewAvailability,
        Func<Uri, Task<bool>>? launchInDefaultBrowser = null,
        Func<NativeWebView, Task<string?>>? getPageTitle = null,
        Func<NativeWebView, bool>? canReparentWebView = null,
        bool? navigationStartedIncludesSubframes = null,
        BrowserFaviconLoader? faviconLoader = null)
    {
        _createWebView = createWebView;
        _getWebViewAvailability = getWebViewAvailability;
        _launchInDefaultBrowser = launchInDefaultBrowser;
        _getPageTitle = getPageTitle ?? (static webView => webView.InvokeScript("document.title"));
        _canReparentWebView = canReparentWebView ?? (static webView => webView.TryGetPlatformHandle() is not null);
        _navigationStartedIncludesSubframes = navigationStartedIncludesSubframes ?? OperatingSystem.IsMacOS();
        FaviconLoader = faviconLoader;
        InitializeComponent();
        AddressTextBox.SearchSuggestionsChanged += OnSearchSuggestionsChanged;
        AddHandler(KeyDownEvent, OnBrowserKeyDown, RoutingStrategies.Tunnel);
        Loaded += OnLoaded;
    }

    public BrowserFaviconLoader? FaviconLoader { get; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_disposed) return;
        var viewModel = DataContext as WebBrowserTabViewModel;
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        WebBrowserTabViewModel? previousViewModel = _viewModel;
        ResetPageStateForRebind();
        UnsubscribeFromViewModel();
        _viewModel = viewModel;
        if (viewModel == null)
        {
            AddressTextBox.SuggestionsEnabled = false;
            AddressTextBox.SuggestionProvider = static (_, _) => Task.FromResult<IReadOnlyList<string>>([]);
            DisposeWebView();
            UpdateBlankPageState();
            OnCancelBookmarkEditorClick(this, new RoutedEventArgs());
            return;
        }
        if (_webView != null && previousViewModel != null && _webView.Source == viewModel.CurrentUri)
        {
            // The document is still displayed, so keep its commit across a context rebind.
            // Source may be a provisional URL; the previous model knows the last loaded page.
            viewModel.AdoptCommittedPage(previousViewModel);
        }
        AttachToViewModel(viewModel);
    }

    // Work, offers and panels that belong to the page the previous context was showing.
    private void ResetPageStateForRebind()
    {
        _adBlockSession?.Dispose();
        _adBlockSession = null;
        _downloadCancellation?.Cancel();
        ClearDeferredNativeDownloads();
        ResetPageDownloadRequests();
        CloseBrowserPanel();
        _pageRevision++;
        _findRequest?.Cancel();
        AddressTextBox.CancelSearchSuggestions();
    }

    private void UnsubscribeFromViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.Disposing -= Dispose;
            _viewModel.Profile.SettingsChanged -= OnProfileChanged;
            _viewModel.Profile.Bookmarks.CollectionChanged -= OnBookmarksChanged;
            _viewModel.Profile.Downloads.CollectionChanged -= OnDownloadHistoryChanged;
        }
    }

    private void AttachToViewModel(WebBrowserTabViewModel viewModel)
    {
        viewModel.Disposing += Dispose;
        viewModel.Profile.SettingsChanged += OnProfileChanged;
        viewModel.Profile.Bookmarks.CollectionChanged += OnBookmarksChanged;
        viewModel.Profile.Downloads.CollectionChanged += OnDownloadHistoryChanged;
        UpdateBlankPageState();
        OnCancelBookmarkEditorClick(this, new RoutedEventArgs());
        OnProfileChanged();

        if (_webView != null)
            _adBlockSession = new BrowserAdBlockSession(_webView, viewModel.Profile, viewModel.CurrentUri, ShowToolStatus);

        if (_webView != null && _webView.Source != viewModel.CurrentUri)
        {
            InvalidatePageDownloadRequests(navigationStarted: true);
            viewModel.BeginNavigation(viewModel.CurrentUri);
            _webView.Source = viewModel.CurrentUri;
        }

        EnsureWebView();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureWebView();
    }

    private void EnsureWebView()
    {
        if (_disposed || _viewModel == null || _webView != null)
        {
            return;
        }

        (bool isAvailable, string? detail, bool showLinuxRuntimeHelp) = _getWebViewAvailability();
        if (!isAvailable)
        {
            _viewModel.SetWebViewUnavailable(detail, showLinuxRuntimeHelp);
            return;
        }

        Uri initialUri = _viewModel.CurrentUri;
        bool downloadInitialMedia = BrowserMediaDownload.IsMediaLink(initialUri);
        if (downloadInitialMedia)
            _viewModel.CompleteNavigation(WebBrowserTabViewModel.BlankPage, true, false, false);
        if (initialUri != WebBrowserTabViewModel.BlankPage && !downloadInitialMedia)
            _viewModel.BeginNavigation(initialUri);
        NativeWebView webView = _createWebView(downloadInitialMedia || _viewModel.Profile.BlockAds ? WebBrowserTabViewModel.BlankPage : initialUri);
        _adBlockSession = new BrowserAdBlockSession(webView, _viewModel.Profile,
            downloadInitialMedia ? WebBrowserTabViewModel.BlankPage : initialUri, ShowToolStatus);
        if (OperatingSystem.IsMacOS())
        {
            webView.EnvironmentRequested += ConfigureMacOSWebViewEnvironment;
        }
        webView.AdapterCreated += OnAdapterCreated;
        webView.AdapterDestroyed += OnAdapterDestroyed;
        webView.NavigationStarted += OnNavigationStarted;
        webView.NavigationCompleted += OnNavigationCompleted;
        webView.NewWindowRequested += OnNewWindowRequested;
        webView.WebMessageReceived += OnWebMessageReceived;
        _webView = webView;
        BrowserWebViewRegistry.Register(webView);
        WebViewHost.Content = webView;
        if (downloadInitialMedia)
        {
            WebBrowserTabViewModel initiatingViewModel = _viewModel;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_viewModel, initiatingViewModel)) _ = DownloadMediaAsync(initialUri, null);
            });
        }
    }

    internal static void ConfigureMacOSWebViewEnvironment(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (e is AppleWKWebViewEnvironmentRequestedEventArgs apple)
        {
            // WKWebView omits Safari's product token, so Google serves its basic HTML UI.
            // Append the compatibility token while retaining the system's OS and WebKit UA.
            // EnvironmentRequested runs before the first navigation; AdapterCreated is too late.
            apple.ApplicationNameForUserAgent = "Safari/605.1.15";
        }
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
        if (_disposed || _webView == null)
        {
            return;
        }

        UpdateHistoryState();
        int handlerVersion = InvalidateNativeDownloads();
        NativeWebView webView = _webView;
        if (_pendingPageDownloadRequest?.Source != null) ClearPageDownloadRequest();
        _nativeDownloadHandler?.Dispose();
        void OnDownload(Uri uri, string name, IBrowserDownloadSource source)
        {
            if (handlerVersion != _nativeDownloadHandlerVersion || !ReferenceEquals(webView, _webView))
            {
                source.Dispose();
                return;
            }
            if (OperatingSystem.IsWindows()) OnWindowsNativeDownloadRequested(uri, name, source);
            else OnNativeDownloadRequested(uri, name, source);
        }
        if (OperatingSystem.IsMacOS())
        {
            _nativeDownloadHandler = MacOSBrowserDownloadHandler.TryAttach(e.TryGetPlatformHandle(), OnDownload,
                OnNativeNavigationCommitted, OnNativeNavigationStarted);
        }
        else if (OperatingSystem.IsWindows())
        {
            _nativeDownloadHandler = WindowsBrowserDownloadHandler.TryAttach(e.TryGetPlatformHandle(), OnDownload);
        }
        ScheduleLinuxSizeRefresh(_webView);
    }

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        if (sender is NativeWebView webView && !ReferenceEquals(webView, _webView)) return;
        InvalidateNativeDownloads();
        if (_pendingPageDownloadRequest?.Source != null) ClearPageDownloadRequest();
        _nativeDownloadHandler?.Dispose();
        _nativeDownloadHandler = null;
    }

    // A native download callback checks the returned version, so callbacks of the previous adapter or
    // web view are ignored; failures and deferred offers recorded for it are dropped with it.
    private int InvalidateNativeDownloads()
    {
        _nativeDownloadHandlerVersion++;
        _nativeDownloadFailures.Clear();
        _latestNavigationRequest = null;
        ClearDeferredNativeDownloads();
        return _nativeDownloadHandlerVersion;
    }

    private void ScheduleLinuxSizeRefresh(NativeWebView webView)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // The GTK adapter is created after the first layout pass. Toggling visibility on the next
        // background tick makes NativeWebView forward its already-arranged bounds to the native
        // X11 child instead of waiting for a window resize or dock reparent.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && ReferenceEquals(_webView, webView))
            {
                RefreshWebViewBounds(webView);
            }
        }, DispatcherPriority.Background);
    }

    internal static void RefreshWebViewBounds(NativeWebView webView)
    {
        bool wasVisible = webView.IsVisible;
        webView.IsVisible = !wasVisible;
        webView.IsVisible = wasVisible;
    }

    private async void OnOpenInDefaultBrowserClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { HasWebAddress.Value: true } viewModel)
        {
            return;
        }

        if (!await TryLaunchInDefaultBrowser(viewModel.CurrentUri))
        {
            viewModel.ReportActionFailure();
        }
    }

    private async void OnOpenLinuxRuntimeHelpClick(object? sender, RoutedEventArgs e)
    {
        if (!await TryLaunchInDefaultBrowser(LinuxWebViewSetupGuide))
        {
            _viewModel?.ReportActionFailure();
        }
    }

    private async Task<bool> TryLaunchInDefaultBrowser(Uri uri)
    {
        try
        {
            if (_launchInDefaultBrowser != null)
            {
                return await _launchInDefaultBrowser(uri);
            }

            return TopLevel.GetTopLevel(this)?.Launcher is { } launcher
                && await launcher.LaunchUriAsync(uri);
        }
        catch
        {
            return false;
        }
    }

    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_webView == null)
            {
                _viewModel?.ReportActionFailure();
                return;
            }

            _webView.ShowPrintUI();
        }
        catch
        {
            _viewModel?.ReportActionFailure();
        }
    }

    IDisposable? IWebViewReparentingContent.BeginReparenting()
    {
        NativeWebView? webView = _webView;
        if (_disposed
            || webView == null
            || !ReferenceEquals(WebViewHost.Content, webView)
            || !_canReparentWebView(webView))
        {
            return null;
        }

        IDisposable reparentingScope = webView.BeginReparenting();
        try
        {
            // Dock can present the destination window before the source presenter completes its
            // deferred cleanup. Detach synchronously so the scope captures the current adapter.
            WebViewHost.Content = null;
        }
        catch
        {
            reparentingScope.Dispose();
            throw;
        }

        return Disposable.Create(() =>
        {
            try
            {
                if (!_disposed
                    && ReferenceEquals(_webView, webView)
                    && WebViewHost.Content == null)
                {
                    WebViewHost.Content = webView;
                }
            }
            finally
            {
                reparentingScope.Dispose();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResetPageDownloadRequests();
        _pageRevision++;
        _findRequest?.Cancel();
        CloseBrowserPanel();
        _downloadCancellation?.Cancel();
        AddressTextBox.CancelSearchSuggestions();
        Loaded -= OnLoaded;
        UnsubscribeFromViewModel();
        _viewModel = null;
        BookmarkItems.ItemsSource = null;
        DisposeWebView();
    }

    private void DisposeWebView()
    {
        InvalidateNativeDownloads();
        _adBlockSession?.Dispose();
        _adBlockSession = null;
        if (_webView == null)
        {
            return;
        }

        _webView.EnvironmentRequested -= ConfigureMacOSWebViewEnvironment;
        _webView.AdapterCreated -= OnAdapterCreated;
        _webView.AdapterDestroyed -= OnAdapterDestroyed;
        _nativeDownloadHandler?.Dispose();
        _nativeDownloadHandler = null;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.NewWindowRequested -= OnNewWindowRequested;
        _webView.WebMessageReceived -= OnWebMessageReceived;
        BrowserWebViewRegistry.Unregister(_webView);
        WebViewHost.Content = null;
        _webView = null;
    }

    internal static (bool IsAvailable, string? Detail, bool ShowLinuxRuntimeHelp) GetWebViewAvailability()
    {
        WebViewAdapterType[] candidates = OperatingSystem.IsWindows()
            ? [WebViewAdapterType.WebView2, WebViewAdapterType.WebView1]
            : OperatingSystem.IsMacOS()
                ? [WebViewAdapterType.WkWebView]
                : OperatingSystem.IsLinux()
                    ? [WebViewAdapterType.WpeWebKit, WebViewAdapterType.WebKitGtk]
                    : [];

        foreach (WebViewAdapterType candidate in candidates)
        {
            try
            {
                DetailedWebViewAdapterInfo info = WebViewAdapterInfo.GetAdapterInfo(candidate);
                if (IsAdapterAvailable(info))
                {
                    return (true, null, false);
                }
            }
            catch
            {
                // Probe every platform-supported adapter before reporting the runtime unavailable.
            }
        }

        string? detail = OperatingSystem.IsWindows()
            ? Strings.WebViewWindowsRuntimeHint
            : OperatingSystem.IsLinux()
                ? Strings.WebViewLinuxRuntimeHint
                : null;
        return (false, detail, OperatingSystem.IsLinux());
    }

    internal static bool IsAdapterAvailable(DetailedWebViewAdapterInfo info)
    {
        // WebKitGTK 11.4 reports NativeDialog here even though NativeWebView's Linux factory
        // creates a GtkX11WebViewAdapter for the embedded control. The platform-specific candidate
        // list above already matches the factory, so installation/support is the reliable gate.
        return info.IsSupported && info.IsInstalled;
    }
}
