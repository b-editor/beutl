using System.Net.Http;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;

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
    public async Task PanelsRemainReadableAndDownloadActionsStayVisible(int width, bool light)
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
        var window = new Window { Content = view, Width = width, Height = 640, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        try
        {
            window.Show();
            OpenMenu(view, Strings.BrowserDownloads);
            Dispatcher.UIThread.RunJobs();
            AssertFits(view);
            Capture(window, $"history-{width}-{light}");
            profile.ClearHistory();
            Dispatcher.UIThread.RunJobs();
            Capture(window, $"history-empty-{width}-{light}");

            Task<WebBrowserTabView.BrowserDownloadOptions?> pending = view.ChooseDownloadOptionsAsync(
                new Uri("https://videos.example.com/forest-light.mp4?download=true"), default);
            Dispatcher.UIThread.RunJobs();
            AssertFits(view);
            var options = (BrowserDownloadOptionsView)view.FindControl<ContentControl>("ToolPanelContent")!.Content!;
            var confirm = options.FindControl<Button>("ConfirmDownloadButton")!;
            Point confirmPosition = confirm.TranslatePoint(default, window)!.Value;
            Assert.That(confirmPosition.Y + confirm.Bounds.Height, Is.LessThanOrEqualTo(window.Bounds.Height));
            Assert.That(options.SelectedDirectory, Is.EqualTo(Path.Combine(root, "resources", "downloads")));
            Capture(window, $"download-options-{width}-{light}");
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
            Capture(window, $"find-{width}-{light}");
            view.PageScriptRunner = _ => Task.FromResult<string?>("{\"Index\":0,\"Count\":0}");
            await view.FindInPageAsync(0);
            Assert.That(view.FindControl<Button>("FindNextButton")!.IsEnabled, Is.False);
            Assert.That(view.FindControl<TextBlock>("FindStatusText")!.Text, Is.EqualTo(Strings.BrowserFindNoResults));
            Capture(window, $"find-no-results-{width}-{light}");
            view.PageScriptRunner = _ => Task.FromResult<string?>(null);
            await view.FindInPageAsync(0);
            Assert.That(view.FindControl<TextBlock>("FindCountText")!.Text, Is.Empty);
            Assert.That(view.FindControl<TextBlock>("FindStatusText")!.Text, Is.EqualTo(Strings.BrowserFindUnavailable));
            Capture(window, $"find-unavailable-{width}-{light}");
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    [TestCase("navigation")]
    [TestCase("zoom")]
    [TestCase("current")]
    public async Task ZoomFailuresOnlyAffectThePageAndZoomThatRequestedThem(string supersededBy)
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

        view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
        { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4"}""" });
        Dispatcher.UIThread.RunJobs();
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.True);
        pending.SetException(new InvalidOperationException("The previous script context was destroyed."));
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
            Capture(window, "download-progress-320");
            view.FindControl<Button>("DownloadCancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert.That(view.FindControl<ProgressBar>("DownloadProgressBar")!.IsVisible, Is.False);
            Assert.That(view.FindControl<Button>("DismissDownloadStatusButton")!.IsVisible, Is.True);
            view.FindControl<Button>("DismissDownloadStatusButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.FindControl<Grid>("DownloadStatusPanel")!.IsVisible, Is.False);
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
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

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is not { Length: > 0 } directory) return;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
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
