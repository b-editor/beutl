using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class BrowserSettingsPage : UserControl
{
    private readonly NativeWebView? _cookieWebView;

    public BrowserSettingsPage() : this(() => WebBrowserTabView.GetWebViewAvailability().IsAvailable) { }

    internal BrowserSettingsPage(Func<bool> isWebViewAvailable)
    {
        InitializeComponent();
        if (!isWebViewAvailable()) return;
        // A hidden control initializes the same persistent profile even after the last tab closes.
        // NativeWebView initializes on visual attachment, independently of IsVisible.
        _cookieWebView = new NativeWebView { Source = WebBrowserTabViewModel.BlankPage, IsVisible = false };
        ((Grid)Content!).Children.Add(_cookieWebView);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_cookieWebView != null) BrowserWebViewRegistry.Register(_cookieWebView);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_cookieWebView != null) BrowserWebViewRegistry.Unregister(_cookieWebView);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnClearHistoryClick(object? sender, RoutedEventArgs e) =>
        (DataContext as BrowserSettingsPageViewModel)?.ClearHistory();
    private async void OnClearCookiesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BrowserSettingsPageViewModel vm) await vm.ClearCookiesAsync();
    }
}
