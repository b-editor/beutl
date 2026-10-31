using System.Net.Http;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.Controls;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;

using FluentAvalonia.UI.Controls;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserToolPanelsTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public void Address_input_matches_buttons_and_keeps_focus_and_text_entry(int width, bool light)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var uri = new Uri("https://example.com/long/path?q=value");
        using var vm = new WebBrowserTabViewModel(CreateContext(root), uri, new BrowserProfile(Path.Combine(root, "profile.json")));
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (false, null, false));
        view.DataContext = vm;
        vm.CompleteNavigation(uri, true, false, false);
        var window = new Window { Content = view, Width = width, Height = 400, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        try
        {
            window.Show();
            WebBrowserAddressBox address = view.FindControl<WebBrowserAddressBox>("AddressTextBox")!;
            Button menu = view.FindControl<Button>("BrowserMenuButton")!;
            address.SuggestionsEnabled = false;
            menu.Focus();
            HeadlessTestHelpers.Render();
            Border border = address.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.That(address.Theme, Is.SameAs(view.FindResource("LayerHeaderNameTextBoxTheme")));
            Assert.That(border.BorderThickness, Is.EqualTo(default(Thickness)));
            Assert.That(((ISolidColorBrush)border.Background!).Color.A, Is.Zero);
            CheckHeight();
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"address-idle-{width}-{light}");

            window.MouseMove(address.TranslatePoint(new Point(address.Bounds.Width / 2, address.Bounds.Height / 2), window)!.Value);
            HeadlessTestHelpers.Render();
            CheckHeight();
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"address-hover-{width}-{light}");

            address.Focus(NavigationMethod.Tab);
            HeadlessTestHelpers.Render();
            CheckHeight();
            Assert.That(address.SelectedText, Is.EqualTo(uri.AbsoluteUri));
            Assert.That(address.BorderThickness, Is.EqualTo(default(Thickness)));
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"address-focused-{width}-{light}");
            window.KeyTextInput("検索テスト");
            Assert.That(address.Address, Is.EqualTo("検索テスト"));
            menu.Focus();
            HeadlessTestHelpers.Render();
            Assert.That(address.Text, Is.EqualTo("検索テスト"));
            CheckHeight();

            void CheckHeight()
            {
                Assert.That(address.Bounds.Height, Is.EqualTo(menu.Bounds.Height));
                Assert.That(address.TranslatePoint(default, view)!.Value.Y,
                    Is.EqualTo(menu.TranslatePoint(default, view)!.Value.Y));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(640)]
    public async Task PanelsRemainReadableAndDownloadActionsStayVisible(int width)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "森林の木漏れ日.mp4");
        File.WriteAllText(file, "media");
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        profile.AddDownload(new Uri("https://photos.example.com/reference.png"), Path.Combine(root, "reference.png"));
        profile.AddDownload(new Uri("https://videos.example.com/forest.mp4"), file);
        var uri = new Uri("https://example.com/");
        using var vm = new WebBrowserTabViewModel(CreateContext(root), uri, profile);
        using var view = new WebBrowserTabView(url => new NativeWebView { Source = url }, () => (true, null, false));
        view.DataContext = vm;
        vm.CompleteNavigation(uri, true, false, false);
        var window = new Window { Content = view, Width = width, Height = 640, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            OpenMenu(view, Strings.BrowserDownloads);
            Dispatcher.UIThread.RunJobs();
            AssertFits(view);
            var header = view.FindControl<ToolTabBar>("ToolPanelHeader")!;
            var addressBar = view.FindControl<ToolTabBar>("AddressBar")!;
            var close = view.FindControl<Button>("CloseBrowserPanelButton")!;
            var menu = view.FindControl<Button>("BrowserMenuButton")!;
            double closeRight = close.TranslatePoint(default, header)!.Value.X + close.Bounds.Width;
            double menuRight = menu.TranslatePoint(default, addressBar)!.Value.X + menu.Bounds.Width;
            Assert.Multiple(() =>
            {
                Assert.That(header.Bounds.Height, Is.EqualTo(addressBar.Bounds.Height),
                    "the panel header should match the address bar height");
                Assert.That(header.Bounds.Width - closeRight, Is.EqualTo(addressBar.Bounds.Width - menuRight),
                    "the panel close button should have the same right inset as the address bar menu");
                Assert.That(close.Bounds.Size, Is.EqualTo(menu.Bounds.Size),
                    "the two bars should use the same icon button size");
            });
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"history-{width}");
            profile.ClearHistory();
            Dispatcher.UIThread.RunJobs();
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"history-empty-{width}");

            Task<WebBrowserTabView.BrowserDownloadOptions?> pending = view.ChooseDownloadOptionsAsync(
                new Uri("https://videos.example.com/forest-light.mp4?download=true"), default);
            Dispatcher.UIThread.RunJobs();
            AssertFits(view);
            var options = (BrowserDownloadOptionsView)view.FindControl<ContentControl>("ToolPanelContent")!.Content!;
            var confirm = options.FindControl<Button>("ConfirmDownloadButton")!;
            Point confirmPosition = confirm.TranslatePoint(default, window)!.Value;
            Assert.That(confirmPosition.Y + confirm.Bounds.Height, Is.LessThanOrEqualTo(window.Bounds.Height));
            Assert.That(options.SelectedDirectory, Is.EqualTo(Path.Combine(root, "resources", "downloads")));
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"download-options-{width}");
            window.Height = 360;
            window.UpdateLayout();
            confirmPosition = confirm.TranslatePoint(default, window)!.Value;
            Assert.That(confirmPosition.Y + confirm.Bounds.Height, Is.LessThanOrEqualTo(window.Bounds.Height));
            Assert.That(confirmPosition.Y, Is.GreaterThan(0));
            view.CloseBrowserPanel();
            Assert.That(await pending, Is.Null);

            window.Height = 220;
            view.PageScriptRunner = _ => Task.FromResult<string?>("{\"Index\":2,\"Count\":24}");
            OpenMenu(view, Strings.BrowserFind);
            view.FindControl<TextBox>("FindTextBox")!.Text = "forest";
            Dispatcher.UIThread.RunJobs();
            await view.FindInPageAsync(0);
            window.UpdateLayout();
            AssertFits(view);
            Assert.That(view.FindControl<Button>("FindNextButton")!.IsEnabled, Is.True);
            Assert.That(view.FindControl<TextBox>("FindTextBox")!.Bounds.Width, Is.GreaterThan(140));
            Assert.That(view.FindControl<TextBlock>("FindStatusText")!.IsVisible, Is.False);
            Assert.That(view.FindControl<TextBlock>("FindCountText")!.IsEffectivelyVisible, Is.True);
            Assert.That(view.FindControl<TextBlock>("FindCountText")!.Text, Is.EqualTo(string.Format(Strings.BrowserFindCount, 2, 24)));
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"find-{width}");
            view.PageScriptRunner = _ => Task.FromResult<string?>("{\"Index\":0,\"Count\":0}");
            await view.FindInPageAsync(0);
            Assert.That(view.FindControl<Button>("FindNextButton")!.IsEnabled, Is.False);
            Assert.That(view.FindControl<TextBlock>("FindStatusText")!.Text, Is.EqualTo(Strings.BrowserFindNoResults));
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"find-no-results-{width}");
            view.PageScriptRunner = _ => Task.FromResult<string?>(null);
            await view.FindInPageAsync(0);
            Assert.That(view.FindControl<TextBlock>("FindCountText")!.Text, Is.Empty);
            Assert.That(view.FindControl<TextBlock>("FindStatusText")!.Text, Is.EqualTo(Strings.BrowserFindUnavailable));
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), $"find-unavailable-{width}");
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(640)]
    public void LongPanelTitlesWrapWithoutClippingOrHidingTheCloseButton(int width)
    {
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (false, null, false));
        var window = new Window { Content = view, Width = width, Height = 360 };
        bool closed = false;
        try
        {
            string title = string.Join(" ", Enumerable.Repeat(Strings.WebDownloadMedia, 6));
            view.ShowBrowserPanel(title, new TextBlock { Text = "Content" }, () => closed = true);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AssertFits(view);

            var header = view.FindControl<ToolTabBar>("ToolPanelHeader")!;
            var addressBar = view.FindControl<ToolTabBar>("AddressBar")!;
            var titleText = view.FindControl<TextBlock>("ToolPanelTitle")!;
            var close = view.FindControl<Button>("CloseBrowserPanelButton")!;
            Point titlePosition = titleText.TranslatePoint(default, header)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(header.Bounds.Height, Is.GreaterThan(addressBar.Bounds.Height));
                Assert.That(titlePosition.Y, Is.GreaterThanOrEqualTo(0));
                Assert.That(titlePosition.Y + titleText.Bounds.Height, Is.LessThanOrEqualTo(header.Bounds.Height));
                Assert.That(close.IsEffectivelyVisible, Is.True);
            });

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(closed, Is.True);
            Assert.That(view.FindControl<Grid>("ToolPanel")!.IsVisible, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("navigation", true)]
    [TestCase("zoom", true)]
    [TestCase("same zoom", true)]
    [TestCase("same zoom", false)]
    [TestCase("current", true)]
    [TestCase("current", false)]
    public async Task ZoomFailuresOnlyAffectThePageAndZoomThatRequestedThem(string supersededBy, bool throws)
    {
        using var vm = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object, new Uri("https://original.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            getPageTitle: _ => Task.FromResult<string?>("Page"))
        { DataContext = vm };
        var pending = new TaskCompletionSource<string?>();
        view.PageScriptRunner = _ => pending.Task;
        Task zoom = view.SetPageZoomAsync(110);
        view.PageScriptRunner = _ => Task.FromResult<string?>("true");
        if (supersededBy == "navigation")
            view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs
            { Request = new Uri("https://next.example/"), IsSuccess = true });
        else if (supersededBy == "zoom")
            await view.SetPageZoomAsync(120);
        else if (supersededBy == "same zoom")
            await view.SetPageZoomAsync(110);

        view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
        { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4"}""" });
        Dispatcher.UIThread.RunJobs();
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.True);
        if (throws) pending.SetException(new InvalidOperationException("The previous script context was destroyed."));
        else pending.SetResult("false");
        await zoom;
        bool current = supersededBy == "current";
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.EqualTo(!current));
        Assert.That(view.FindControl<TextBlock>("DownloadStatusText")!.Text,
            Is.EqualTo(current ? Strings.BrowserPageToolsUnavailable : Strings.BrowserPageDownloadRequest));
    }

    [AvaloniaTest]
    public async Task FindFeedbackIndicatesWhenTheMatchLimitIsReached()
    {
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (false, null, false));
        view.FindControl<Grid>("FindPanel")!.IsVisible = true;
        view.FindControl<TextBox>("FindTextBox")!.Text = "a";
        view.PageScriptRunner = _ => Task.FromResult<string?>("{\"Index\":1,\"Count\":1000,\"LimitReached\":true}");
        await view.FindInPageAsync(0);
        Assert.That(view.FindControl<TextBlock>("FindCountText")!.Text,
            Is.EqualTo(string.Format(Strings.BrowserFindCount, 1, "1000+")));
        Assert.That(view.FindControl<Button>("FindNextButton")!.IsEnabled, Is.True);
    }

    [AvaloniaTest]
    public async Task FindFeedbackIgnoresResultsFromAnOlderRequest()
    {
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (false, null, false));
        view.FindControl<Grid>("FindPanel")!.IsVisible = true;
        var first = new TaskCompletionSource<string?>();
        var second = new TaskCompletionSource<string?>();
        view.PageScriptRunner = _ => first.Task;
        view.FindControl<TextBox>("FindTextBox")!.Text = "first";
        Task oldRequest = view.FindInPageAsync(0);
        view.PageScriptRunner = _ => second.Task;
        view.FindControl<TextBox>("FindTextBox")!.Text = "second";
        Task newRequest = view.FindInPageAsync(0);
        second.SetResult("{\"Index\":2,\"Count\":3}");
        await newRequest;
        first.SetResult("{\"Index\":0,\"Count\":0}");
        await oldRequest;
        Assert.That(view.FindControl<TextBlock>("FindCountText")!.Text, Is.EqualTo(string.Format(Strings.BrowserFindCount, 2, 3)));
        Assert.That(view.FindControl<TextBlock>("FindStatusText")!.IsVisible, Is.False);
        Assert.That(view.FindControl<Button>("FindNextButton")!.IsEnabled, Is.True);
    }

    [AvaloniaTest]
    public void UnsavedProjectsDisableUnavailableChoicesAndOtherTabsKeepTheirSelection()
    {
        var first = new BrowserDownloadOptionsView(new Uri("https://example.com/media.mp4"), "/project-a", "/materials-a", true);
        var second = new BrowserDownloadOptionsView(new Uri("https://example.com/media.mp4"), "/project-b", "/materials-b", true);
        var unsaved = new BrowserDownloadOptionsView(new Uri("https://example.com/media.mp4"), null, "/materials", false);
        var window = new Window { Content = new StackPanel { Children = { first, second, unsaved } } };
        try
        {
            window.Show();
            second.FindControl<RadioButton>("MaterialsDestination")!.IsChecked = true;
            Assert.That(first.SelectedDirectory, Is.EqualTo("/project-a"));
            Assert.That(second.SelectedDirectory, Is.EqualTo("/materials-b"));
            Assert.That(unsaved.SelectedDirectory, Is.EqualTo("/materials"));
            Assert.That(unsaved.FindControl<RadioButton>("ProjectDestination")!.IsEnabled, Is.False);
            Assert.That(unsaved.FindControl<CheckBox>("AddToTimelineCheckBox")!.IsEnabled, Is.False);
            Assert.That(unsaved.AddToTimeline, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task PendingDownloadsShowProgressAndCanBeCanceledThenDismissed()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new PendingMediaHandler());
        using var vm = new WebBrowserTabViewModel(CreateContext(root), new Uri("https://example.com/"),
            new BrowserProfile(Path.Combine(root, "profile.json")));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false));
        view.DataContext = vm;
        vm.CompleteNavigation(vm.CurrentUri, true, false, false);
        view.MediaDownloader = new BrowserMediaDownload(client);
        view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(root, false));
        var window = new Window { Content = view, Width = 320, Height = 220, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            Task pending = view.DownloadMediaAsync(new Uri("https://example.com/video.mp4"), null);
            Assert.That(view.FindControl<ProgressBar>("DownloadProgressBar")!.IsIndeterminate, Is.True);
            Assert.That(view.FindControl<ProgressBar>("DownloadProgressBar")!.IsVisible, Is.True);
            Assert.That(view.FindControl<Button>("DismissDownloadStatusButton")!.IsVisible, Is.False);
            HeadlessCapture.SaveIfRequested(window, nameof(BrowserToolPanelsTests), "download-progress-320");
            view.FindControl<Button>("DownloadCancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert.That(view.FindControl<ProgressBar>("DownloadProgressBar")!.IsVisible, Is.False);
            Assert.That(view.FindControl<Button>("DismissDownloadStatusButton")!.IsVisible, Is.True);
            view.FindControl<Button>("DismissDownloadStatusButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.FindControl<Grid>("DownloadStatusPanel")!.IsVisible, Is.False);
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void MenuBookmarksTheCurrentPageUnderItsTitleOrHost()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "profile.json");
        var uri = new Uri("https://example.com/page");
        var profile = new BrowserProfile(file);
        using var vm = new WebBrowserTabViewModel(CreateContext(root), WebBrowserTabViewModel.BlankPage, profile);
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (false, null, false)) { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 480 };
        using var notifications = new NotificationCapture();
        try
        {
            window.Show();
            Assert.That(IsMenuItemEnabled(view, Strings.BrowserBookmarkThisPage), Is.False);
            OpenMenu(view, Strings.BrowserBookmarkThisPage);
            Assert.That(profile.Bookmarks, Is.Empty);

            vm.CompleteNavigation(uri, true, false, false);
            vm.SetPageTitle(uri, "Example Page");
            Assert.That(IsMenuItemEnabled(view, Strings.BrowserBookmarkThisPage), Is.True);
            OpenMenu(view, Strings.BrowserBookmarkThisPage);
            Assert.That(profile.Bookmarks, Is.EqualTo(new[] { new BrowserBookmark(uri.AbsoluteUri, "Example Page") }));
            Notification notification = notifications.All.Single();
            Assert.Multiple(() =>
            {
                Assert.That(notification.Type, Is.EqualTo(NotificationType.Success));
                Assert.That(notification.Message, Is.EqualTo(string.Format(Strings.BrowserBookmarkAdded, "Example Page")));
            });

            vm.SetPageTitle(uri, null);
            OpenMenu(view, Strings.BrowserBookmarkThisPage);
            Assert.That(new BrowserProfile(file).Bookmarks,
                Is.EqualTo(new[] { new BrowserBookmark(uri.AbsoluteUri, "example.com") }));
        }
        finally
        {
            window.Close();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public void BookmarkSaveFailuresAreReportedWithoutReplacingAPendingDownloadOffer()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var uri = new Uri("https://example.com/page");
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"), _ => throw new IOException("write denied"));
        using var vm = new WebBrowserTabViewModel(CreateContext(root), uri, profile);
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (false, null, false)) { DataContext = vm };
        vm.CompleteNavigation(uri, true, false, false);
        view.OnNativeDownloadRequested(new Uri("https://files.example/movie.mp4"), "movie.mp4", new IdleDownloadSource());
        Dispatcher.UIThread.RunJobs();
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.True);
        using var notifications = new NotificationCapture();
        try
        {
            OpenMenu(view, Strings.BrowserBookmarkThisPage);
            Notification notification = notifications.All.Single();
            Assert.Multiple(() =>
            {
                Assert.That(profile.Bookmarks, Is.Empty);
                Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
                Assert.That(notification.Message, Is.EqualTo(string.Format(Strings.BrowserStorageError, "write denied")));
                Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.True);
                Assert.That(view.FindControl<TextBlock>("DownloadStatusText")!.Text, Is.EqualTo(Strings.BrowserPageDownloadRequest));
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public void HistoryOpensOnlyMediaDirectly()
    {
        using var temporary = new TemporaryDirectory();
        Directory.CreateDirectory(temporary.Path);
        string media = Path.Combine(temporary.Path, "clip.mp4");
        string program = Path.Combine(temporary.Path, "Setup.exe");
        File.WriteAllText(media, "media");
        File.WriteAllText(program, "program");
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        profile.AddDownload(new Uri("https://files.example/clip.mp4"), media);
        profile.AddDownload(new Uri("https://files.example/Setup.exe"), program);
        var uri = new Uri("https://example.com/");
        using var vm = new WebBrowserTabViewModel(CreateContext(temporary.Path), uri, profile);
        using var view = new WebBrowserTabView(url => new NativeWebView { Source = url }, () => (true, null, false));
        view.DataContext = vm;
        vm.CompleteNavigation(uri, true, false, false);
        var window = new Window { Content = view, Width = 640, Height = 640 };
        try
        {
            window.Show();
            OpenMenu(view, Strings.BrowserDownloads);
            Dispatcher.UIThread.RunJobs();
            Button Find(string file, string feature) => view.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Tag is BrowserDownloadRecord record && record.FilePath == file
                && Beutl.Editor.Components.Helpers.UsageTracking.GetFeature(button) == feature);

            // Opening hands the file to the system, which would run a program; other files keep the folder button.
            Assert.Multiple(() =>
            {
                Assert.That(Find(media, "WebBrowser.OpenHistoryFile").IsVisible, Is.True);
                Assert.That(Find(program, "WebBrowser.OpenHistoryFile").IsVisible, Is.False);
                Assert.That(Find(program, "WebBrowser.OpenHistoryFolder").IsVisible, Is.True);
            });
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void HistoryOffersNoRetryForAFileThePageGenerated()
    {
        using var temporary = new TemporaryDirectory();
        Directory.CreateDirectory(temporary.Path);
        string media = Path.Combine(temporary.Path, "clip.mp4");
        string readme = Path.Combine(temporary.Path, "README.md");
        File.WriteAllText(media, "media");
        File.WriteAllText(readme, "# Beutl");
        var page = new Uri("https://github.com/b-editor/beutl/blob/main/README.md");
        var profile = new BrowserProfile(Path.Combine(temporary.Path, "profile.json"));
        profile.AddDownload(new Uri("https://files.example/clip.mp4"), media);
        profile.AddDownload(page, readme, generated: true);
        var uri = new Uri("https://example.com/");
        using var vm = new WebBrowserTabViewModel(CreateContext(temporary.Path), uri, profile);
        using var view = new WebBrowserTabView(url => new NativeWebView { Source = url }, () => (true, null, false));
        view.DataContext = vm;
        vm.CompleteNavigation(uri, true, false, false);
        var window = new Window { Content = view, Width = 640, Height = 640 };
        try
        {
            window.Show();
            OpenMenu(view, Strings.BrowserDownloads);
            Dispatcher.UIThread.RunJobs();
            Button Find(string file, string feature) => view.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Tag is BrowserDownloadRecord record && record.FilePath == file
                && Beutl.Editor.Components.Helpers.UsageTracking.GetFeature(button) == feature);

            // Retrying requests the recorded address, which for a generated file is the page that made it.
            Assert.Multiple(() =>
            {
                Assert.That(Find(media, "WebBrowser.RetryHistory").IsVisible, Is.True);
                Assert.That(Find(readme, "WebBrowser.RetryHistory").IsVisible, Is.False);
                Assert.That(Find(readme, "WebBrowser.OpenHistoryFolder").IsVisible, Is.True);
            });
        }
        finally { window.Close(); }
    }

    private static IEditorContext CreateContext(string root)
    {
        var project = new Project { Uri = new Uri(Path.Combine(root, "project.bep")) };
        var scene = new Scene { Uri = new Uri(Path.Combine(root, "scene.scene")) };
        project.Items.Add(scene);
        var context = new Mock<IEditorContext>();
        context.SetupGet(value => value.Object).Returns(scene);
        context.Setup(value => value.GetService(typeof(IElementAdder))).Returns(new Mock<IElementAdder>().Object);
        return context.Object;
    }

    private static void OpenMenu(WebBrowserTabView view, string text)
    {
        var menu = (FAMenuFlyout)view.FindControl<Button>("BrowserMenuButton")!.Flyout!;
        menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == text).RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
    }

    private static bool IsMenuItemEnabled(WebBrowserTabView view, string text)
    {
        Button button = view.FindControl<Button>("BrowserMenuButton")!;
        var menu = (FAMenuFlyout)button.Flyout!;
        menu.ShowAt(button);
        HeadlessTestHelpers.Render();
        try
        {
            return menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == text).IsEffectivelyEnabled;
        }
        finally
        {
            menu.Hide();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void AssertFits(WebBrowserTabView view)
    {
        view.UpdateLayout();
        foreach (var control in view.GetVisualDescendants().OfType<Control>()
                     .Where(control => control is Button or TextBlock && control.IsEffectivelyVisible && control.Bounds.Width > 0))
        {
            Point position = control.TranslatePoint(default, view)!.Value;
            Assert.That(position.X, Is.GreaterThanOrEqualTo(-1), control.Name);
            Assert.That(position.X + control.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width + 1), control.Name);
        }
    }

    private sealed class IdleDownloadSource : IBrowserDownloadSource
    {
        public Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class PendingMediaHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The pending request should be canceled.");
        }
    }
}
