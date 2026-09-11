using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    private int _zoomPercent = 100;
    private int _pageRevision;
    private CancellationTokenSource? _findRequest;
    internal Func<string, Task<string?>>? PageScriptRunner { get; set; }
    internal Task<string?> RunPageScriptAsync(string script) => PageScriptRunner?.Invoke(script)
        ?? _webView?.InvokeScript(script) ?? Task.FromResult<string?>(null);

    private void OnFindPageClick(object? sender, RoutedEventArgs e)
    {
        CloseBrowserPanel();
        FindPanel.IsVisible = true;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private async void OnFindTextChanged(object? sender, TextChangedEventArgs e)
    {
        _findRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _findRequest = request;
        try
        {
            await Task.Delay(150, request.Token);
            await FindInPageAsync(0);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_findRequest, request)) _findRequest = null; }
    }

    internal async Task FindInPageAsync(int direction)
    {
        int revision = _pageRevision;
        string query = FindTextBox.Text ?? string.Empty;
        try
        {
            var result = BrowserPageTools.ParseFindResult(await RunPageScriptAsync(BrowserPageTools.FindScript(query, direction)));
            if (!_disposed && revision == _pageRevision && FindTextBox.Text == query && FindPanel.IsVisible)
            {
                FindCountText.Text = result == null ? Strings.BrowserPageToolsUnavailable
                    : string.Format(Strings.BrowserFindCount, result.Index, result.Count);
            }
        }
        catch
        {
            if (!_disposed && revision == _pageRevision) FindCountText.Text = Strings.BrowserPageToolsUnavailable;
        }
    }

    private async void OnFindNextClick(object? sender, RoutedEventArgs e) => await FindInPageAsync(1);
    private async void OnFindPreviousClick(object? sender, RoutedEventArgs e) => await FindInPageAsync(-1);
    private async void OnCloseFindClick(object? sender, RoutedEventArgs e) => await CloseFindAsync();
    private async Task CloseFindAsync()
    {
        _findRequest?.Cancel();
        FindPanel.IsVisible = false;
        try { await RunPageScriptAsync(BrowserPageTools.FindScript("", 0)); }
        catch { }
    }

    private async void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await FindInPageAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); }
        else if (e.Key == Key.Escape) { e.Handled = true; await CloseFindAsync(); }
    }

    private async void OnZoomInClick(object? sender, RoutedEventArgs e) => await SetPageZoomAsync(_zoomPercent + 10);
    private async void OnZoomOutClick(object? sender, RoutedEventArgs e) => await SetPageZoomAsync(_zoomPercent - 10);
    private async void OnZoomResetClick(object? sender, RoutedEventArgs e) => await SetPageZoomAsync(100);

    internal async Task SetPageZoomAsync(int percent)
    {
        percent = Math.Clamp(percent, 50, 200);
        _zoomPercent = percent;
        int revision = _pageRevision;
        try
        {
            string? result = NormalizePageTitle(await RunPageScriptAsync(BrowserPageTools.ZoomScript(percent)));
            if (!_disposed && revision == _pageRevision && _zoomPercent == percent)
            {
                if (result == "true") ZoomResetMenuItem.Text = $"{Strings.BrowserZoomReset} ({percent}%)";
                else ShowToolStatus(Strings.BrowserPageToolsUnavailable);
            }
        }
        catch { if (!_disposed) ShowToolStatus(Strings.BrowserPageToolsUnavailable); }
    }

    private async void OnBrowserKeyDown(object? sender, KeyEventArgs e)
    {
        KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(modifier)) return;
        if (e.Key == Key.F) { e.Handled = true; OnFindPageClick(this, e); }
        else if (e.Key is Key.Add or Key.OemPlus) { e.Handled = true; await SetPageZoomAsync(_zoomPercent + 10); }
        else if (e.Key is Key.Subtract or Key.OemMinus) { e.Handled = true; await SetPageZoomAsync(_zoomPercent - 10); }
        else if (e.Key is Key.D0 or Key.NumPad0) { e.Handled = true; await SetPageZoomAsync(100); }
    }
}
