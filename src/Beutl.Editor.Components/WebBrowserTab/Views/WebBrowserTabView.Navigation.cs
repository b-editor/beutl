using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

using Beutl.Editor.Components.WebBrowserTab.ViewModels;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    // A tab that a page opened goes to its first address with the origin that page sends. Switching tabs replaces
    // the adapter, and NativeWebView knows no address until the page has loaded, so each new adapter asks again.
    // Until the page commits, the tab's blank initial document belongs to that page, so its downloads send the
    // origin too. WebView2 reports no commit before the page has loaded, so on Windows that ends when the
    // navigation starts; until the commit, only WebView2's own downloads answer it there.
    private (Uri Uri, Uri Referrer)? _openerNavigation;
    private bool _openerNavigationIssued;
    private bool _openerPageCommitted;
    // The referrer WebKit computed for the latest new window request, from the page's policy and the link's rel.
    private (Uri Request, Uri? Referrer)? _nativeNewWindow;
    private string? _expectedWebView2HtmlPayload;

    // The page whose document makes the current requests. BlankPage stands for a document of unknown origin.
    private Uri? DocumentReferrer => (_openerPageCommitted ? null : _openerNavigation?.Referrer)
        ?? (_pageDownloadReferrerUncertain ? WebBrowserTabViewModel.BlankPage : _viewModel?.CurrentUri);

    internal void OnNativeNavigationCommitted(Uri uri)
    {
        if (_disposed) return;
        _viewModel?.CommitNavigation(uri);
        // The page loading now makes its own requests, before NavigationCompleted reports it loaded.
        if (uri != WebBrowserTabViewModel.BlankPage) _openerPageCommitted = true;
    }

    internal void OnNativeNavigationStarted(bool history = false)
    {
        if (_disposed) return;
        InvalidatePageDownloadRequests(navigationStarted: history);
        if (_adBlockSession?.IsPreparing != true) _viewModel?.BeginNavigation();
    }

    private void OnNativeNavigationFailed()
    {
        if (_webView == null || _viewModel == null) return;
        CompleteNavigation(_latestNavigationRequest ?? _viewModel.CurrentUri, false);
    }

    private void OnNativeHistoryChanged(Uri? restored)
    {
        if (_disposed || _viewModel == null || _webView == null || _adBlockSession?.IsPreparing == true) return;
        if (restored == null)
        {
            UpdateHistoryState();
            return;
        }
        _viewModel.CompleteNavigation(restored, true, _webView.CanGoBack, _webView.CanGoForward);
        UpdateBlankPageState();
        _ = UpdatePageTitleAsync(restored);
    }

    internal void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (sender is NativeWebView webView && !ReferenceEquals(webView, _webView)) return;
        if (e.Cancel) return;
        Uri? request = e.Request;
        if (_expectedWebView2HtmlPayload is { } payload && request is { Scheme: "data" })
        {
            ReadOnlySpan<char> address = request.OriginalString;
            int separator = address.IndexOf(',');
            if (separator >= 0
                && address[..separator].StartsWith("data:text/html;", StringComparison.OrdinalIgnoreCase)
                && address[..separator].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                && address[(separator + 1)..].SequenceEqual(payload))
            {
                // WebView2 reports about:blank as the document URL for this HTML load.
                _expectedWebView2HtmlPayload = null;
                request = WebBrowserTabViewModel.BlankPage;
            }
        }

        if (request is { } unsupportedRequest && unsupportedRequest != WebBrowserTabViewModel.BlankPage
            && !BrowserMediaDownload.IsHttpUri(unsupportedRequest))
        {
            e.Cancel = true;
            if (!_navigationStartedIncludesSubframes) _viewModel?.BeginNavigation(unsupportedRequest);
            return;
        }

        if (request != WebBrowserTabViewModel.BlankPage) _expectedWebView2HtmlPayload = null;
        SupersedeHeldAdapterAddress(request);
        _latestNavigationRequest = request;
        if (_nativeBrowserHandler is WindowsBrowserDownloadHandler windows) windows.BeginDocument(request);

        // WebKit reports subframe policy decisions here without frame identity; only the macOS delegate
        // can filter them here. The WebKit backends report main-frame load starts separately.
        // App-initiated navigation and explicit download links are handled separately.
        if (_navigationStartedIncludesSubframes)
        {
            // Without a native response handler, a media request must become a download offer here,
            // even though a subframe may have made it; WebKit would otherwise play it in the page.
            if (!_handlesMediaResponsesNatively
                && TryInterceptMediaNavigation(e, _pageDownloadNavigationPending || _viewModel?.IsLoadingRequestedAddress == true)) return;
            // Expire script offers at the policy decision, but keep a captured native response
            // until the WK delegate confirms that a new main-frame navigation has started.
            if (_pendingPageDownloadRequest?.Source == null) InvalidatePageDownloadRequests();
            return;
        }

        _pageRevision++;
        _findRequest?.Cancel();
        if (_nativeBrowserHandler is not WindowsBrowserDownloadHandler && TryInterceptMediaNavigation(e, true)) return;

        if (request != null)
        {
            PreservePendingNativeDownload();
            InvalidatePageDownloadRequests(navigationStarted: true);
            _openerPageCommitted = true;
            _viewModel?.BeginNavigation(request);
        }
    }

    // A media request that may come from a subframe only settles a navigation the app started;
    // otherwise it must leave the top-level page's loading and failure state alone.
    private bool TryInterceptMediaNavigation(WebViewNavigationStartingEventArgs e, bool settlesNavigation)
    {
        if (e.Request is not { } mediaUri || !BrowserMediaDownload.IsMediaLink(mediaUri)) return false;
        e.Cancel = true;
        StopCanceledGtkNavigation();
        _latestNavigationRequest = null;
        if (settlesNavigation)
        {
            // A redirect can turn an in-flight page navigation into a download. Its document
            // origin is no longer confirmed, and cancellation must release the request gate.
            if (_pageDownloadNavigationPending) SettleAbortedPageNavigation();
            else InvalidatePageDownloadRequests();
            _mediaNavigationIntercepted = true;
            _viewModel?.StopNavigation();
        }
        else
        {
            ClearPageDownloadRequest();
        }
        QueuePageDownloadRequest(mediaUri, null);
        return true;
    }

    // Avalonia's WebKitGTK adapter reports a canceled policy decision as handled without ignoring it,
    // and WebKit uses a decision released undecided, so the media would still replace the page.
    private void StopCanceledGtkNavigation()
    {
        if (!OperatingSystem.IsLinux() || _webView is not { } webView
            || webView.TryGetPlatformHandle() is not IGtkWebViewPlatformHandle) return;
        // The GLib thread waits for this handler; stopping synchronously here would deadlock.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && ReferenceEquals(_webView, webView)) webView.Stop();
        });
    }

    internal void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (sender is NativeWebView webView && !ReferenceEquals(webView, _webView)) return;
        // Avalonia drops the navigation ID; the Windows handler filters superseded completions.
        if (_nativeBrowserHandler is WindowsBrowserDownloadHandler { HandlesNavigation: true }) return;
        CompleteNavigation(e.Request, e.IsSuccess);
    }

    private void CompleteNavigation(Uri? request, bool isSuccess)
    {
        if (_adBlockSession?.IsPreparing == true) return;
        if (_viewModel == null || _webView == null)
        {
            return;
        }

        // The canceled media never replaced the document. Its failure may arrive even after
        // the offer is dismissed or downloaded, so track it independently of the confirmation UI.
        Uri uri = request ?? _webView.Source;
        if (isSuccess) SupersedeHeldAdapterAddress(uri);
        if (!isSuccess && ConsumeNativeDownloadFailure(request, uri))
        {
            return;
        }
        if (isSuccess)
        {
            // An iframe download can overlap this page load without producing a top-level failure.
            _nativeDownloadFailures.RemoveAll(item => item.Navigation == uri);
        }
        if (uri == _latestNavigationRequest) _latestNavigationRequest = null;
        if (!isSuccess && _mediaNavigationIntercepted) return;

        // WKWebView reports a page once it has loaded, after its scripts could already generate a file. That file
        // does not expire with the load; it is offered again below.
        if (isSuccess && _pendingPageDownloadRequest?.Source?.IsGenerated == true) PreservePendingNativeDownload();
        if (isSuccess) ResetPageDownloadRequests();
        else SettleAbortedPageNavigation();
        if (isSuccess && uri != WebBrowserTabViewModel.BlankPage) _openerNavigation = null;
        _pageRevision++;
        _findRequest?.Cancel();
        var (canGoBack, canGoForward) = ReadHistoryState(_webView, _viewModel);
        _viewModel.CompleteNavigation(uri, isSuccess, canGoBack, canGoForward);
        UpdateBlankPageState();
        SettleLoadingPageClaims(uri, isSuccess);
        OfferNextDeferredNativeDownload();
        if (isSuccess && uri != WebBrowserTabViewModel.BlankPage)
        {
            _ = UpdatePageTitleAsync(uri);
            _ = InstallDownloadLinkHandlerAsync(_webView);
            _ = SetPageZoomAsync(_zoomPercent);
            if (FindPanel.IsVisible) _ = FindInPageAsync(0);
            // WebView2 handles attachment responses itself; an inline media page still offers a download.
            if (_nativeBrowserHandler is WindowsBrowserDownloadHandler && BrowserMediaDownload.IsMediaLink(uri))
                QueuePageDownloadRequest(uri, null);
            // Linux sees no response before showing it, so media served without a media extension is found here.
            else if (!_handlesMediaResponsesNatively) _ = OfferInlineMediaPageAsync(uri);
        }
    }

    internal Task UpdatePageTitleAsync(Uri uri)
    {
        WebBrowserTabViewModel? viewModel = _viewModel;
        if (viewModel == null) return Task.CompletedTask;
        return QueryPageAsync(_getPageTitle, () => ReferenceEquals(_viewModel, viewModel),
            title => viewModel.SetPageTitle(uri, title));
    }

    internal Task OfferInlineMediaPageAsync(Uri uri)
    {
        // On Linux a page-initiated navigation start changes the download document, not the page revision.
        int documentId = _pageDownloadDocumentId;
        // WebKit reports the response type of a document it shows as media.
        return QueryPageAsync(_getPageContentType, () => documentId == _pageDownloadDocumentId, contentType =>
        {
            if (BrowserMediaDownload.IsMediaType(contentType)) QueuePageDownloadRequest(uri, null);
        });
    }

    // Runs a page script and applies its answer on the UI thread, unless the page has changed since it was asked.
    private async Task QueryPageAsync(Func<NativeWebView, Task<string?>> query, Func<bool> isCurrent, Action<string?> apply)
    {
        NativeWebView? webView = _webView;
        int revision = _pageRevision;
        if (webView == null) return;

        string? result;
        try
        {
            result = NormalizePageTitle(await query(webView));
        }
        catch
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && revision == _pageRevision && ReferenceEquals(_webView, webView) && isCurrent())
                {
                    apply(result);
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

    // The WebKit hook reports the request immediately before the adapter opens the window.
    internal void OnNativeNewWindowRequested(Uri request, string? referrer)
    {
        if (!_disposed) _nativeNewWindow = (request, Uri.TryCreate(referrer, UriKind.Absolute, out Uri? uri) ? uri : null);
    }

    // A page's window.open() opens a tab as a link with target="_blank" does. WebView2 raises NewWindowRequested for
    // both, while WKWebView and WebKitGTK ask only their own delegates for it, so the handlers report it here.
    internal void OnNativeWindowOpenRequested(Uri request)
    {
        if (!_disposed && _webView is { } webView)
            OnNewWindowRequested(webView, new WebViewNewWindowRequestedEventArgs { Request = request });
    }

    internal void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        // WebView2 delivers the window name only to its native handler, after Avalonia's callback.
        if (_nativeBrowserHandler is WindowsBrowserDownloadHandler { HandlesNewWindows: true }) return;
        (Uri Request, Uri? Referrer)? native = _nativeNewWindow;
        _nativeNewWindow = null;
        if (e.Request is { } request)
            e.Handled = OpenWindow(request, native is { } report && report.Request == request ? report.Referrer : null);
    }

    private bool OpenWindow(Uri request, Uri? page, BrowserReferrerPolicy? reportedPolicy = null)
    {
        if (request != WebBrowserTabViewModel.BlankPage && !BrowserMediaDownload.IsHttpUri(request)) return true;
        // WebKit only reports the header for the first hop. A same-origin hop grants no permission for a cross-origin redirect.
        BrowserReferrerPolicy policy = reportedPolicy ?? (page != null && BrowserMediaDownload.IsSameOrigin(page, request)
            ? BrowserReferrerPolicy.SameOrigin : BrowserReferrerPolicy.Origin);
        Uri? referrer = BrowserMediaDownload.NormalizeReferrer(page, request, policy);
        if (BrowserMediaDownload.IsMediaLink(request))
        {
            QueuePageDownloadRequest(request, null, referrer == null ? BrowserReferrerPolicy.NoReferrer : policy, referrer: referrer);
            return true;
        }
        return _viewModel?.TryOpenNewTab(request, referrer) == true;
    }

    internal void NavigateToString(string html, Uri? baseUri = null)
    {
        if (_webView is not { } webView) return;
        _openerNavigation = null;
        // An HTML load can be queued before EnvironmentRequested. Keep it here so the
        // adapter's blank startup navigation cannot replace it.
        _pendingHtmlNavigation = (html, baseUri);
        if (_pendingAdapterAddress == null && webView.TryGetPlatformHandle() != null)
            ResumeHtmlNavigation(webView);
    }

    private void ResumeHtmlNavigation(NativeWebView webView)
    {
        if (_adBlockSession?.IsPreparing == true)
            _adBlockSession.HoldNavigation(WebBrowserTabViewModel.BlankPage, replacePending: true);
        else
            ResumeNavigation(webView, WebBrowserTabViewModel.BlankPage);
    }

    // Ad blocking holds a new tab's first navigation until its rules are in place.
    private void ResumeNavigation(NativeWebView webView, Uri uri)
    {
        // The adapter will resume the latest address entered while it was starting.
        if (_pendingAdapterAddress != null) return;
        if (_pendingHtmlNavigation is { } html)
        {
            _pendingHtmlNavigation = null;
            _expectedWebView2HtmlPayload = webView.TryGetPlatformHandle() is IWindowsWebView2PlatformHandle
                ? Convert.ToBase64String(Encoding.UTF8.GetBytes(html.Html))
                : null;
            webView.NavigateToString(html.Html, html.BaseUri);
        }
        else if (_openerNavigation?.Uri == uri)
        {
            _openerNavigationIssued = false;
            NavigateFromOpener(webView);
        }
        else
        {
            webView.Source = uri;
        }
    }

    // Ad blocking on Windows matches each request against its page, and NativeWebView names a page only once it has
    // loaded. Until the page this tab was opened for has loaded, its requests belong to that page.
    internal Uri AdBlockPage(NativeWebView webView) => _openerNavigation?.Uri ?? webView.Source;

    private void NavigateFromOpener(NativeWebView webView)
    {
        if (_disposed || _openerNavigation is not { } navigation || _openerNavigationIssued) return;
        _openerNavigationIssued = true;
        // The tab starts over from its blank document, which belongs to the opener until the page commits again.
        _openerPageCommitted = false;
        if (!_navigateWithReferrer(webView, navigation.Uri, navigation.Referrer)) webView.Navigate(navigation.Uri);
    }

    // NativeWebView navigates without request headers, so the referrer goes through the web view's own API.
    private bool NavigateWithReferrer(NativeWebView webView, Uri uri, Uri referrer)
    {
        string value = BrowserMediaDownload.FormatReferrerHeader(referrer);
        IPlatformHandle? handle = webView.TryGetPlatformHandle();
        return handle switch
        {
            // WebView2 answers later; a refusal then navigates without the referrer, unless the tab has moved on.
            IWindowsWebView2PlatformHandle windows when OperatingSystem.IsWindows() => WindowsReferrerNavigation.TryNavigate(windows, uri, value,
                () =>
                {
                    if (!_disposed && ReferenceEquals(_webView, webView) && _openerNavigation?.Uri == uri && !_openerPageCommitted)
                        webView.Navigate(uri);
                }),
            IAppleWKWebViewPlatformHandle apple when OperatingSystem.IsMacOS() => MacOSReferrerNavigation.TryNavigate(apple, uri, value),
            _ when OperatingSystem.IsLinux() && LinuxWebKit.TryGetWebView(handle, out nint linux, out bool wpe)
                => LinuxReferrerNavigation.TryNavigate(linux, wpe, uri, value),
            _ => false
        };
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (_webView != null) NavigateHistory(_webView.GoBack);
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        if (_webView != null) NavigateHistory(_webView.GoForward);
    }

    private void NavigateHistory(Func<bool> navigate)
    {
        CloseBrowserPanel();
        if (OperatingSystem.IsMacOS() && _nativeBrowserHandler is MacOSBrowserDownloadHandler mac)
        {
            if (mac.NavigateHistory(navigate)) _openerNavigation = null;
        }
        else if (OperatingSystem.IsWindows() && _nativeBrowserHandler is WindowsBrowserDownloadHandler { HandlesNavigation: true } windows)
        {
            if (windows.NavigateHistory(navigate)) _openerNavigation = null;
        }
        else if (StartNativePageNavigation(navigate))
        {
            _viewModel?.BeginNavigation(_viewModel.CurrentUri);
        }
        UpdateHistoryState();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        CloseBrowserPanel();
        if (_webView != null && StartNativePageNavigation(_webView.Refresh)) _viewModel?.BeginNavigation(_viewModel.CurrentUri);
    }

    private bool StartNativePageNavigation(Func<bool> navigate)
    {
        bool wasPending = _pageDownloadNavigationPending;
        bool wasIntercepted = _mediaNavigationIntercepted;
        InvalidatePageDownloadRequests(navigationStarted: true);
        bool started = navigate();
        if (!started)
        {
            _pageDownloadNavigationPending = wasPending;
            _mediaNavigationIntercepted = wasIntercepted;
        }
        else
        {
            _openerNavigation = null;
        }
        return started;
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (_webView is not { } webView || _viewModel is not { } viewModel) return;

        // WebView2 can complete the canceled navigation inside Stop. Mark the stop before the call
        // so that completion does not show the cancellation as a page failure.
        viewModel.PrepareNavigationStop();
        if (webView.Stop() != true)
        {
            viewModel.CancelNavigationStop();
            return;
        }

        OnNavigationStopped(markStopped: false);
    }

    internal void OnNavigationStopped(bool markStopped = true)
    {
        if (markStopped) _viewModel?.StopNavigation();
        else _viewModel?.EndLoading();
        SettleAbortedPageNavigation();
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.TryOpenNewTab(WebBrowserTabViewModel.BlankPage);
    }

    internal void NavigateFromAddress()
    {
        EnsureWebView();
        if (_webView == null) return;
        if (_viewModel?.TryCreateNavigationUri(out Uri uri) == true)
        {
            Beutl.Editor.Services.UsageTelemetry.Current?.Record("tool.command", "WebBrowser", "Navigate");
            CloseBrowserPanel();
            // A typed address has no referrer, and the tab leaves the page that opened it.
            _openerNavigation = null;
            if (BrowserMediaDownload.IsMediaLink(uri))
            {
                // Downloading does not replace the page hosted by the WebView.
                _viewModel.Address.Value = WebBrowserTabViewModel.FormatAddress(_viewModel.CurrentUri);
                _ = DownloadMediaAsync(uri, null);
                return;
            }
            InvalidatePageDownloadRequests(navigationStarted: true);
            _viewModel.BeginNavigation(uri);
            if (!HoldAdapterAddress(uri)) _webView.Navigate(uri);
        }
    }

    private void UpdateHistoryState()
    {
        if (_viewModel == null || _webView == null)
        {
            return;
        }

        var (canGoBack, canGoForward) = ReadHistoryState(_webView, _viewModel);
        _viewModel.UpdateHistoryState(canGoBack, canGoForward);
    }

    private (bool CanGoBack, bool CanGoForward) ReadHistoryState(NativeWebView webView, WebBrowserTabViewModel viewModel)
    {
        if (webView.TryGetPlatformHandle() is not IGtkWebViewPlatformHandle)
            return (webView.CanGoBack, webView.CanGoForward);

        // The next navigation can already be in a GTK policy decision waiting on the UI thread.
        // Keep the current button state until a history read on GLib can complete.
        _ = UpdateGtkHistoryStateAsync(webView, viewModel);
        return (viewModel.CanGoBack.Value, viewModel.CanGoForward.Value);
    }

    private async Task UpdateGtkHistoryStateAsync(NativeWebView webView, WebBrowserTabViewModel viewModel)
    {
        int version = _nativeDownloadHandlerVersion, revision = _pageRevision;
        bool canGoBack = false, canGoForward = false;
        await LinuxWebKit.OnGlibAsync(() => (canGoBack, canGoForward) = (webView.CanGoBack, webView.CanGoForward));
        if (version == _nativeDownloadHandlerVersion && revision == _pageRevision && ReferenceEquals(viewModel, _viewModel))
            viewModel.UpdateHistoryState(canGoBack, canGoForward);
    }
}
