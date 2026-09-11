using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;


namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    private CancellationTokenSource? _downloadCancellation;

    internal BrowserMediaDownload MediaDownloader { get; set; } = BrowserMediaDownload.Default;
    internal Func<Uri, CancellationToken, Task<BrowserDownloadOptions?>>? DownloadOptionsSelector { get; set; }
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

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
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
                Dispatcher.UIThread.Post(() => _ = DownloadMediaAsync(uri, name));
            }
        }
        catch (JsonException)
        {
            // Ignore messages from pages that use their own bridge protocol.
        }
    }

    private void OnDownloadMediaClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is { } vm && BrowserMediaDownload.IsHttpUri(vm.CurrentUri))
        {
            _ = DownloadMediaAsync(vm.CurrentUri, null);
        }
    }

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    internal async Task<BrowserDownloadOptions?> ChooseDownloadOptionsAsync(Uri uri, CancellationToken cancellation)
    {
        if (_disposed || _viewModel is not { } vm || cancellation.IsCancellationRequested) return null;
        string? projectDirectory = vm.ProjectDownloadDirectory;
        string materialsDirectory = BeutlEnvironment.GetMaterialsDirectoryPath();
        var destination = new ComboBox
        {
            ItemsSource = new[]
            {
                new ComboBoxItem { Content = Strings.WebDownloadProject, IsEnabled = projectDirectory != null },
                new ComboBoxItem { Content = Strings.WebDownloadMaterials }
            },
            SelectedIndex = projectDirectory != null ? 0 : 1,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        var location = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        void UpdateLocation() => location.Text = destination.SelectedIndex == 0 ? projectDirectory : materialsDirectory;
        destination.SelectionChanged += (_, _) => UpdateLocation();
        UpdateLocation();
        var addToTimeline = new CheckBox
        {
            Content = Strings.WebDownloadAddToTimeline,
            IsEnabled = vm.CanAddDownloadedMedia,
            IsChecked = vm.CanAddDownloadedMedia
        };
        var content = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = uri.AbsoluteUri, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 440 },
            destination, location, addToTimeline
        } };
        if (projectDirectory == null)
        {
            content.Children.Add(new TextBlock { Text = Strings.WebDownloadProjectUnavailable, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }

        var completion = new TaskCompletionSource<BrowserDownloadOptions?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = new Button { Name = "ConfirmDownloadButton", Content = Strings.Save };
        var cancel = new Button { Name = "CancelDownloadOptionsButton", Content = Strings.Cancel };
        save.Click += (_, _) => completion.TrySetResult(new BrowserDownloadOptions(
            destination.SelectedIndex == 0 ? projectDirectory! : materialsDirectory, addToTimeline.IsChecked == true));
        cancel.Click += (_, _) => completion.TrySetResult(null);
        content.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { save, cancel }
        });
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

    internal async Task DownloadMediaAsync(Uri uri, string? suggestedName)
    {
        if (_disposed || _downloadCancellation != null || _viewModel is not { } vm) return;
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        try
        {
            BrowserDownloadOptions? options = await (DownloadOptionsSelector ?? ChooseDownloadOptionsAsync)(uri, cancellation.Token);
            if (options == null || cancellation.IsCancellationRequested) return;
            DownloadStatusPanel.IsVisible = true;
            DownloadCancelButton.IsVisible = true;
            DownloadStatusText.Text = Strings.WebDownloadMedia;
            var progress = new Progress<(long Received, long? Total)>(value =>
            {
                if (!_disposed && ReferenceEquals(_downloadCancellation, cancellation) && !cancellation.IsCancellationRequested)
                {
                    DownloadStatusText.Text = value.Total is > 0
                        ? $"{Strings.WebDownloadMedia}: {value.Received * 100.0 / value.Total:0}%"
                        : $"{Strings.WebDownloadMedia}: {value.Received / 1024:N0} KB";
                }
            });
            string file = await MediaDownloader.DownloadAsync(uri, options.Directory, suggestedName, progress, cancellation.Token);
            if (_disposed || !ReferenceEquals(_viewModel, vm)) return;
            DownloadStatusText.Text = string.Format(Strings.WebDownloadComplete, file);
            CheckProfileSave(vm.Profile.AddDownload(uri, file));
            if (options.AddToTimeline)
            {
                try { await vm.AddDownloadedMediaAsync(file, cancellation.Token); }
                catch (Exception ex)
                {
                    if (!_disposed && ReferenceEquals(_viewModel, vm))
                        DownloadStatusText.Text = string.Format(Strings.WebDownloadImportFailed, file, ex.Message);
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
            if (!_disposed) DownloadCancelButton.IsVisible = false;
        }
    }
}
