using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Dock.Model.Core;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class WebBrowserTabLifecycleTests
{
    [AvaloniaTest]
    public void ClosingBrowserContext_ReleasesViewWithTheExistingDockHost()
    {
        var context = new TestEditorContext();
        using var vm = new WebBrowserTabViewModel(context);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false));
        view.DataContext = vm;
        var dockable = new BeutlToolDockable(vm, null!) { ToolContent = view };

        dockable.Dispose();

        Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Content, Is.Null);
        Assert.That(dockable.ToolContent, Is.Null);
    }

    [AvaloniaTest]
    public void ReboundView_IsReleasedOnlyByItsCurrentContext()
    {
        var context = new TestEditorContext();
        using var first = new WebBrowserTabViewModel(context);
        using var second = new WebBrowserTabViewModel(context);
        var nativeView = new NativeWebView();
        using var view = new WebBrowserTabView(_ => nativeView, () => (true, null, false));
        view.DataContext = first;
        view.DataContext = second;
        first.Dispose();
        Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Content, Is.SameAs(nativeView));
        second.Dispose();
        Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Content, Is.Null);
    }

    [AvaloniaTest]
    public void DisposedBrowserView_DoesNotReactivateWhenReboundToItsOriginalContext()
    {
        var context = new TestEditorContext();
        using var browserVm = new WebBrowserTabViewModel(context);
        int browserCreations = 0;
        using var browser = new WebBrowserTabView(uri =>
        {
            browserCreations++;
            return new NativeWebView { Source = uri };
        }, () => (true, null, false));
        browser.DataContext = browserVm;
        browser.Dispose();
        browser.DataContext = null;
        browser.DataContext = browserVm;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(browserCreations, Is.EqualTo(1));
            Assert.That(browser.FindControl<ContentControl>("WebViewHost")!.Content, Is.Null);
        }
    }

    [AvaloniaTest]
    public void DownloadHistory_UsesInlinePanelAndOnlyRemovesMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "clip.mp4");
        File.WriteAllText(file, "test");
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        profile.AddDownload(new Uri("https://example.com/clip.mp4"), file);
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), WebBrowserTabViewModel.BlankPage, profile);
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (true, null, false));
        view.DataContext = vm;
        var window = new Window { Content = view, Width = 640, Height = 720 };
        try
        {
            window.Show();
            var menu = (FAMenuFlyout)view.FindControl<Button>("BrowserMenuButton")!.Flyout!;
            menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.BrowserDownloads)
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.FindControl<Grid>("ToolPanel")!.IsVisible, Is.True);
            var content = view.FindControl<ContentControl>("ToolPanelContent")!;
            Assert.That(content.GetVisualDescendants().OfType<ItemsControl>().Single().ItemCount, Is.EqualTo(1));
            if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var wide = window.CaptureRenderedFrame();
                wide?.Save(Path.Combine(directory, "downloads-640.png"), PngBitmapEncoderOptions.Default);
                window.Width = 320;
                window.UpdateLayout();
                using var narrow = window.CaptureRenderedFrame();
                narrow?.Save(Path.Combine(directory, "downloads-320.png"), PngBitmapEncoderOptions.Default);
            }
            content.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("removeHistory"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(profile.Downloads, Is.Empty);
            Assert.That(File.Exists(file), Is.True);
            Assert.That(window.GetVisualDescendants().OfType<FAContentDialog>(), Is.Empty);
            profile.AddDownload(new Uri("https://example.com/missing.mp4"), Path.Combine(root, "missing.mp4"));
            Dispatcher.UIThread.RunJobs();
            Assert.That(content.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, Strings.Open)).IsEnabled, Is.False);
            view.CloseBrowserPanel();
            Assert.That(view.FindControl<Grid>("BrowserSurface")!.IsVisible, Is.True);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void ExplicitMediaAddress_StillRequestsDownloadOnMacOS()
    {
        var google = new Uri("https://www.google.com/");
        var media = new Uri("https://example.com/video.mp4");
        var nativeView = new NativeWebView { Source = google };
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), google);
        using var view = new WebBrowserTabView(_ => nativeView, () => (true, null, false),
            navigationStartedIncludesSubframes: true);
        view.DataContext = vm;
        Uri? requested = null;
        view.DownloadOptionsSelector = (uri, _) =>
        {
            requested = uri;
            return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null);
        };
        vm.Address.Value = media.AbsoluteUri;
        view.NavigateFromAddress();
        Assert.That(requested, Is.EqualTo(media));
        Assert.That(nativeView.Source, Is.EqualTo(google));
    }

    [AvaloniaTest]
    public void SubframeNavigation_DoesNotReplaceTopLevelAddressOrStartDownloads()
    {
        var google = new Uri("https://www.google.com/");
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), google);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri },
            () => (true, null, false), navigationStartedIncludesSubframes: true);
        view.DataContext = vm;
        vm.CompleteNavigation(google, true, false, false);
        vm.SetPageTitle(google, "Google");

        foreach (string address in new[] { "https://ogs.google.com/widget/app/so?origin=https%3A%2F%2Fwww.google.com", "https://media.example/frame.mp4" })
        {
            var starting = new WebViewNavigationStartingEventArgs { Request = new Uri(address) };
            view.OnNavigationStarted(null, starting);
            Assert.Multiple(() =>
            {
                Assert.That(starting.Cancel, Is.False);
                Assert.That(vm.CurrentUri, Is.EqualTo(google));
                Assert.That(vm.Address.Value, Is.EqualTo(google.AbsoluteUri));
                Assert.That(vm.Header.Value, Is.EqualTo("Google"));
                Assert.That(vm.IsLoading.Value, Is.False);
                Assert.That(vm.AddressSuggestions, Is.EqualTo(new[] { google.AbsoluteUri }));
            });
        }

        var next = new Uri("https://example.com/page");
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = next, IsSuccess = true });
        Assert.That(vm.CurrentUri, Is.EqualTo(next));
        Assert.That(vm.Address.Value, Is.EqualTo(next.AbsoluteUri));
    }

    [AvaloniaTest]
    public void BrowserSettingsMenu_UsesApplicationSettingsHost()
    {
        var host = new SettingsHostProbe();
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(host));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (false, null, false));
        view.DataContext = vm;
        var window = new Window { Content = view };
        try
        {
            window.Show();
            var menu = (FAMenuFlyout)view.FindControl<Button>("BrowserMenuButton")!.Flyout!;
            menu.Items.OfType<FAMenuFlyoutItem>().Single(x => x.Text == Strings.BrowserSettings)
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            Assert.That(host.Owner, Is.SameAs(window));
            Assert.That(window.GetVisualDescendants().OfType<FAContentDialog>(), Is.Empty);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void BlankPage_ShowsBookmarksWithOpenAndRemoveActions_WithoutBookmarkMenu()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        var uri = new Uri("https://example.com/page");
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), WebBrowserTabViewModel.BlankPage, profile);
        using var view = new WebBrowserTabView(url => new NativeWebView { Source = url }, () => (true, null, false));
        view.DataContext = vm;
        var window = new Window { Content = view, Width = 640, Height = 720 };
        try
        {
            window.Show();
            var menu = (FAMenuFlyout)view.FindControl<Button>("BrowserMenuButton")!.Flyout!;
            Assert.That(menu.Items.OfType<FAMenuFlyoutItem>().Any(item => item.Text == Strings.BrowserBookmarks
                || item.Text == Strings.BrowserAddBookmark), Is.False);
            Assert.That(view.FindControl<Button>("BookmarkButton"), Is.Null);
            Assert.That(view.FindControl<Border>("BrowserEmptyState")!.IsVisible, Is.True);
            var intro = view.FindControl<Border>("BrowserEmptyState")!;
            Assert.That(intro.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text),
                Does.Contain(Strings.WebBrowser).And.Contain(Strings.BrowserStartDescription)
                    .And.Not.Contain(Strings.BrowserBookmarksEmpty).And.Not.Contain(Strings.BrowserBookmarksHint));
            view.FindControl<Button>("StartBrowsingButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.FindControl<WebBrowserAddressBox>("AddressTextBox")!.IsFocused, Is.True);
            Assert.That(vm.CurrentUri, Is.EqualTo(WebBrowserTabViewModel.BlankPage));
            void Capture(string name)
            {
                if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is not { Length: > 0 } directory) return;
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
            }
            Dispatcher.UIThread.RunJobs();
            Capture("empty-640");
            window.Width = 320;
            window.UpdateLayout();
            Capture("empty-320");
            window.Width = 640;
            view.FindControl<Button>("AddBookmarkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.FindControl<Border>("BookmarkEditor")!.IsVisible, Is.True);
            Assert.That(view.FindControl<Border>("BrowserEmptyState")!.IsVisible, Is.False);
            view.FindControl<TextBox>("BookmarkUrlInput")!.Text = "javascript:alert(1)";
            view.FindControl<Button>("SaveBookmarkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(profile.Bookmarks, Is.Empty);
            Assert.That(view.FindControl<TextBlock>("BookmarkEditorError")!.Text, Is.Not.Empty);
            Assert.That(view.FindControl<TextBlock>("BookmarkEditorError")!.IsVisible, Is.True);
            view.FindControl<TextBox>("BookmarkUrlInput")!.Text = uri.AbsoluteUri;
            view.FindControl<TextBox>("BookmarkNameInput")!.Text = "Example";
            view.FindControl<Button>("SaveBookmarkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.FindControl<Border>("BookmarkEditor")!.IsVisible, Is.False);
            Assert.That(view.FindControl<Border>("BrowserEmptyState")!.IsVisible, Is.False);
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .Select(text => text.Text), Does.Not.Contain(Strings.NewTab)
                .And.Not.Contain(Strings.WebBrowser)
                .And.Not.Contain(Strings.BrowserStartDescription)
                .And.Not.Contain(Strings.BrowserStartSearch)
                .And.Not.Contain(Strings.BrowserBookmarks));
            Capture("bookmarks-640");
            Assert.That(profile.Bookmarks.Single().Url, Is.EqualTo(uri.AbsoluteUri));
            vm.CompleteNavigation(WebBrowserTabViewModel.BlankPage, true, false, false);
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.FindControl<ScrollViewer>("BlankPagePanel")!.IsVisible, Is.True);
            var items = view.FindControl<ItemsControl>("BookmarkItems")!;
            Assert.That(items.ItemCount, Is.EqualTo(1));
            Assert.That(items.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text),
                Does.Contain("example.com").And.Not.Contain(uri.AbsoluteUri));
            items.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("removeBookmark"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(profile.Bookmarks, Is.Empty);
            Assert.That(view.FindControl<Border>("BrowserEmptyState")!.IsVisible, Is.True);
            Assert.That(view.FindControl<StackPanel>("BookmarkListPanel")!.IsVisible, Is.False);
            profile.AddBookmark(uri, "Example");
            Dispatcher.UIThread.RunJobs();
            items.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("openBookmark"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(vm.CurrentUri, Is.EqualTo(uri));
            Assert.That(view.FindControl<ScrollViewer>("BlankPagePanel")!.IsVisible, Is.False);
            Assert.That(window.GetVisualDescendants().OfType<FAContentDialog>(), Is.Empty);
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public void BlankPage_RestoresBookmarksAndSwitchesStatesWithoutHorizontalOverflow(int width, bool lightTheme)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        profile.AddBookmark(new Uri("https://www.pexels.com/ja-jp/videos/"), "Pexels — 動画素材");
        profile.AddBookmark(new Uri("https://www.youtube.com/"), "YouTube");
        profile.AddBookmark(new Uri("https://docs.beutl.com/reference/a-long-page-address"),
            "Beutl ドキュメント — エフェクトとアニメーションのリファレンス");
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), WebBrowserTabViewModel.BlankPage,
            new BrowserProfile(Path.Combine(root, "profile.json")));
        using var view = new WebBrowserTabView(url => new NativeWebView { Source = url }, () => (true, null, false));
        view.DataContext = vm;
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 520,
            RequestedThemeVariant = lightTheme ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var blank = view.FindControl<ScrollViewer>("BlankPagePanel")!;
            var empty = view.FindControl<Border>("BrowserEmptyState")!;
            var list = view.FindControl<StackPanel>("BookmarkListPanel")!;
            var editor = view.FindControl<Border>("BookmarkEditor")!;
            Assert.That(empty.IsVisible, Is.False);
            Assert.That(list.IsVisible, Is.True);
            Assert.That(blank.Extent.Width, Is.LessThanOrEqualTo(blank.Viewport.Width));
            foreach (var button in list.GetVisualDescendants().OfType<Button>())
            {
                Point position = button.TranslatePoint(default, blank)!.Value;
                Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(position.X + button.Bounds.Width, Is.LessThanOrEqualTo(blank.Bounds.Width));
            }
            Capture("bookmarks");

            view.FindControl<Button>("AddAnotherBookmarkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(editor.IsVisible, Is.True);
            Assert.That(empty.IsVisible, Is.False);
            Assert.That(list.IsVisible, Is.False);
            Capture("editor");
            view.FindControl<Button>("CancelBookmarkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(editor.IsVisible, Is.False);
            Assert.That(list.IsVisible, Is.True);

            vm.Profile.Bookmarks.Clear();
            Dispatcher.UIThread.RunJobs();
            Assert.That(empty.IsVisible, Is.True);
            Assert.That(list.IsVisible, Is.False);
            Assert.That(blank.Extent.Width, Is.LessThanOrEqualTo(blank.Viewport.Width));
            Capture("empty");
            Point emptyPosition = empty.TranslatePoint(default, blank)!.Value;
            Assert.That(emptyPosition.Y + empty.Bounds.Height / 2, Is.EqualTo(blank.Bounds.Height / 2).Within(1));

            vm.Profile.AddBookmark(new Uri("https://example.com/"), "Example");
            Assert.That(empty.IsVisible, Is.False);
            Assert.That(list.IsVisible, Is.True);

            void Capture(string state)
            {
                if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is not { Length: > 0 } directory) return;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, $"{state}-{width}-{(lightTheme ? "light" : "dark")}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task PrivacyChanges_CancelPendingSuggestionsAcrossTabsAndClearAddressHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var firstVm = new WebBrowserTabViewModel(new TestEditorContext(), WebBrowserTabViewModel.BlankPage, profile);
        using var secondVm = new WebBrowserTabViewModel(new TestEditorContext(), WebBrowserTabViewModel.BlankPage, profile);
        using var first = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (false, null, false));
        using var second = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (false, null, false));
        first.DataContext = firstVm;
        second.DataContext = secondVm;
        var window = new Window { Content = new StackPanel { Children = { first, second } } };
        try
        {
            window.Show();
            var address = first.FindControl<WebBrowserAddressBox>("AddressTextBox")!;
            address.Focus();
            Dispatcher.UIThread.RunJobs();
            var response = new TaskCompletionSource<IReadOnlyList<string>>();
            address.SuggestionDelay = TimeSpan.Zero;
            address.SuggestionProvider = (_, _) => response.Task;
            Task pending = address.RefreshSearchSuggestionsAsync("old query");
            profile.UpdateSettings(BrowserSearchEngine.Bing, false, false);
            response.SetResult(["stale result"]);
            await pending;
            Assert.That(first.FindControl<Popup>("SearchSuggestionsPopup")!.IsOpen, Is.False);
            Assert.That(address.SuggestionsEnabled, Is.False);
            Assert.That(second.FindControl<WebBrowserAddressBox>("AddressTextBox")!.SuggestionsEnabled, Is.False);
            firstVm.CompleteNavigation(new Uri("https://example.com/one"), true, false, false);
            secondVm.CompleteNavigation(new Uri("https://example.com/two"), true, false, false);
            profile.ClearHistory();
            Assert.That(firstVm.AddressSuggestions, Is.Empty);
            Assert.That(secondVm.AddressSuggestions, Is.Empty);
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task PageTools_UseEscapedQueriesAndBoundZoom()
    {
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (false, null, false));
        var scripts = new List<string>();
        view.PageScriptRunner = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>(script.Contains("const percent") ? "\"true\"" : "{\"Index\":1,\"Count\":2}");
        };
        view.FindControl<Grid>("FindPanel")!.IsVisible = true;
        view.FindControl<TextBox>("FindTextBox")!.Text = "quote \" and slash \\";
        await view.FindInPageAsync(0);
        Assert.That(view.FindControl<TextBlock>("FindCountText")!.Text, Does.Contain("1").And.Contain("2"));
        await view.SetPageZoomAsync(300);
        Assert.That(view.FindControl<FAMenuFlyoutItem>("ZoomResetMenuItem")!.Text, Does.EndWith("(200%)"));
        Assert.That(scripts[0], Does.Contain(System.Text.Json.JsonSerializer.Serialize("quote \" and slash \\")));
        await view.SetPageZoomAsync(10);
        Assert.That(view.FindControl<FAMenuFlyoutItem>("ZoomResetMenuItem")!.Text, Does.EndWith("(50%)"));
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public void SearchSuggestions_DoNotResizePageWhenOpenedOrClosed(int width, bool light)
    {
        var uri = new Uri("https://example.com/page");
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), uri);
        using var view = new WebBrowserTabView(url => new NativeWebView { Source = url }, () => (true, null, false));
        view.DataContext = vm;
        vm.CompleteNavigation(uri, true, false, false);
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 500,
            RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark
        };
        try
        {
            window.Show();
            var address = view.FindControl<WebBrowserAddressBox>("AddressTextBox")!;
            address.SuggestionDelay = TimeSpan.Zero;
            address.SuggestionProvider = (_, _) => Task.FromResult<IReadOnlyList<string>>(["avalonia", "avalonia tutorial", "avalonia examples"]);
            address.Focus();
            Dispatcher.UIThread.RunJobs();
            Rect pageBounds = view.FindControl<ContentControl>("WebViewHost")!.Bounds;
            Point? pagePosition = view.FindControl<ContentControl>("WebViewHost")!.TranslatePoint(default, view);
            window.KeyTextInput("ava");
            window.UpdateLayout();
            var popup = view.FindControl<Popup>("SearchSuggestionsPopup")!;
            Assert.That(popup.IsOpen, Is.True);
            Assert.That(popup.ShouldUseOverlayLayer, Is.False);
            Assert.That(popup.Child!.Bounds.Width, Is.EqualTo(address.Bounds.Width).Within(1));
            Assert.That(address.IsFocused, Is.True);
            Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Bounds, Is.EqualTo(pageBounds));
            Assert.That(view.FindControl<ContentControl>("WebViewHost")!.TranslatePoint(default, view), Is.EqualTo(pagePosition));
            if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, $"suggestions-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.UpdateLayout();
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(address.IsFocused, Is.True);
            Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Bounds, Is.EqualTo(pageBounds));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void SearchSuggestions_AllowAddressClicksAndDismissWithoutConsumingOutsideClicks()
    {
        using var vm = new WebBrowserTabViewModel(new TestEditorContext());
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false));
        view.DataContext = vm;
        int clicks = 0;
        var other = new Button { Content = "Other", Height = 36 };
        other.Click += (_, _) => clicks++;
        Grid.SetRow(other, 1);
        var window = new Window { Content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { view, other } }, Width = 360, Height = 500 };
        try
        {
            window.Show();
            var address = view.FindControl<WebBrowserAddressBox>("AddressTextBox")!;
            var popup = view.FindControl<Popup>("SearchSuggestionsPopup")!;
            address.SuggestionDelay = TimeSpan.Zero;
            address.SuggestionProvider = (_, _) => Task.FromResult<IReadOnlyList<string>>(["suggestion"]);
            address.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("query");
            window.UpdateLayout();
            Point addressPoint = address.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(addressPoint, MouseButton.Left);
            window.MouseUp(addressPoint, MouseButton.Left);
            Assert.That(popup.IsOpen, Is.True);
            Point otherPoint = other.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(otherPoint, MouseButton.Left);
            window.MouseUp(otherPoint, MouseButton.Left);
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(clicks, Is.EqualTo(1));

            address.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("another query");
            Assert.That(popup.IsOpen, Is.True);
            object? content = window.Content;
            window.Content = null;
            Assert.That(popup.IsOpen, Is.False);
            window.Content = content;
            Assert.That(popup.IsOpen, Is.False);
            address.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("last query");
            Assert.That(popup.IsOpen, Is.True);
            view.Dispose();
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(view.FindControl<ListBox>("SearchSuggestionsList")!.ItemCount, Is.Zero);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void SearchSuggestions_KeyboardSelectionNavigatesToSearch()
    {
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false));
        using var vm = new WebBrowserTabViewModel(new TestEditorContext());
        view.DataContext = vm;
        var address = view.FindControl<WebBrowserAddressBox>("AddressTextBox")!;
        address.SuggestionDelay = TimeSpan.Zero;
        address.SuggestionProvider = (_, _) => Task.FromResult<IReadOnlyList<string>>(["avalonia tutorial"]);
        var window = new Window { Content = view };
        try
        {
            window.Show();
            address.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("ava");
            Assert.That(view.FindControl<Popup>("SearchSuggestionsPopup")!.IsOpen, Is.True);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.That(vm.Address.Value, Is.EqualTo("https://www.google.com/search?q=avalonia%20tutorial"));
            Assert.That(view.FindControl<Popup>("SearchSuggestionsPopup")!.IsOpen, Is.False);

            address.SelectAll();
            address.SuggestionProvider = (_, _) => Task.FromResult<IReadOnlyList<string>>(["mouse choice"]);
            window.KeyTextInput("mouse");
            window.UpdateLayout();
            var list = view.FindControl<ListBox>("SearchSuggestionsList")!;
            Control item = list.ContainerFromIndex(0)!;
            var popupRoot = TopLevel.GetTopLevel(item)!;
            Point point = item.TranslatePoint(new Point(8, 8), popupRoot)!.Value;
            popupRoot.MouseDown(point, MouseButton.Left);
            popupRoot.MouseUp(point, MouseButton.Left);
            Assert.That(vm.Address.Value, Is.EqualTo("https://www.google.com/search?q=mouse%20choice"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Dispose_RemovesTheHostedNativeWebView()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false));
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);

        view.DataContext = viewModel;
        ContentControl host = view.FindControl<ContentControl>("WebViewHost")!;
        TextBox addressTextBox = view.FindControl<TextBox>("AddressTextBox")!;
        Button menuButton = view.FindControl<Button>("BrowserMenuButton")!;

        Assert.Multiple(() =>
        {
            Assert.That(host.Content, Is.SameAs(nativeWebView));
            Assert.That(host.HorizontalContentAlignment, Is.EqualTo(HorizontalAlignment.Stretch));
            Assert.That(host.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Stretch));
            Assert.That(TextBoxAttachment.GetEnterDownBehavior(addressTextBox),
                Is.EqualTo(TextBoxAttachment.EnterBehaviorMode.None));
            Assert.That(menuButton.Flyout, Is.TypeOf<FAMenuFlyout>());
        });

        view.Dispose();

        Assert.That(host.Content, Is.Null);
    }

    [AvaloniaTest]
    public void LinuxSizeRefresh_TogglesVisibilityAndRestoresItsValue()
    {
        var nativeWebView = new NativeWebView { IsVisible = true };
        var visibilityChanges = new List<bool>();
        using IDisposable subscription = nativeWebView
            .GetObservable(Visual.IsVisibleProperty)
            .Subscribe(visibilityChanges.Add);

        WebBrowserTabView.RefreshWebViewBounds(nativeWebView);

        Assert.Multiple(() =>
        {
            Assert.That(nativeWebView.IsVisible, Is.True);
            Assert.That(visibilityChanges, Does.Contain(false));
            Assert.That(visibilityChanges[^1], Is.True);
        });
    }

    [AvaloniaTest]
    public void Rebinding_ReusesTheNativeWebViewAndNavigatesToTheNewContext()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false));
        var context = new TestEditorContext();
        using var first = new WebBrowserTabViewModel(context);
        using var second = new WebBrowserTabViewModel(context);
        var secondUri = new Uri("https://example.com/second");
        second.BeginNavigation(secondUri);

        view.DataContext = first;
        view.DataContext = null;
        view.DataContext = second;

        ContentControl host = view.FindControl<ContentControl>("WebViewHost")!;
        Assert.Multiple(() =>
        {
            Assert.That(host.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebView.Source, Is.EqualTo(secondUri));
        });

        view.Dispose();
    }

    [AvaloniaTest]
    public void ReparentingWithoutANativeAdapter_LeavesTheWebViewAttached()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false));
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);
        view.DataContext = viewModel;

        IDisposable? reparentingScope = ((IWebViewReparentingContent)view).BeginReparenting();

        Assert.Multiple(() =>
        {
            Assert.That(reparentingScope, Is.Null);
            Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Content, Is.SameAs(nativeWebView));
        });
        view.Dispose();
    }

    [AvaloniaTest]
    public void FloatingBrowserDockable_DetachesNativeWebViewDuringDockMove()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false),
            canReparentWebView: _ => true);
        var context = new TestEditorContext();
        var viewModel = new WebBrowserTabViewModel(context);
        using var dockable = new BeutlToolDockable(viewModel, null!)
        {
            ToolContent = view
        };
        view.DataContext = viewModel;

        ContentControl webViewHost = view.FindControl<ContentControl>("WebViewHost")!;
        bool nativeWebViewWasDetached = false;
        using IDisposable subscription = webViewHost
            .GetObservable(ContentControl.ContentProperty)
            .Subscribe(content => nativeWebViewWasDetached |= content == null);

        var factory = new BeutlDockFactory(null!);
        IToolDock toolDock = factory.CreateToolDock();
        toolDock.IsCollapsable = false;
        toolDock.VisibleDockables = factory.CreateList<IDockable>(dockable);
        toolDock.ActiveDockable = dockable;
        IRootDock rootDock = factory.CreateRootDock();
        rootDock.VisibleDockables = factory.CreateList<IDockable>(toolDock);
        rootDock.ActiveDockable = toolDock;
        factory.InitDockable(rootDock, null);

        factory.FloatDockable(dockable);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(rootDock.Windows, Has.Count.EqualTo(1));
        });

        IDock floatingDock = (IDock)dockable.Owner!;
        nativeWebViewWasDetached = false;

        factory.MoveDockable(floatingDock, toolDock, dockable, null);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(dockable.Owner, Is.SameAs(toolDock));
        });

        nativeWebViewWasDetached = false;

        factory.FloatAllDockables(dockable);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(dockable.Owner, Is.Not.SameAs(toolDock));
        });

        IDock secondFloatingDock = (IDock)dockable.Owner!;
        ITool swapTarget = factory.CreateTool();
        factory.AddDockable(toolDock, swapTarget);
        nativeWebViewWasDetached = false;

        factory.SwapDockable(secondFloatingDock, toolDock, dockable, swapTarget);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(dockable.Owner, Is.SameAs(toolDock));
        });
    }

    [AvaloniaTest]
    public async Task CompletedNavigation_UsesTheDocumentTitleForTheToolHeader()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false),
            getPageTitle: _ => Task.FromResult<string?>("\"Example page title\""));
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);
        var uri = new Uri("https://example.com/page");
        view.DataContext = viewModel;
        viewModel.BeginNavigation(uri);
        viewModel.CompleteNavigation(uri, isSuccess: true, canGoBack: false, canGoForward: false);

        await view.UpdatePageTitleAsync(uri);

        Assert.That(viewModel.Header.Value, Is.EqualTo("Example page title"));
        view.Dispose();
    }

    [AvaloniaTest]
    public async Task MacPageNavigationFallsBackToTheNewHostWhenTitleRetrievalFails()
    {
        var first = new Uri("https://first.example/page");
        var next = new Uri("https://second.example/page");
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), first);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            getPageTitle: _ => Task.FromException<string?>(new InvalidOperationException("Script unavailable")),
            navigationStartedIncludesSubframes: true)
        { DataContext = vm };
        vm.CompleteNavigation(first, true, false, false);
        vm.SetPageTitle(first, "Previous document");
        view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = next });
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = next, IsSuccess = true });
        await view.UpdatePageTitleAsync(next);
        Assert.That(vm.CurrentUri, Is.EqualTo(next));
        Assert.That(vm.Header.Value, Does.EndWith(": second.example"));
        Assert.That(vm.IsLoading.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task OldTitleResultCannotReplaceTheTitleAfterReloadingTheSameUri()
    {
        var uri = new Uri("https://example.com/page");
        var oldTitle = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool reloaded = false;
        using var vm = new WebBrowserTabViewModel(new TestEditorContext(), uri);
        using var view = new WebBrowserTabView(source => new NativeWebView { Source = source }, () => (true, null, false),
            getPageTitle: _ => reloaded ? Task.FromResult<string?>("New document") : oldTitle.Task,
            navigationStartedIncludesSubframes: true)
        { DataContext = vm };
        Task previousRequest = view.UpdatePageTitleAsync(uri);
        reloaded = true;
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = uri, IsSuccess = true });
        await view.UpdatePageTitleAsync(uri);
        Assert.That(vm.Header.Value, Is.EqualTo("New document"));
        oldTitle.SetResult("Previous document");
        await previousRequest;
        Assert.That(vm.Header.Value, Is.EqualTo("New document"));
    }

    [AvaloniaTest]
    public void MissingLinuxRuntime_ShowsTheSetupGuideButtonWithoutCreatingAWebView()
    {
        Uri? launchedUri = null;
        var view = new WebBrowserTabView(
            _ => throw new AssertionException("A WebView must not be created when the runtime is unavailable."),
            () => (false, "Missing WebKit runtime.", true),
            uri =>
            {
                launchedUri = uri;
                return Task.FromResult(true);
            });
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);

        view.DataContext = viewModel;

        ContentControl host = view.FindControl<ContentControl>("WebViewHost")!;
        Button helpButton = view.FindControl<Button>("LinuxRuntimeHelpButton")!;
        Assert.Multiple(() =>
        {
            Assert.That(host.Content, Is.Null);
            Assert.That(helpButton.IsVisible, Is.True);
            Assert.That(viewModel.IsLinuxRuntimeHelpVisible.Value, Is.True);
            Assert.That(viewModel.ErrorMessage.Value, Does.Contain("Missing WebKit runtime."));
        });

        helpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.That(launchedUri, Is.EqualTo(WebBrowserTabView.LinuxWebViewSetupGuide));

        view.Dispose();
    }

    private sealed class TestEditorContext(IBrowserSettingsHost? settingsHost = null) : IEditorContext
    {
        public CoreObject Object { get; } = new TestCoreObject();

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public object? GetService(Type serviceType) => serviceType == typeof(IBrowserSettingsHost) ? settingsHost : null;
    }

    private sealed class SettingsHostProbe : IBrowserSettingsHost
    {
        internal Window? Owner { get; private set; }
        public Task OpenBrowserSettingsAsync(Window owner) { Owner = owner; return Task.CompletedTask; }
    }

    private sealed class TestCoreObject : CoreObject;
}
