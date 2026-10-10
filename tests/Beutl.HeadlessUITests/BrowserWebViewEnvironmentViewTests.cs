using System.Reflection;
using System.Runtime.CompilerServices;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Platform;

using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Pages.SettingsPages;

using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserWebViewEnvironmentViewTests
{
    private static string ExpectedUserDataFolder =>
        Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "WebView2");

    // Attaching a NativeWebView makes it raise EnvironmentRequested and then create a real WebView2
    // environment, which a headless test thread cannot host. Invoke the subscribers as attachment would.
    private static WindowsWebView2EnvironmentRequestedEventArgs RequestWindowsEnvironment(NativeWebView webView)
    {
        var handlers = (EventHandler<WebViewEnvironmentRequestedEventArgs>?)typeof(NativeWebView)
            .GetField(nameof(NativeWebView.EnvironmentRequested), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(webView);
        var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(
            typeof(WindowsWebView2EnvironmentRequestedEventArgs));
        handlers?.Invoke(webView, args);
        return args;
    }

    [AvaloniaTest]
    public void BrowserTab_RequestsTheProfileUnderTheBeutlHome()
    {
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        NativeWebView? webView = null;
        using var view = new WebBrowserTabView(uri => webView = new NativeWebView { Source = uri }, () => (true, null, false))
        {
            DataContext = vm
        };

        Assert.That(webView, Is.Not.Null);
        Assert.That(RequestWindowsEnvironment(webView!).UserDataFolder, Is.EqualTo(ExpectedUserDataFolder));
    }

    [AvaloniaTest]
    public void SettingsPage_RequestsTheSameProfileAsTheBrowserTab()
    {
        var page = new BrowserSettingsPage(() => true);
        NativeWebView profileView = page.GetLogicalDescendants().OfType<NativeWebView>().Single();

        Assert.That(RequestWindowsEnvironment(profileView).UserDataFolder, Is.EqualTo(ExpectedUserDataFolder));
    }
}
