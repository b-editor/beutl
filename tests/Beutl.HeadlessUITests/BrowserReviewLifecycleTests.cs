using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Pages.SettingsPages;
using Beutl.ViewModels.Dock;
using Beutl.ViewModels.SettingsPages;

using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserReviewLifecycleTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ClearingContextDetachesOldSubscriptionsAndAllowsReuse(bool invalidObject)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var first = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, new Uri("https://first.example/"), profile);
        using var second = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, new Uri("https://second.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false));
        try
        {
            view.DataContext = first;
            view.DataContext = invalidObject ? new object() : null;
            profile.UpdateSettings(BrowserSearchEngine.Bing, true, true);
            var addressBox = view.FindControl<WebBrowserAddressBox>("AddressTextBox")!;
            Assert.That(addressBox.SuggestionsEnabled, Is.False);
            Assert.That(await addressBox.SuggestionProvider("query", CancellationToken.None), Is.Empty);
            Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Content, Is.Null);
            first.Dispose();
            view.DataContext = second;
            Assert.That(addressBox.SuggestionsEnabled, Is.True);
            Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Content, Is.TypeOf<NativeWebView>());
            Assert.That(((NativeWebView)view.FindControl<ContentControl>("WebViewHost")!.Content!).Source, Is.EqualTo(second.CurrentUri));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    [TestCase("https://example.com/")]
    [TestCase("search words")]
    public void MissingRuntimeKeepsItsGuidanceWhenNavigationIsRequested(string address)
    {
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        using var view = new WebBrowserTabView(_ => throw new AssertionException("No runtime"), () => (false, "missing runtime", true));
        view.DataContext = vm;
        vm.Address.Value = address;
        view.NavigateFromAddress();
        Assert.That(vm.IsLoading.Value, Is.False);
        Assert.That(vm.ErrorMessage.Value, Does.Contain("missing runtime"));
        Assert.That(vm.IsLinuxRuntimeHelpVisible.Value, Is.True);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void CredentialBearingNativeNavigationIsCanceled(bool includesSubframes)
    {
        var safe = new Uri("https://safe.example/");
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, safe);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: includesSubframes);
        view.DataContext = vm;
        var request = new WebViewNavigationStartingEventArgs { Request = new Uri("https://user:password@example.com/") };
        view.OnNavigationStarted(null, request);
        Assert.That(request.Cancel, Is.True);
        Assert.That(vm.CurrentUri, Is.EqualTo(safe));
        Assert.That(vm.IsLoading.Value, Is.False);
    }

    [AvaloniaTest]
    public void FailedReparentingRestoresPreviouslyAcquiredScopes()
    {
        int restored = 0;
        using var first = new BeutlToolDockable(new WebBrowserTabViewModel(new Mock<IEditorContext>().Object), null!)
        { ToolContent = new ReparentingContent(() => System.Reactive.Disposables.Disposable.Create(() => restored++)) };
        using var second = new BeutlToolDockable(new WebBrowserTabViewModel(new Mock<IEditorContext>().Object), null!)
        { ToolContent = new ReparentingContent(() => throw new InvalidOperationException("detach failed")) };
        Assert.Throws<InvalidOperationException>(() => BeutlDockFactory.BeginToolContentReparenting(first, second));
        Assert.That(restored, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task CookieAvailabilityUpdatesExistingSettingsBindings()
    {
        Func<Task>? action = null;
        int cleared = 0;
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var vm = new BrowserSettingsPageViewModel(new BrowserProfile(Path.Combine(root, "profile.json")), () => action);
        var page = new BrowserSettingsPage { DataContext = vm };
        var view = new NativeWebView();
        var window = new Window { Content = page };
        try
        {
            window.Show();
            var confirmation = page.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.That(vm.CanClearCookies, Is.False);
            Assert.That(confirmation.IsEnabled, Is.False);
            action = () => { cleared++; return Task.CompletedTask; };
            BrowserWebViewRegistry.Register(view);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.CanClearCookies, Is.True);
            Assert.That(confirmation.IsEnabled, Is.True);
            vm.CookieDeletionConfirmed.Value = true;
            await vm.ClearCookiesAsync();
            Assert.That(cleared, Is.EqualTo(1));
            action = null;
            BrowserWebViewRegistry.Unregister(view);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.CanClearCookies, Is.False);
            Assert.That(confirmation.IsEnabled, Is.False);
        }
        finally { BrowserWebViewRegistry.Unregister(view); window.Close(); }
    }

    [AvaloniaTest]
    public void FailedSettingsSaveRestoresTheEditingViewModel()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new BrowserSettingsPageViewModel(profile, () => null);
        try
        {
            profile.AddBookmark(new Uri("https://example.com/"), "Example");
            File.Delete(Path.Combine(root, "profile.json"));
            Directory.CreateDirectory(Path.Combine(root, "profile.json"));
            vm.SelectedEngineIndex.Value = 1;
            Assert.That(vm.SelectedEngineIndex.Value, Is.Zero);
            Assert.That(profile.Engine, Is.EqualTo(BrowserSearchEngine.Google));
            Assert.That(vm.Feedback.Value, Is.Not.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ReparentingContent(Func<IDisposable> begin) : Control, IWebViewReparentingContent
    {
        public IDisposable BeginReparenting() => begin();
    }
}
