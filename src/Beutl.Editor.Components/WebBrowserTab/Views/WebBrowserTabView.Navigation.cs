using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
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

        // The macOS WebView adapter forwards policy decisions for every target frame through this event,
        // without exposing IsMainFrame. Only completed navigation identifies the top-level URL.
        // App-initiated navigation and explicit download links are handled separately.
        if (_navigationStartedIncludesSubframes)
        {
            // Script offers expire without treating a frame as a new page. Captured native
            // responses survive iframe activity; the WK delegate reports their main-frame starts.
            if (_pendingPageDownloadRequest?.Source == null) InvalidatePageDownloadRequests();
            return;
        }

        _pageRevision++;
        _findRequest?.Cancel();
        if (e.Request is { } mediaUri && BrowserMediaDownload.IsMediaLink(mediaUri)
            && _nativeDownloadHandler is not WindowsBrowserDownloadHandler)
        {
            e.Cancel = true;
            _latestNavigationRequest = null;
            // A redirect can turn an in-flight page navigation into a download. Its document
            // origin is no longer confirmed, and cancellation must release the request gate.
            if (_pageDownloadNavigationPending) SettleAbortedPageNavigation();
            else InvalidatePageDownloadRequests();
            _mediaNavigationIntercepted = true;
            _viewModel?.StopNavigation();
            QueuePageDownloadRequest(mediaUri, null);
            return;
        }

        if (e.Request is { } request)
        {
            PreservePendingNativeDownload();
            InvalidatePageDownloadRequests(navigationStarted: true);
            _viewModel?.BeginNavigation(request);
        }
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
