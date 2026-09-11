using System.Net;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;


namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    private CancellationTokenSource? _downloadCancellation;
    private PageDownloadRequest? _pendingPageDownloadRequest;
    private int _pageDownloadDocumentId;
    private bool _pageDownloadRequestsSuppressed;
    private bool _pageDownloadNavigationPending;

    private sealed record PageDownloadRequest(Uri Uri, string? SuggestedName, Uri? Referrer, int DocumentId);

    internal BrowserMediaDownload MediaDownloader { get; set; } = BrowserMediaDownload.Default;
    internal Func<Uri, CancellationToken, Task<BrowserDownloadOptions?>>? DownloadOptionsSelector { get; set; }
    internal Func<NativeWebView?, CancellationToken, Task<IReadOnlyList<Cookie>>> DownloadCookiesProvider { get; set; } =
        static async (webView, cancellation) => webView?.TryGetCookieManager() is { } manager
            ? await manager.GetCookiesAsync().WaitAsync(cancellation) : [];
    internal sealed record BrowserDownloadOptions(string Directory, bool AddToTimeline);

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
                event.preventDefault();
                event.stopImmediatePropagation();
                window.invokeCSharpAction(JSON.stringify({kind:'beutl-download',url:url.href,name:link.getAttribute('download')}));
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
        if (_disposed || _viewModel == null || _pageDownloadRequestsSuppressed || _pageDownloadNavigationPending
            || _pendingPageDownloadRequest != null || _downloadCancellation != null) return;
        if (e.Body is not { Length: > 0 and < 16384 } body) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "beutl-download"
                && root.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? uri) && BrowserMediaDownload.IsHttpUri(uri))
            {
                string? name = root.TryGetProperty("name", out var fileName) && fileName.ValueKind == JsonValueKind.String
                    ? fileName.GetString() : null;
                QueuePageDownloadRequest(uri, name);
            }
        }
        catch (JsonException)
        {
            // Ignore messages from pages that use their own bridge protocol.
        }
    }

    private void QueuePageDownloadRequest(Uri uri, string? suggestedName)
    {
        if (_disposed || _viewModel == null || _pageDownloadRequestsSuppressed || _pageDownloadNavigationPending
            || _pendingPageDownloadRequest != null || _downloadCancellation != null) return;
        var owner = _viewModel;
        var source = _webView;
        int documentId = _pageDownloadDocumentId;
        Uri? referrer = owner?.CurrentUri;
        var request = new PageDownloadRequest(uri, suggestedName, referrer, documentId);
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

    private void ClearPageDownloadRequest()
    {
        if (_pendingPageDownloadRequest != null && ConfirmPageDownloadButton.IsVisible)
        {
            DownloadStatusPanel.IsVisible = false;
            DownloadProgressText.IsVisible = false;
            DownloadProgressText.Text = string.Empty;
            ToolTip.SetTip(DownloadProgressText, null);
        }
        _pendingPageDownloadRequest = null;
        ConfirmPageDownloadButton.IsVisible = false;
        DownloadStatusText.MaxLines = 0;
        DownloadStatusText.TextTrimming = Avalonia.Media.TextTrimming.None;
        ToolTip.SetTip(DismissDownloadStatusButton, Strings.Close);
        Avalonia.Automation.AutomationProperties.SetName(DismissDownloadStatusButton, Strings.Close);
    }

    private void ResetPageDownloadRequests()
    {
        InvalidatePageDownloadRequests();
        _pageDownloadRequestsSuppressed = false;
        _pageDownloadNavigationPending = false;
    }

    private void InvalidatePageDownloadRequests(bool navigationStarted = false)
    {
        _pageDownloadDocumentId++;
        if (navigationStarted) _pageDownloadNavigationPending = true;
        ClearPageDownloadRequest();
    }

    private async void OnConfirmPageDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_pageDownloadNavigationPending || !ConfirmPageDownloadButton.IsVisible || _pendingPageDownloadRequest is not { } request) return;
        ClearPageDownloadRequest();
        if (_disposed || request.DocumentId != _pageDownloadDocumentId) return;
        await DownloadMediaAsync(request.Uri, request.SuggestedName, request.Referrer);
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
        if (_pendingPageDownloadRequest != null) _pageDownloadRequestsSuppressed = true;
        ClearPageDownloadRequest();
        DownloadStatusPanel.IsVisible = false;
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

    internal async Task<BrowserDownloadOptions?> ChooseDownloadOptionsAsync(Uri uri, CancellationToken cancellation)
    {
        if (_disposed || _viewModel is not { } vm || cancellation.IsCancellationRequested) return null;
        var content = new BrowserDownloadOptionsView(uri, vm.ProjectDownloadDirectory,
            BeutlEnvironment.GetMaterialsDirectoryPath(), vm.CanAddDownloadedMedia);
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

    internal async Task DownloadMediaAsync(Uri uri, string? suggestedName, Uri? referrer = null)
    {
        if (_disposed || _downloadCancellation != null || _viewModel is not { } vm) return;
        ClearPageDownloadRequest();
        NativeWebView? initiatingWebView = _webView;
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        try
        {
            BrowserDownloadOptions? options = await (DownloadOptionsSelector ?? ChooseDownloadOptionsAsync)(uri, cancellation.Token);
            if (options == null || cancellation.IsCancellationRequested) return;
            DownloadStatusPanel.IsVisible = true;
            SetDownloadRunning(true);
            ToolTip.SetTip(DownloadStatusText, uri.AbsoluteUri);
            DownloadStatusText.Text = Strings.WebDownloadMedia;
            var progress = new Progress<(long Received, long? Total)>(value =>
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
            IReadOnlyList<Cookie> cookies = await DownloadCookiesProvider(initiatingWebView, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            string file = await MediaDownloader.DownloadAsync(uri, options.Directory, suggestedName, progress, cancellation.Token, referrer, cookies);
            if (_disposed || !ReferenceEquals(_viewModel, vm)) return;
            DownloadStatusText.Text = string.Format(Strings.WebDownloadComplete, Path.GetFileName(file));
            ToolTip.SetTip(DownloadStatusText, file);
            CheckProfileSave(vm.Profile.AddDownload(uri, file, referrer));
            if (options.AddToTimeline)
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
            _downloadCancellation = null;
            if (!_disposed) SetDownloadRunning(false);
        }
    }
}
