using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;


namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    private const int MaxDeferredNativeDownloads = 32;
    private const int MaxNativeDownloadFailures = 32;
    private CancellationTokenSource? _downloadCancellation;
    private PageDownloadRequest? _pendingPageDownloadRequest;
    private int _pageDownloadDocumentId;
    private bool _pageDownloadRequestsSuppressed;
    private bool _pageDownloadNavigationPending;
    private bool _pageDownloadReferrerUncertain;
    private bool _mediaNavigationIntercepted;
    private Uri? _latestNavigationRequest;
    private readonly List<(Uri? Navigation, Uri Download)> _nativeDownloadFailures = [];
    private readonly LinkedList<(Uri Uri, string? SuggestedName, IBrowserDownloadSource Source)> _deferredNativeDownloads = new();

    private sealed record PageDownloadRequest(Uri Uri, string? SuggestedName, Uri? Referrer, int DocumentId,
        BrowserReferrerPolicy ReferrerPolicy, IBrowserDownloadSource? Source);

    internal BrowserMediaDownload MediaDownloader { get; set; } = BrowserMediaDownload.Default;
    internal Func<Uri, CancellationToken, Task<BrowserDownloadOptions?>>? DownloadOptionsSelector { get; set; }
    internal Func<NativeWebView?, CancellationToken, Task<IReadOnlyList<Cookie>>> DownloadCookiesProvider { get; set; } =
        static async (webView, cancellation) => webView?.TryGetCookieManager() is { } manager
            ? await manager.GetCookiesAsync().WaitAsync(cancellation) : [];
    internal sealed record BrowserDownloadOptions(string Directory, bool AddToTimeline);

    internal void OnWindowsNativeDownloadRequested(Uri uri, string suggestedName, IBrowserDownloadSource source) =>
        OnNativeDownloadRequested(uri, suggestedName, source, trackNavigationFailure: true);

    internal void OnNativeDownloadRequested(Uri uri, string suggestedName, IBrowserDownloadSource source) =>
        OnNativeDownloadRequested(uri, suggestedName, source, trackNavigationFailure: false);

    private void OnNativeDownloadRequested(Uri uri, string suggestedName, IBrowserDownloadSource source,
        bool trackNavigationFailure)
    {
        if (_disposed || _viewModel == null)
        {
            source.Dispose();
            return;
        }
        bool mainFrameDownload = trackNavigationFailure && _pageDownloadNavigationPending && _latestNavigationRequest == uri;
        if (trackNavigationFailure && !_pageDownloadNavigationPending
            && (_pendingPageDownloadRequest?.Source != null || _downloadCancellation != null))
        {
            DeferNativeDownload(uri, suggestedName, source);
            return;
        }
        if (trackNavigationFailure && _pageDownloadNavigationPending && !mainFrameDownload)
        {
            // An iframe's response belongs to the page that is still loading. Offer it after that page commits.
            DeferNativeDownload(uri, suggestedName, source);
            return;
        }
        if (mainFrameDownload)
        {
            // WebView2 forwards a canceled main-frame download navigation as a failure.
            // Redirects also raise NavigationStarting, so the final download URI must match
            // the active main-frame request. An iframe download cannot borrow another URI's marker.
            _nativeDownloadFailures.Add((_latestNavigationRequest, uri));
            if (_nativeDownloadFailures.Count > MaxNativeDownloadFailures) _nativeDownloadFailures.RemoveAt(0);
        }
        _latestNavigationRequest = null;
        if (_pageDownloadNavigationPending) SettleAbortedPageNavigation();
        else InvalidatePageDownloadRequests();
        _viewModel.RestoreCommittedPage();
        UpdateBlankPageState();
        // Keep history retries conservative; the current transfer retains the browser's original response.
        if (trackNavigationFailure && _downloadCancellation != null) DeferNativeDownload(uri, suggestedName, source);
        else QueuePageDownloadRequest(uri, suggestedName, BrowserReferrerPolicy.NoReferrer, source);
        // The queued request keeps its conservative metadata; later links belong to the restored document.
        _pageDownloadReferrerUncertain = !BrowserMediaDownload.IsHttpUri(_viewModel.CurrentUri);
    }

    // One offer at a time, and none while a page loads, a download runs or the user dismissed this page's offers.
    private bool IsPageDownloadOfferBlocked => _disposed || _viewModel == null || _pageDownloadRequestsSuppressed
        || _pageDownloadNavigationPending || _pendingPageDownloadRequest != null || _downloadCancellation != null;

    private void OfferNextDeferredNativeDownload()
    {
        if (_deferredNativeDownloads.Count == 0 || IsPageDownloadOfferBlocked) return;
        var deferred = _deferredNativeDownloads.First!.Value;
        _deferredNativeDownloads.RemoveFirst();
        QueuePageDownloadRequest(deferred.Uri, deferred.SuggestedName, BrowserReferrerPolicy.NoReferrer, deferred.Source);
    }

    private void DeferNativeDownload(Uri uri, string? suggestedName, IBrowserDownloadSource source)
    {
        if (_pageDownloadRequestsSuppressed || _deferredNativeDownloads.Count >= MaxDeferredNativeDownloads)
        {
            source.Dispose();
            return;
        }
        _deferredNativeDownloads.AddLast((uri, suggestedName, source));
    }

    private void PreservePendingNativeDownload()
    {
        if (_pendingPageDownloadRequest is not { Source: { } source } pending) return;
        ClearPageDownloadRequest(disposeSource: false);
        if (_deferredNativeDownloads.Count >= MaxDeferredNativeDownloads)
        {
            _deferredNativeDownloads.Last!.Value.Source.Dispose();
            _deferredNativeDownloads.RemoveLast();
        }
        _deferredNativeDownloads.AddFirst((pending.Uri, pending.SuggestedName, source));
    }

    private void ClearDeferredNativeDownloads()
    {
        foreach (var deferred in _deferredNativeDownloads) deferred.Source.Dispose();
        _deferredNativeDownloads.Clear();
    }

    private bool ConsumeNativeDownloadFailure(Uri? request, Uri fallback)
    {
        int index = _nativeDownloadFailures.FindIndex(item =>
            request != null ? request == item.Navigation || request == item.Download : fallback == item.Download);
        if (index < 0 && request == null && _latestNavigationRequest == null && _nativeDownloadFailures.Count > 0)
            index = 0;
        if (index < 0) return false;
        _nativeDownloadFailures.RemoveAt(index);
        return true;
    }

    // Capture explicit download links as well as media links added dynamically by the page.
    internal const string DownloadLinkScript = """
        (() => {
            if (window.__beutlDownloadsInstalled) return;
            window.__beutlDownloadsInstalled = true;
            document.addEventListener('click', event => {
                if (!event.isTrusted || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                const link = event.target.closest?.('a[href]');
                if (!link) return;
                const url = new URL(link.href, document.baseURI);
                if (!['http:', 'https:'].includes(url.protocol)) return;
                if (!link.hasAttribute('download') && !/\.(mp4|webm|mov|mkv|avi|mpeg|mp3|m4a|wav|flac|ogg|opus|aac|jpg|jpeg|png|gif|webp|bmp|svg)$/i.test(url.pathname)) return;
                if (typeof window.invokeCSharpAction !== 'function') return;
                let policy = link.referrerPolicy;
                if (!policy) {
                    const policies = ['no-referrer','no-referrer-when-downgrade','origin','origin-when-cross-origin',
                        'same-origin','strict-origin','strict-origin-when-cross-origin','unsafe-url'];
                    for (const meta of document.querySelectorAll('meta[name],meta[http-equiv]')) {
                        if (meta.name.toLowerCase() !== 'referrer' && meta.httpEquiv.toLowerCase() !== 'referrer-policy') continue;
                        const candidate = meta.content.trim().toLowerCase();
                        if (policies.includes(candidate)) policy = candidate;
                    }
                }
                const noReferrer = link.rel.toLowerCase().split(/\s+/).includes('noreferrer') || policy === 'no-referrer';
                const referrerPolicy = noReferrer ? 'no-referrer' : policy === 'same-origin' ? 'same-origin' : 'origin';
                event.preventDefault();
                event.stopImmediatePropagation();
                window.invokeCSharpAction(JSON.stringify({kind:'beutl-download',url:url.href,name:link.getAttribute('download'),referrerPolicy}));
            }, true);
        })()
        """;

    private async Task InstallDownloadLinkHandlerAsync(NativeWebView webView)
    {
        try
        {
            await webView.InvokeScript(DownloadLinkScript);
        }
        catch
        {
            // A navigation can dispose the old document before script installation completes.
        }
    }

    internal void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (IsPageDownloadOfferBlocked) return;
        if (e.Body is not { Length: > 0 and < 16384 } body) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (TryReadDownloadMessage(root, out Uri? uri, out string? name))
                QueuePageDownloadRequest(uri, name, ReadReferrerPolicy(root));
        }
        catch (JsonException)
        {
            // Ignore messages from pages that use their own bridge protocol.
        }
    }

    // The message DownloadLinkScript posts for a captured link.
    private static bool TryReadDownloadMessage(JsonElement root, [NotNullWhen(true)] out Uri? uri, out string? name)
    {
        uri = null;
        name = null;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
            && kind.GetString() == "beutl-download"
            && root.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
            && Uri.TryCreate(url.GetString(), UriKind.Absolute, out uri) && BrowserMediaDownload.IsHttpUri(uri))
        {
            name = root.TryGetProperty("name", out var fileName) && fileName.ValueKind == JsonValueKind.String
                ? fileName.GetString() : null;
            return true;
        }

        return false;
    }

    private static BrowserReferrerPolicy ReadReferrerPolicy(JsonElement root)
    {
        BrowserReferrerPolicy policy = BrowserReferrerPolicy.Origin;
        if (root.TryGetProperty("referrerPolicy", out var policyNode))
        {
            policy = policyNode.ValueKind == JsonValueKind.String ? policyNode.GetString() switch
            {
                "origin" => BrowserReferrerPolicy.Origin,
                "same-origin" => BrowserReferrerPolicy.SameOrigin,
                _ => BrowserReferrerPolicy.NoReferrer
            } : BrowserReferrerPolicy.NoReferrer;
        }

        return policy;
    }

    private void QueuePageDownloadRequest(Uri uri, string? suggestedName, BrowserReferrerPolicy referrerPolicy = BrowserReferrerPolicy.Origin,
        IBrowserDownloadSource? downloadSource = null)
    {
        if (IsPageDownloadOfferBlocked)
        {
            downloadSource?.Dispose();
            return;
        }
        var owner = _viewModel;
        var source = _webView;
        int documentId = _pageDownloadDocumentId;
        // BlankPage deliberately prevents the downloader's direct-navigation fallback from
        // using destination cookies when the surviving document's origin is unknown.
        Uri? referrer = _pageDownloadReferrerUncertain ? ViewModels.WebBrowserTabViewModel.BlankPage : owner?.CurrentUri;
        var request = new PageDownloadRequest(uri, suggestedName, referrer, documentId, referrerPolicy, downloadSource);
        _pendingPageDownloadRequest = request;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || owner == null || !ReferenceEquals(owner, _viewModel) || !ReferenceEquals(source, _webView)
                || documentId != _pageDownloadDocumentId || _pageDownloadRequestsSuppressed || _pageDownloadNavigationPending
                || !ReferenceEquals(_pendingPageDownloadRequest, request) || _downloadCancellation != null) return;

            // Page scripts control bridge messages and navigation requests. Only native UI can authorize opening options.
            SetDownloadRunning(false);
            DownloadStatusText.MaxLines = 1;
            DownloadStatusText.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            DownloadStatusText.Text = Strings.BrowserPageDownloadRequest;
            DownloadProgressText.Text = uri.AbsoluteUri;
            DownloadProgressText.IsVisible = true;
            ToolTip.SetTip(DownloadStatusText, uri.AbsoluteUri);
            ToolTip.SetTip(DownloadProgressText, uri.AbsoluteUri);
            ToolTip.SetTip(DismissDownloadStatusButton, Strings.BrowserIgnorePageDownloads);
            Avalonia.Automation.AutomationProperties.SetName(DismissDownloadStatusButton, Strings.BrowserIgnorePageDownloads);
            ConfirmPageDownloadButton.IsVisible = true;
            DownloadStatusPanel.IsVisible = true;
        });
    }

    private void ClearPageDownloadRequest(bool disposeSource = true)
    {
        PageDownloadRequest? request = _pendingPageDownloadRequest;
        _pendingPageDownloadRequest = null;
        if (request != null && ConfirmPageDownloadButton.IsVisible)
        {
            DownloadStatusPanel.IsVisible = false;
            DownloadProgressText.IsVisible = false;
            DownloadProgressText.Text = string.Empty;
            ToolTip.SetTip(DownloadProgressText, null);
        }
        ConfirmPageDownloadButton.IsVisible = false;
        DownloadStatusText.MaxLines = 0;
        DownloadStatusText.TextTrimming = Avalonia.Media.TextTrimming.None;
        ToolTip.SetTip(DismissDownloadStatusButton, Strings.Close);
        Avalonia.Automation.AutomationProperties.SetName(DismissDownloadStatusButton, Strings.Close);
        if (disposeSource) request?.Source?.Dispose();
    }

    private void ResetPageDownloadRequests()
    {
        InvalidatePageDownloadRequests();
        _pageDownloadRequestsSuppressed = false;
        _pageDownloadNavigationPending = false;
        _pageDownloadReferrerUncertain = false;
        _mediaNavigationIntercepted = false;
    }

    private void SettleAbortedPageNavigation()
    {
        InvalidatePageDownloadRequests();
        _pageDownloadNavigationPending = false;
        _pageDownloadReferrerUncertain = true;
    }

    private void InvalidatePageDownloadRequests(bool navigationStarted = false)
    {
        _pageDownloadDocumentId++;
        if (navigationStarted)
        {
            _pageDownloadNavigationPending = true;
            _mediaNavigationIntercepted = false;
        }
        ClearPageDownloadRequest();
    }

    private async void OnConfirmPageDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_pageDownloadNavigationPending || !ConfirmPageDownloadButton.IsVisible || _pendingPageDownloadRequest is not { } request) return;
        ClearPageDownloadRequest(disposeSource: false);
        if (_disposed || request.DocumentId != _pageDownloadDocumentId)
        {
            request.Source?.Dispose();
            return;
        }
        await DownloadMediaAsync(request.Uri, request.SuggestedName, request.Referrer, request.ReferrerPolicy, request.Source);
    }

    private void OnDownloadMediaClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is { } vm && BrowserMediaDownload.IsHttpUri(vm.CurrentUri))
        {
            _ = DownloadMediaAsync(vm.CurrentUri, null, vm.CurrentUri);
        }
    }

    private void OnDismissDownloadStatusClick(object? sender, RoutedEventArgs e)
    {
        bool offerNext = _pendingPageDownloadRequest?.Source != null && _deferredNativeDownloads.Count > 0;
        if (_pendingPageDownloadRequest != null && !offerNext) _pageDownloadRequestsSuppressed = true;
        ClearPageDownloadRequest();
        DownloadStatusPanel.IsVisible = false;
        if (offerNext) OfferNextDeferredNativeDownload();
    }

    private void SetDownloadRunning(bool running)
    {
        DownloadCancelButton.IsVisible = running;
        DismissDownloadStatusButton.IsVisible = !running;
        DownloadProgressBar.IsVisible = running;
        DownloadProgressText.IsVisible = running;
        DownloadProgressBar.IsIndeterminate = running;
        DownloadProgressBar.Value = 0;
        DownloadProgressText.Text = string.Empty;
    }

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    internal async Task<BrowserDownloadOptions?> ChooseDownloadOptionsAsync(Uri uri, CancellationToken cancellation, string? suggestedName = null)
    {
        if (_disposed || _viewModel is not { } vm || cancellation.IsCancellationRequested) return null;
        string fileName = string.IsNullOrWhiteSpace(suggestedName) ? Uri.UnescapeDataString(uri.AbsolutePath) : suggestedName;
        var content = new BrowserDownloadOptionsView(uri, vm.ProjectDownloadDirectory,
            BeutlEnvironment.GetMaterialsDirectoryPath(), vm.CanAddDownloadedFile(fileName), suggestedName);
        var completion = new TaskCompletionSource<BrowserDownloadOptions?>(TaskCreationOptions.RunContinuationsAsynchronously);
        content.Confirmed += () => completion.TrySetResult(new BrowserDownloadOptions(content.SelectedDirectory, content.AddToTimeline));
        content.Canceled += () => completion.TrySetResult(null);
        ShowBrowserPanel(Strings.WebDownloadMedia, content, () => completion.TrySetResult(null));
        try
        {
            using var registration = cancellation.Register(() => completion.TrySetResult(null));
            return await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(ToolPanelContent.Content, content)) CloseBrowserPanel();
        }
    }

    private void ShowDownloadRunning(Uri uri)
    {
        DownloadStatusPanel.IsVisible = true;
        SetDownloadRunning(true);
        ToolTip.SetTip(DownloadStatusText, uri.AbsoluteUri);
        DownloadStatusText.Text = Strings.WebDownloadMedia;
    }

    // Reports only while this download is still the current one.
    private Progress<(long Received, long? Total)> CreateDownloadProgress(CancellationTokenSource cancellation)
    {
        return new Progress<(long Received, long? Total)>(value =>
        {
            if (!_disposed && ReferenceEquals(_downloadCancellation, cancellation) && !cancellation.IsCancellationRequested)
            {
                DownloadProgressBar.IsIndeterminate = value.Total is not > 0;
                DownloadProgressBar.Value = value.Total is > 0 ? Math.Clamp(value.Received * 100.0 / value.Total.Value, 0, 100) : 0;
                DownloadProgressText.Text = value.Total is > 0
                    ? $"{DownloadProgressBar.Value:0}%"
                    : $"{value.Received / 1024:N0} KB";
            }
        });
    }

    internal async Task DownloadMediaAsync(Uri uri, string? suggestedName, Uri? referrer = null,
        BrowserReferrerPolicy referrerPolicy = BrowserReferrerPolicy.Origin, IBrowserDownloadSource? source = null)
    {
        if (_disposed || _downloadCancellation != null || _viewModel is not { } vm)
        {
            source?.Dispose();
            return;
        }
        ClearPageDownloadRequest();
        NativeWebView? initiatingWebView = _webView;
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        try
        {
            BrowserDownloadOptions? options = DownloadOptionsSelector is { } selector
                ? await selector(uri, cancellation.Token)
                : await ChooseDownloadOptionsAsync(uri, cancellation.Token, suggestedName);
            if (options == null || cancellation.IsCancellationRequested) return;
            ShowDownloadRunning(uri);
            Progress<(long Received, long? Total)> progress = CreateDownloadProgress(cancellation);
            string file;
            if (source != null)
            {
                file = await source.SaveAsync(options.Directory, progress, cancellation.Token);
            }
            else
            {
                IReadOnlyList<Cookie> cookies = referrer != null && !BrowserMediaDownload.IsHttpUri(referrer)
                    ? [] : await DownloadCookiesProvider(initiatingWebView, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                file = await MediaDownloader.DownloadAsync(uri, options.Directory, suggestedName, progress, cancellation.Token, referrer, cookies, referrerPolicy);
            }
            if (_disposed || !ReferenceEquals(_viewModel, vm)) return;
            DownloadStatusText.Text = string.Format(Strings.WebDownloadComplete, Path.GetFileName(file));
            ToolTip.SetTip(DownloadStatusText, file);
            CheckProfileSave(vm.Profile.AddDownload(uri, file, referrer, referrerPolicy));
            if (options.AddToTimeline && vm.CanAddDownloadedFile(file))
            {
                try { await vm.AddDownloadedMediaAsync(file, cancellation.Token); }
                catch (Exception ex)
                {
                    if (!_disposed && ReferenceEquals(_viewModel, vm))
                        DownloadStatusText.Text = string.Format(Strings.WebDownloadImportFailed, Path.GetFileName(file), ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed) DownloadStatusText.Text = Strings.WebDownloadCanceled;
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                DownloadStatusPanel.IsVisible = true;
                DownloadStatusText.Text = string.Format(Strings.WebDownloadFailed, ex.Message);
            }
        }
        finally
        {
            source?.Dispose();
            _downloadCancellation = null;
            if (!_disposed) SetDownloadRunning(false);
            OfferNextDeferredNativeDownload();
        }
    }
}
