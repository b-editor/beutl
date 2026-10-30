using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

using Beutl.Editor.Components.WebBrowserTab.ViewModels;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    internal void OnNativeNavigationCommitted(Uri uri)
    {
        if (!_disposed) _viewModel?.CommitNavigation(uri);
    }

    internal void OnNativeNavigationStarted()
    {
        if (!_disposed) InvalidatePageDownloadRequests();
    }

    internal void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (sender is NativeWebView webView && !ReferenceEquals(webView, _webView)) return;
        if (e.Cancel) return;
        if (e.Request is { } unsupportedRequest && unsupportedRequest != WebBrowserTabViewModel.BlankPage
            && !BrowserMediaDownload.IsHttpUri(unsupportedRequest))
        {
            e.Cancel = true;
            if (!_navigationStartedIncludesSubframes) _viewModel?.BeginNavigation(unsupportedRequest);
            return;
        }

        _latestNavigationRequest = e.Request;

        // WebKit reports subframe policy decisions here without frame identity; only the macOS delegate
        // can filter them. Main-frame commits arrive through NavigationCompleted, and on macOS main-frame
        // starts also arrive through separate native callbacks.
        // App-initiated navigation and explicit download links are handled separately.
        if (_navigationStartedIncludesSubframes)
        {
            // Without a native response handler, a media request must become a download offer here,
            // even though a subframe may have made it; WebKit would otherwise play it in the page.
            if (!_handlesMediaResponsesNatively && TryInterceptMediaNavigation(e, _pageDownloadNavigationPending)) return;
            // Expire script offers at the policy decision, but keep a captured native response
            // until the WK delegate confirms that a new main-frame navigation has started.
            if (_pendingPageDownloadRequest?.Source == null) InvalidatePageDownloadRequests();
            return;
        }

        _pageRevision++;
        _findRequest?.Cancel();
        if (_nativeDownloadHandler is not WindowsBrowserDownloadHandler && TryInterceptMediaNavigation(e, true)) return;

        if (e.Request is { } request)
        {
            PreservePendingNativeDownload();
            InvalidatePageDownloadRequests(navigationStarted: true);
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
        if (_adBlockSession?.IsPreparing == true) return;
        if (_viewModel == null || _webView == null)
        {
            return;
        }

        // The canceled media never replaced the document. Its failure may arrive even after
        // the offer is dismissed or downloaded, so track it independently of the confirmation UI.
        Uri uri = e.Request ?? _webView.Source;
        if (!e.IsSuccess && ConsumeNativeDownloadFailure(e.Request, uri))
        {
            return;
        }
        if (e.IsSuccess)
        {
            // An iframe download can overlap this page load without producing a top-level failure.
            _nativeDownloadFailures.RemoveAll(item => item.Navigation == uri);
        }
        if (uri == _latestNavigationRequest) _latestNavigationRequest = null;
        if (!e.IsSuccess && _mediaNavigationIntercepted) return;

        if (e.IsSuccess) ResetPageDownloadRequests();
        else SettleAbortedPageNavigation();
        _pageRevision++;
        _findRequest?.Cancel();
        _viewModel.CompleteNavigation(uri, e.IsSuccess, _webView.CanGoBack, _webView.CanGoForward);
        UpdateBlankPageState();
        OfferNextDeferredNativeDownload();
        if (e.IsSuccess && uri != WebBrowserTabViewModel.BlankPage)
        {
            _ = UpdatePageTitleAsync(uri);
            _ = InstallDownloadLinkHandlerAsync(_webView);
            _ = SetPageZoomAsync(_zoomPercent);
            if (FindPanel.IsVisible) _ = FindInPageAsync(0);
            // WebView2 handles attachment responses itself; an inline media page still offers a download.
            if (_nativeDownloadHandler is WindowsBrowserDownloadHandler && BrowserMediaDownload.IsMediaLink(uri))
                QueuePageDownloadRequest(uri, null);
            // Linux sees no response before showing it, so media served without a media extension is found here.
            else if (!_handlesMediaResponsesNatively) _ = OfferInlineMediaPageAsync(uri);
        }
    }

    internal async Task UpdatePageTitleAsync(Uri uri)
    {
        NativeWebView? webView = _webView;
        WebBrowserTabViewModel? viewModel = _viewModel;
        int revision = _pageRevision;
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
                if (!_disposed && revision == _pageRevision
                    && ReferenceEquals(_webView, webView) && ReferenceEquals(_viewModel, viewModel))
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

    internal async Task OfferInlineMediaPageAsync(Uri uri)
    {
        NativeWebView? webView = _webView;
        int revision = _pageRevision;
        // On Linux a page-initiated navigation start changes the download document, not the page revision.
        int documentId = _pageDownloadDocumentId;
        if (webView == null) return;

        string? contentType;
        try
        {
            // WebKit reports the response type of a document it shows as media.
            contentType = NormalizePageTitle(await _getPageContentType(webView));
        }
        catch
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && revision == _pageRevision && documentId == _pageDownloadDocumentId
                    && ReferenceEquals(_webView, webView) && BrowserMediaDownload.IsMediaType(contentType))
                {
                    QueuePageDownloadRequest(uri, null);
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

    internal void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        if (e.Request is { } unsupportedRequest && unsupportedRequest != WebBrowserTabViewModel.BlankPage
            && !BrowserMediaDownload.IsHttpUri(unsupportedRequest))
        {
            e.Handled = true;
            return;
        }

        if (e.Request is { } mediaUri && BrowserMediaDownload.IsMediaLink(mediaUri))
        {
            e.Handled = true;
            QueuePageDownloadRequest(mediaUri, null);
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
        if (_webView != null && StartNativePageNavigation(_webView.GoBack)) _viewModel?.BeginNavigation(_viewModel.CurrentUri);
        UpdateHistoryState();
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        CloseBrowserPanel();
        if (_webView != null && StartNativePageNavigation(_webView.GoForward)) _viewModel?.BeginNavigation(_viewModel.CurrentUri);
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
        return started;
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (_webView?.Stop() == true) OnNavigationStopped();
    }

    internal void OnNavigationStopped()
    {
        _viewModel?.StopNavigation();
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
            if (BrowserMediaDownload.IsMediaLink(uri))
            {
                // Downloading does not replace the page hosted by the WebView.
                _viewModel.Address.Value = WebBrowserTabViewModel.FormatAddress(_viewModel.CurrentUri);
                _ = DownloadMediaAsync(uri, null);
                return;
            }
            InvalidatePageDownloadRequests(navigationStarted: true);
            _viewModel.BeginNavigation(uri);
            _webView.Navigate(uri);
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
}
