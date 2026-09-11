using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
    private WebBrowserTabViewModel? _viewModel;
    private bool _disposed;

    public WebBrowserTabView()
        : this(static uri => new NativeWebView { Source = uri }, GetWebViewAvailability)
    {
    }

    internal WebBrowserTabView(
        Func<Uri, NativeWebView> createWebView,
        Func<(bool IsAvailable, string? Detail, bool ShowLinuxRuntimeHelp)> getWebViewAvailability,
        Func<Uri, Task<bool>>? launchInDefaultBrowser = null,
        Func<NativeWebView, Task<string?>>? getPageTitle = null,
        Func<NativeWebView, bool>? canReparentWebView = null,
        bool? navigationStartedIncludesSubframes = null)
    {
        _createWebView = createWebView;
        _getWebViewAvailability = getWebViewAvailability;
        _launchInDefaultBrowser = launchInDefaultBrowser;
        _getPageTitle = getPageTitle ?? (static webView => webView.InvokeScript("document.title"));
        _canReparentWebView = canReparentWebView ?? (static webView => webView.TryGetPlatformHandle() is not null);
        _navigationStartedIncludesSubframes = navigationStartedIncludesSubframes ?? OperatingSystem.IsMacOS();
        InitializeComponent();
        AddressTextBox.SearchSuggestionsChanged += OnSearchSuggestionsChanged;
        AddHandler(KeyDownEvent, OnBrowserKeyDown, RoutingStrategies.Tunnel);
        Loaded += OnLoaded;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_disposed || DataContext is not WebBrowserTabViewModel viewModel
            || ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        _downloadCancellation?.Cancel();
        CloseBrowserPanel();
        _pageRevision++;
        _findRequest?.Cancel();
        if (_viewModel != null)
        {
            _viewModel.Disposing -= Dispose;
            _viewModel.Profile.SettingsChanged -= OnProfileChanged;
            _viewModel.Profile.Bookmarks.CollectionChanged -= OnBookmarksChanged;
            _viewModel.Profile.Downloads.CollectionChanged -= OnDownloadHistoryChanged;
        }
        _viewModel = viewModel;
        viewModel.Disposing += Dispose;
        viewModel.Profile.SettingsChanged += OnProfileChanged;
        viewModel.Profile.Bookmarks.CollectionChanged += OnBookmarksChanged;
        viewModel.Profile.Downloads.CollectionChanged += OnDownloadHistoryChanged;
        UpdateBlankPageState();
        OnCancelBookmarkEditorClick(this, new RoutedEventArgs());
        OnProfileChanged();

        if (_webView != null && _webView.Source != viewModel.CurrentUri)
        {
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
        if (initialUri != WebBrowserTabViewModel.BlankPage && !downloadInitialMedia)
            _viewModel.BeginNavigation(initialUri);
        NativeWebView webView = _createWebView(downloadInitialMedia ? WebBrowserTabViewModel.BlankPage : initialUri);
        if (OperatingSystem.IsMacOS())
        {
            webView.EnvironmentRequested += ConfigureMacOSWebViewEnvironment;
        }
        webView.AdapterCreated += OnAdapterCreated;
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
        ScheduleLinuxSizeRefresh(_webView);
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

    internal void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        // The macOS WebView adapter forwards policy decisions for every target frame through this event,
        // without exposing IsMainFrame. Only completed navigation identifies the top-level URL.
        // App-initiated navigation and explicit download links are handled separately.
        if (_navigationStartedIncludesSubframes) return;

        _pageRevision++;
        _findRequest?.Cancel();
        if (e.Request is { } mediaUri && BrowserMediaDownload.IsMediaLink(mediaUri))
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(() => _ = DownloadMediaAsync(mediaUri, null));
            return;
        }

        if (e.Request is { } request)
        {
            _viewModel?.BeginNavigation(request);
        }
    }

    internal void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (_viewModel == null || _webView == null)
        {
            return;
        }

        _pageRevision++;
        _findRequest?.Cancel();
        Uri uri = e.Request ?? _webView.Source;
        _viewModel.CompleteNavigation(uri, e.IsSuccess, _webView.CanGoBack, _webView.CanGoForward);
        UpdateBlankPageState();
        if (e.IsSuccess && uri != WebBrowserTabViewModel.BlankPage)
        {
            _ = UpdatePageTitleAsync(uri);
            _ = InstallDownloadLinkHandlerAsync(_webView);
            _ = SetPageZoomAsync(_zoomPercent);
            if (FindPanel.IsVisible) _ = FindInPageAsync(0);
        }
    }

    internal async Task UpdatePageTitleAsync(Uri uri)
    {
        NativeWebView? webView = _webView;
        WebBrowserTabViewModel? viewModel = _viewModel;
        if (webView == null || viewModel == null)
        {
            return;
        }

        string? result;
        try
        {
            result = await _getPageTitle(webView);
        }
        catch
        {
            return;
        }

        string? title = NormalizePageTitle(result);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && ReferenceEquals(_webView, webView) && ReferenceEquals(_viewModel, viewModel))
                {
                    viewModel.SetPageTitle(uri, title);
                }
            });
        }
        catch
        {
            // The dispatcher can be shutting down while a tab is being closed.
        }
    }

    internal static string? NormalizePageTitle(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        string title = result.Trim();
        if (title.StartsWith('"') && title.EndsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(title)?.Trim();
            }
            catch (JsonException)
            {
                // Non-JSON WebKit titles can legitimately start and end with quotation marks.
            }
        }

        return title;
    }

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        if (e.Request is { } mediaUri && BrowserMediaDownload.IsMediaLink(mediaUri))
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() => _ = DownloadMediaAsync(mediaUri, null));
            return;
        }

        if (e.Request is { } request && _viewModel?.TryOpenNewTab(request) == true)
        {
            e.Handled = true;
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        CloseBrowserPanel();
        if (_webView?.GoBack() == true) _viewModel?.BeginNavigation(_viewModel.CurrentUri);
        UpdateHistoryState();
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        CloseBrowserPanel();
        if (_webView?.GoForward() == true) _viewModel?.BeginNavigation(_viewModel.CurrentUri);
        UpdateHistoryState();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        CloseBrowserPanel();
        if (_webView?.Refresh() == true) _viewModel?.BeginNavigation(_viewModel.CurrentUri);
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (_webView?.Stop() == true && _viewModel != null)
        {
            _viewModel.StopNavigation();
        }
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.TryOpenNewTab(WebBrowserTabViewModel.BlankPage);
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

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (SearchSuggestionsPanel.IsVisible)
        {
            int count = SearchSuggestionsList.ItemCount;
            if (e.Key is Key.Down or Key.Up && count > 0)
            {
                int index = SearchSuggestionsList.SelectedIndex;
                SearchSuggestionsList.SelectedIndex = e.Key == Key.Down
                    ? (index + 1) % count
                    : (index <= 0 ? count - 1 : index - 1);
                SearchSuggestionsList.ScrollIntoView(SearchSuggestionsList.SelectedItem!);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && SearchSuggestionsList.SelectedItem is string query)
            {
                SearchForSuggestion(query);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            AddressTextBox.CancelSearchSuggestions();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            AddressTextBox.CancelSearchSuggestions();
            NavigateFromAddress();
            e.Handled = true;
        }
    }

    private void OnSearchSuggestionsChanged(IReadOnlyList<string> suggestions)
    {
        SearchSuggestionsList.ItemsSource = suggestions;
        SearchSuggestionsList.SelectedIndex = -1;
        SearchSuggestionsPanel.IsVisible = suggestions.Count > 0;
    }

    private void OnSearchSuggestionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual visual
            && visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is string query)
        {
            SearchForSuggestion(query);
            e.Handled = true;
        }
    }

    private void SearchForSuggestion(string query)
    {
        AddressTextBox.CancelSearchSuggestions();
        if (_viewModel != null)
        {
            _viewModel.Address.Value = WebSearchSuggestions.CreateSearchUri(query, _viewModel.Profile.Engine).AbsoluteUri;
            NavigateFromAddress();
        }
    }

    internal void NavigateFromAddress()
    {
        EnsureWebView();
        if (_viewModel?.TryCreateNavigationUri(out Uri uri) == true)
        {
            CloseBrowserPanel();
            if (BrowserMediaDownload.IsMediaLink(uri))
            {
                _ = DownloadMediaAsync(uri, null);
                return;
            }
            _viewModel.BeginNavigation(uri);
            _webView?.Navigate(uri);
        }
    }

    private void UpdateHistoryState()
    {
        if (_viewModel == null || _webView == null)
        {
            return;
        }

        _viewModel.UpdateHistoryState(_webView.CanGoBack, _webView.CanGoForward);
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
        _pageRevision++;
        _findRequest?.Cancel();
        CloseBrowserPanel();
        _downloadCancellation?.Cancel();
        AddressTextBox.CancelSearchSuggestions();
        Loaded -= OnLoaded;
        if (_viewModel != null)
        {
            _viewModel.Disposing -= Dispose;
            _viewModel.Profile.SettingsChanged -= OnProfileChanged;
            _viewModel.Profile.Bookmarks.CollectionChanged -= OnBookmarksChanged;
            _viewModel.Profile.Downloads.CollectionChanged -= OnDownloadHistoryChanged;
        }
        _viewModel = null;
        DisposeWebView();
    }

    private void DisposeWebView()
    {
        if (_webView == null)
        {
            return;
        }

        _webView.EnvironmentRequested -= ConfigureMacOSWebViewEnvironment;
        _webView.AdapterCreated -= OnAdapterCreated;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.NewWindowRequested -= OnNewWindowRequested;
        _webView.WebMessageReceived -= OnWebMessageReceived;
        BrowserWebViewRegistry.Unregister(_webView);
        WebViewHost.Content = null;
        _webView = null;
    }

    private static (bool IsAvailable, string? Detail, bool ShowLinuxRuntimeHelp) GetWebViewAvailability()
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
