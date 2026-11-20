using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    private int _zoomPercent = BrowserPageTools.DefaultZoomPercent;
    private int _zoomRevision;
    private int _pageRevision;
    private int _findRevision;
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
        if (!string.IsNullOrEmpty(FindTextBox.Text)) _ = FindInPageAsync(0);
        else ResetFindFeedback();
    }

    private async void OnFindTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_disposed || !FindPanel.IsVisible) return;
        ResetFindFeedback();
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

    private void ResetFindFeedback()
    {
        _findRevision++;
        FindCountText.Text = string.Empty;
        FindStatusText.IsVisible = false;
        FindPreviousButton.IsEnabled = false;
        FindNextButton.IsEnabled = false;
    }

    internal async Task FindInPageAsync(int direction)
    {
        int revision = _pageRevision;
        int request = ++_findRevision;
        string query = FindTextBox.Text ?? string.Empty;
        BrowserPageTools.FindResult? result;
        try
        {
            result = BrowserPageTools.ParseFindResult(await RunPageScriptAsync(BrowserPageTools.FindScript(query, direction)));
        }
        catch { result = null; }
        if (_disposed || revision != _pageRevision || request != _findRevision || FindTextBox.Text != query || !FindPanel.IsVisible) return;
        if (query.Length == 0)
        {
            ResetFindFeedback();
            return;
        }
        FindCountText.Text = result == null ? string.Empty : string.Format(Strings.BrowserFindCount,
            result.Index, result.LimitReached ? $"{result.Count}+" : result.Count.ToString());
        FindStatusText.Text = result == null ? Strings.BrowserFindUnavailable : Strings.BrowserFindNoResults;
        FindStatusText.IsVisible = result == null || result.Count == 0;
        FindPreviousButton.IsEnabled = result?.Count > 0;
        FindNextButton.IsEnabled = result?.Count > 0;
    }

    private async void OnFindNextClick(object? sender, RoutedEventArgs e) => await FindInPageAsync(1);
    private async void OnFindPreviousClick(object? sender, RoutedEventArgs e) => await FindInPageAsync(-1);
    private async void OnCloseFindClick(object? sender, RoutedEventArgs e) => await CloseFindAsync();
    private async Task CloseFindAsync()
    {
        _findRequest?.Cancel();
        FindPanel.IsVisible = false;
        ResetFindFeedback();
        try { await RunPageScriptAsync(BrowserPageTools.FindScript("", 0)); }
        catch { }
    }

    private async void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await FindInPageAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); }
        else if (e.Key == Key.Escape) { e.Handled = true; await CloseFindAsync(); }
    }

    private async void OnZoomInClick(object? sender, RoutedEventArgs e) => await SetPageZoomAsync(_zoomPercent + BrowserPageTools.ZoomStepPercent);
    private async void OnZoomOutClick(object? sender, RoutedEventArgs e) => await SetPageZoomAsync(_zoomPercent - BrowserPageTools.ZoomStepPercent);
    private async void OnZoomResetClick(object? sender, RoutedEventArgs e) => await SetPageZoomAsync(BrowserPageTools.DefaultZoomPercent);

    internal async Task SetPageZoomAsync(int percent)
    {
        percent = Math.Clamp(percent, BrowserPageTools.MinZoomPercent, BrowserPageTools.MaxZoomPercent);
        _zoomPercent = percent;
        int revision = _pageRevision;
        int request = ++_zoomRevision;
        try
        {
            string? result = NormalizePageTitle(await RunPageScriptAsync(BrowserPageTools.ZoomScript(percent)));
            if (!_disposed && revision == _pageRevision && request == _zoomRevision && _zoomPercent == percent)
            {
                if (result == "true") ZoomResetMenuItem.Text = $"{Strings.BrowserZoomReset} ({percent}%)";
                else ShowToolStatus(Strings.BrowserPageToolsUnavailable);
            }
        }
        catch
        {
            if (!_disposed && revision == _pageRevision && request == _zoomRevision && _zoomPercent == percent)
                ShowToolStatus(Strings.BrowserPageToolsUnavailable);
        }
    }

    private void OnBrowserKeyDown(object? sender, KeyEventArgs e)
    {
        if (TryHandleBrowserShortcut(e.Key, e.KeyModifiers)) e.Handled = true;
    }

    internal bool TryHandleBrowserShortcut(Key key, KeyModifiers modifiers)
    {
        KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!modifiers.HasFlag(modifier)) return false;
        if (key == Key.F) { OnFindPageClick(this, new RoutedEventArgs()); return true; }
        if (key is Key.Add or Key.OemPlus) { _ = SetPageZoomAsync(_zoomPercent + BrowserPageTools.ZoomStepPercent); return true; }
        if (key is Key.Subtract or Key.OemMinus) { _ = SetPageZoomAsync(_zoomPercent - BrowserPageTools.ZoomStepPercent); return true; }
        if (key is Key.D0 or Key.NumPad0) { _ = SetPageZoomAsync(BrowserPageTools.DefaultZoomPercent); return true; }
        return false;
    }

    private void OnWebViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_webView is { } webView
            && e.Source is Visual source
            && ReferenceEquals(source.FindAncestorOfType<NativeWebView>(includeSelf: true), webView)
            && !(e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.Alt))
        {
            // NativeWebView leaves KeyDown unhandled on Linux X11 (GTK3 consumes XI_KeyPress while core
            // KeyPress can still reach Avalonia) and on offscreen Linux adapters when IME forwards a key
            // with PhysicalKey.None. Stop it here so editor and shell shortcuts do not run behind the page.
            e.Handled = true;
        }
    }
}
