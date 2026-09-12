using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class WebBrowserDownloadTests
{
    [AvaloniaTest]
    public async Task DownloadOptions_ReplacementAndCancellationDoNotDismissAnotherPanel()
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()));
        using var view = new WebBrowserTabView(_ => new NativeWebView(), () => (false, null, false));
        view.DataContext = vm;
        var uri = new Uri("https://example.com/video.mp4");
        var replacement = new TextBlock { Text = "History" };
        Task<WebBrowserTabView.BrowserDownloadOptions?> first = view.ChooseDownloadOptionsAsync(uri, CancellationToken.None);
        view.ShowBrowserPanel("History", replacement);
        Assert.That(await first, Is.Null);
        Assert.That(view.FindControl<ContentControl>("ToolPanelContent")!.Content, Is.SameAs(replacement));
        Assert.That(view.FindControl<Grid>("ToolPanel")!.IsVisible, Is.True);

        using var cancellation = new CancellationTokenSource();
        Task<WebBrowserTabView.BrowserDownloadOptions?> second = view.ChooseDownloadOptionsAsync(uri, cancellation.Token);
        cancellation.Cancel();
        Assert.That(await second, Is.Null);
        Assert.That(view.FindControl<Grid>("ToolPanel")!.IsVisible, Is.False);
        Assert.That(view.FindControl<Grid>("BrowserSurface")!.IsVisible, Is.True);

        Task<WebBrowserTabView.BrowserDownloadOptions?> third = view.ChooseDownloadOptionsAsync(uri, CancellationToken.None);
        view.Dispose();
        Assert.That(await third, Is.Null);
    }

    [AvaloniaTest]
    public async Task OptionsPanel_OffersBothDestinationsAndRestoresBrowser()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var project = new Project { Uri = new Uri(Path.Combine(root, "project.bep")) };
        var scene = new Scene { Uri = new Uri(Path.Combine(root, "scene.scene")) };
        project.Items.Add(scene);
        using var vm = new WebBrowserTabViewModel(new DownloadContext(scene));
        var nativeView = new NativeWebView();
        using var view = new WebBrowserTabView(_ => nativeView, () => (true, null, false));
        view.DataContext = vm;
        var window = new Window { Content = view };
        try
        {
            window.Show();
            foreach (int destination in new[] { 0, 1 })
            {
                Task<WebBrowserTabView.BrowserDownloadOptions?> pending = view.ChooseDownloadOptionsAsync(
                    new Uri("https://example.com/media.mp4"), CancellationToken.None);
                Dispatcher.UIThread.RunJobs();
                var content = (BrowserDownloadOptionsView)view.FindControl<ContentControl>("ToolPanelContent")!.Content!;
                Assert.That(window.GetVisualDescendants().OfType<FAContentDialog>(), Is.Empty);
                var choice = content.FindControl<RadioButton>(destination == 0 ? "ProjectDestination" : "MaterialsDestination")!;
                var checkbox = content.FindControl<CheckBox>("AddToTimelineCheckBox")!;
                Assert.That(checkbox.IsEnabled, Is.True);
                Assert.That(view.FindControl<Grid>("BrowserSurface")!.IsVisible, Is.False);
                choice.IsChecked = true;
                checkbox.IsChecked = destination == 0;
                content.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ConfirmDownloadButton")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var options = await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(options!.Directory, Is.EqualTo(destination == 0
                    ? Path.Combine(root, "resources", "downloads") : BeutlEnvironment.GetMaterialsDirectoryPath()));
                Assert.That(options.AddToTimeline, Is.EqualTo(destination == 0));
                Assert.That(view.FindControl<Grid>("BrowserSurface")!.IsVisible, Is.True);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Download_UsesChosenDirectoryAndOptionalTimelineImport()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MediaHandler());
        var context = new DownloadContext(new Scene { Uri = new Uri(Path.Combine(root, "scene.scene")) });
        using var vm = new WebBrowserTabViewModel(context, WebBrowserTabViewModel.BlankPage, new BrowserProfile(Path.Combine(root, "profile.json")));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (false, null, false));
        view.DataContext = vm;
        view.MediaDownloader = new BrowserMediaDownload(client);
        try
        {
            foreach (bool add in new[] { false, true })
            {
                string directory = Path.Combine(root, add ? "project" : "materials");
                view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(directory, add));
                await view.DownloadMediaAsync(new Uri("https://example.com/video.mp4"), null);
                Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
                Assert.That(context.Imported.Count, Is.EqualTo(add ? 1 : 0));
                if (add) Assert.That(((ElementSource.File)context.Imported[0].Source).FileName, Does.StartWith(directory));
            }

            view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null);
            await view.DownloadMediaAsync(new Uri("https://example.com/canceled.mp4"), null);
            Assert.That(Directory.GetFiles(root, "*.mp4", SearchOption.AllDirectories), Has.Length.EqualTo(2));
            Assert.That(context.Imported, Has.Count.EqualTo(1));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task LinkDownloadsKeepTheInitiatingSiteWhileChoosingOptions()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var page = new Uri("https://soundeffect-lab.info/sound/button/?private=value#section");
        var context = new DownloadContext(new Scene { Uri = new Uri(Path.Combine(root, "scene.scene")) });
        using var vm = new WebBrowserTabViewModel(context, page, new BrowserProfile(Path.Combine(root, "profile.json")));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false));
        view.DataContext = vm;
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        view.MediaDownloader = new BrowserMediaDownload(client);
        var options = new TaskCompletionSource<WebBrowserTabView.BrowserDownloadOptions?>();
        view.DownloadOptionsSelector = (_, _) => options.Task;
        var imported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnImport = () => imported.TrySetResult();
        var window = new Window { Content = view };
        try
        {
            window.Show();
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            {
                Body = """{"kind":"beutl-download","url":"https://soundeffect-lab.info/sound/button/mp3/decision1.mp3","name":"sound.mp3"}"""
            });
            Dispatcher.UIThread.RunJobs();
            view.FindControl<Button>("ConfirmPageDownloadButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            vm.CompleteNavigation(new Uri("https://elsewhere.example/"), true, false, false);
            options.SetResult(new(root, true));
            await imported.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(handler.Referrer, Is.EqualTo(new Uri("https://soundeffect-lab.info/")));
            Assert.That(vm.Profile.Downloads.Single().Referrer, Is.EqualTo("https://soundeffect-lab.info/"));

            var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.OnImport = () => retried.TrySetResult();
            view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(root, true));
            var menu = (FAMenuFlyout)view.FindControl<Button>("BrowserMenuButton")!.Flyout!;
            menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Beutl.Language.Strings.BrowserDownloads)
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            view.FindControl<ContentControl>("ToolPanelContent")!.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(ToolTip.GetTip(button), Beutl.Language.Strings.BrowserRetry))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await retried.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(context.Imported, Has.Count.EqualTo(2));
            Assert.That(handler.Referrer, Is.EqualTo(new Uri("https://soundeffect-lab.info/")));
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void PageMessagesCannotOpenDownloadOptionsWithoutNativeConfirmation(bool anotherPanelOpen)
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://page.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false)) { DataContext = vm };
        var existingContent = new TextBlock { Text = "Existing panel" };
        if (anotherPanelOpen) view.ShowBrowserPanel("Existing panel", existingContent);
        int optionsOpened = 0;
        Uri? requested = null;
        view.DownloadOptionsSelector = (uri, _) =>
        {
            requested = uri;
            optionsOpened++;
            return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null);
        };
        view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
        { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4","isTrusted":true}""" });
        view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
        { Body = """{"kind":"beutl-download","url":"https://attacker.example/queued.mp4"}""" });
        Dispatcher.UIThread.RunJobs();
        Assert.That(optionsOpened, Is.Zero);
        Assert.That(view.FindControl<Grid>("BrowserSurface")!.IsVisible, Is.EqualTo(!anotherPanelOpen));
        Assert.That(view.FindControl<Grid>("ToolPanel")!.IsVisible, Is.EqualTo(anotherPanelOpen));
        if (anotherPanelOpen) Assert.That(view.FindControl<ContentControl>("ToolPanelContent")!.Content, Is.SameAs(existingContent));
        var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
        Assert.That(confirm.IsVisible, Is.True);
        view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
        { Body = """{"kind":"beutl-download","url":"https://attacker.example/replacement.mp4"}""" });
        Dispatcher.UIThread.RunJobs();
        Assert.That(view.FindControl<TextBlock>("DownloadProgressText")!.Text, Is.EqualTo("https://files.example/movie.mp4"));
        confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.That(optionsOpened, Is.EqualTo(1));
        Assert.That(requested, Is.EqualTo(new Uri("https://files.example/movie.mp4")));
        Assert.That(confirm.IsVisible, Is.False);
        confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.That(optionsOpened, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void DismissingPageDownloadsSuppressesRepeatedRequestsUntilANewDocument()
    {
        var initial = new Uri("https://page.example/");
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), initial);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            getPageTitle: _ => Task.FromResult<string?>("Page"))
        { DataContext = vm };
        view.PageScriptRunner = _ => Task.FromResult<string?>("true");
        void Request()
        {
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4"}""" });
            Dispatcher.UIThread.RunJobs();
        }
        Request();
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.True);
        view.FindControl<Button>("DismissDownloadStatusButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Request();
        Assert.That(view.FindControl<Grid>("DownloadStatusPanel")!.IsVisible, Is.False);
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = initial, IsSuccess = false });
        Request();
        Assert.That(view.FindControl<Grid>("DownloadStatusPanel")!.IsVisible, Is.False);
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = initial, IsSuccess = true });
        Request();
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.True);
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void LeavingThePageInvalidatesDownloadRequestsBeforeCompletion(bool typedAddress, bool queued)
    {
        var initial = new Uri("https://page.example/");
        var next = new Uri("https://next.example/");
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), initial);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            getPageTitle: _ => Task.FromResult<string?>("Page"), navigationStartedIncludesSubframes: typedAddress)
        { DataContext = vm };
        view.PageScriptRunner = _ => Task.FromResult<string?>("true");
        int optionsOpened = 0;
        view.DownloadOptionsSelector = (_, _) => { optionsOpened++; return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null); };
        void Request()
        {
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4"}""" });
        }
        Request();
        if (!queued) Dispatcher.UIThread.RunJobs();
        if (typedAddress) { vm.Address.Value = next.AbsoluteUri; view.NavigateFromAddress(); }
        else view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = next });
        Dispatcher.UIThread.RunJobs();
        var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
        Assert.That(confirm.IsVisible, Is.False);
        confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.That(optionsOpened, Is.Zero);
        Request();
        Dispatcher.UIThread.RunJobs();
        Assert.That(confirm.IsVisible, Is.False);
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = next, IsSuccess = false });
        Request();
        Dispatcher.UIThread.RunJobs();
        Assert.That(confirm.IsVisible, Is.True);
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = next, IsSuccess = true });
        Request();
        Dispatcher.UIThread.RunJobs();
        Assert.That(confirm.IsVisible, Is.True);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task RedirectedMediaKeepsItsConfirmationAfterNavigationIsCanceled(bool showBeforeCompletion)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://original.example/"), profile);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: false)
        { DataContext = vm };
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        view.MediaDownloader = new BrowserMediaDownload(client);
        int optionsOpened = 0;
        Uri? requested = null;
        view.DownloadOptionsSelector = (uri, _) =>
        {
            requested = uri;
            optionsOpened++;
            return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(root, false));
        };
        view.DownloadCookiesProvider = (_, _) => throw new AssertionException("The canceled navigation has no confirmed document origin.");
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Downloads.CollectionChanged += (_, _) => completed.TrySetResult();
        var intermediate = new Uri("https://redirect.example/download");
        var media = new Uri("https://files.example/movie.mp4");
        try
        {
            view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = intermediate });
            var redirect = new WebViewNavigationStartingEventArgs { Request = media };
            view.OnNavigationStarted(null, redirect);
            Assert.That(redirect.Cancel, Is.True);
            Assert.That(vm.IsLoading.Value, Is.False);
            if (showBeforeCompletion) Dispatcher.UIThread.RunJobs();
            view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = intermediate, IsSuccess = false });
            Dispatcher.UIThread.RunJobs();
            var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
            Assert.That(confirm.IsVisible, Is.True);
            Assert.That(optionsOpened, Is.Zero);
            Assert.That(vm.ErrorMessage.Value, Is.Null);
            confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(optionsOpened, Is.EqualTo(1));
            Assert.That(requested, Is.EqualTo(media));
            Assert.That(handler.Referrer, Is.Null);
            Assert.That(handler.CookieHeader, Is.Null);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void AnotherMediaNavigationReplacesThePreviousDownloadOffer(bool firstOfferVisible)
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://original.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: false)
        { DataContext = vm };
        Uri? requested = null;
        view.DownloadOptionsSelector = (uri, _) =>
        {
            requested = uri;
            return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null);
        };
        var first = new Uri("https://files.example/first.mp4");
        var second = new Uri("https://files.example/second.mp4");
        view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = new Uri("https://redirect.example/download") });
        view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = first });
        if (firstOfferVisible) Dispatcher.UIThread.RunJobs();
        view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = second });
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = first, IsSuccess = false });
        view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = second, IsSuccess = false });
        Dispatcher.UIThread.RunJobs();
        var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
        Assert.That(confirm.IsVisible, Is.True);
        Assert.That(view.FindControl<TextBlock>("DownloadProgressText")!.Text, Is.EqualTo(second.AbsoluteUri));
        Assert.That(requested, Is.Null);
        confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.That(requested, Is.EqualTo(second));
    }

    [AvaloniaTest]
    [TestCase("pending")]
    [TestCase("dismissed")]
    [TestCase("finished")]
    public async Task CanceledMediaCompletionPreservesTheDisplayedPage(string offerState)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var page = new Uri("https://original.example/page");
        var media = new Uri("https://files.example/movie.mp4");
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), page, profile);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: false)
        { DataContext = vm };
        vm.CompleteNavigation(page, true, false, false);
        vm.SetPageTitle(page, "Original page");
        using var client = new HttpClient(new MediaHandler());
        view.MediaDownloader = new BrowserMediaDownload(client);
        view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(root, false));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Downloads.CollectionChanged += (_, _) => completed.TrySetResult();
        try
        {
            view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = media });
            Dispatcher.UIThread.RunJobs();
            if (offerState == "dismissed")
                view.FindControl<Button>("DismissDownloadStatusButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            else if (offerState == "finished")
            {
                view.FindControl<Button>("ConfirmPageDownloadButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = media, IsSuccess = false });
            Assert.That(vm.CurrentUri, Is.EqualTo(page));
            Assert.That(vm.Address.Value, Is.EqualTo(page.AbsoluteUri));
            Assert.That(vm.Header.Value, Is.EqualTo("Original page"));
            Assert.That(vm.IsLoading.Value, Is.False);
            Assert.That(vm.ErrorMessage.Value, Is.Null);
            var saved = new JsonObject();
            vm.WriteToJson(saved);
            Assert.That(saved["source"]!.GetValue<string>(), Is.EqualTo(page.AbsoluteUri));

            var next = new Uri("https://next.example/page");
            view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = next });
            Assert.That(vm.IsLoading.Value, Is.True);
            view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = next, IsSuccess = false });
            Assert.That(vm.CurrentUri, Is.EqualTo(next));
            Assert.That(vm.IsLoading.Value, Is.False);
            Assert.That(vm.ErrorMessage.Value, Is.Not.Empty);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void RedirectedMediaDoesNotReenableDismissedPageRequests()
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://original.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: false)
        { DataContext = vm };
        view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
        { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4"}""" });
        Dispatcher.UIThread.RunJobs();
        view.FindControl<Button>("DismissDownloadStatusButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = new Uri("https://redirect.example/download") });
        var redirect = new WebViewNavigationStartingEventArgs { Request = new Uri("https://files.example/movie.mp4") };
        view.OnNavigationStarted(null, redirect);
        Dispatcher.UIThread.RunJobs();
        Assert.That(redirect.Cancel, Is.True);
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task DownloadsAfterAbortedNavigationOmitUncertainPageMetadata(bool stopped)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var initial = new Uri("https://original.example/");
        var next = new Uri("https://failed.example/");
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), initial, profile);
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            getPageTitle: _ => Task.FromResult<string?>("Page"), navigationStartedIncludesSubframes: false)
        { DataContext = vm };
        view.PageScriptRunner = _ => Task.FromResult<string?>("true");
        using var handler = new ReferrerHandler();
        using var client = new HttpClient(handler);
        view.MediaDownloader = new BrowserMediaDownload(client);
        view.DownloadOptionsSelector = (_, _) => Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(new(root, false));
        view.DownloadCookiesProvider = (_, _) => Task.FromResult<IReadOnlyList<Cookie>>(
            [new("session", "private", "/", "files.example") { Secure = true }]);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Downloads.CollectionChanged += (_, _) => completed.TrySetResult();
        try
        {
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            { Body = """{"kind":"beutl-download","url":"https://files.example/media.mp3"}""" });
            Dispatcher.UIThread.RunJobs();
            var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
            Assert.That(confirm.IsVisible, Is.True);
            view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = next });
            if (stopped) view.OnNavigationStopped();
            else view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = next, IsSuccess = false });
            Assert.That(vm.IsLoading.Value, Is.False);
            Assert.That(confirm.IsVisible, Is.False);
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            { Body = """{"kind":"beutl-download","url":"https://files.example/media.mp3"}""" });
            Dispatcher.UIThread.RunJobs();
            Assert.That(confirm.IsVisible, Is.True);
            confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(handler.Referrer, Is.Null);
            Assert.That(handler.CookieHeader, Is.Null);
            Assert.That(profile.Downloads.Single().Referrer, Is.Null);

            completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs
            { Request = new Uri("https://files.example/page"), IsSuccess = true });
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            { Body = """{"kind":"beutl-download","url":"https://files.example/media.mp3"}""" });
            Dispatcher.UIThread.RunJobs();
            confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(handler.Referrer, Is.EqualTo(new Uri("https://files.example/")));
            Assert.That(handler.CookieHeader, Is.EqualTo("session=private"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void AmbiguousNavigationInvalidatesAnOfferWithoutReenablingDismissedRequests()
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://page.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: true)
        { DataContext = vm };
        void Request()
        {
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4"}""" });
            Dispatcher.UIThread.RunJobs();
        }
        Request();
        view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = new Uri("https://other.example/") });
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.False);
        Request();
        view.FindControl<Button>("DismissDownloadStatusButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        view.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = new Uri("https://frame.example/") });
        Request();
        Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void PageDownloadRequestsExpireWithTheirDocumentOrContext(bool clearContext, bool queued)
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://page.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            getPageTitle: _ => Task.FromResult<string?>("Page"))
        { DataContext = vm };
        view.PageScriptRunner = _ => Task.FromResult<string?>("true");
        int optionsOpened = 0;
        view.DownloadOptionsSelector = (_, _) => { optionsOpened++; return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null); };
        view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
        { Body = """{"kind":"beutl-download","url":"https://files.example/movie.mp4"}""" });
        if (!queued) Dispatcher.UIThread.RunJobs();
        if (clearContext) view.DataContext = null;
        else view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = new Uri("https://next.example/"), IsSuccess = true });
        Dispatcher.UIThread.RunJobs();
        var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
        Assert.That(confirm.IsVisible, Is.False);
        confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.That(optionsOpened, Is.Zero);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void PageMediaNavigationsAndPopupsAlsoRequireNativeConfirmation(bool popup)
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://page.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false),
            navigationStartedIncludesSubframes: false)
        { DataContext = vm };
        int optionsOpened = 0;
        view.DownloadOptionsSelector = (_, _) => { optionsOpened++; return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(null); };
        var media = new Uri("https://files.example/movie.mp4");
        if (popup)
        {
            var request = new WebViewNewWindowRequestedEventArgs { Request = media };
            view.OnNewWindowRequested(null, request);
            Assert.That(request.Handled, Is.True);
        }
        else
        {
            var request = new WebViewNavigationStartingEventArgs { Request = media };
            view.OnNavigationStarted(null, request);
            Assert.That(request.Cancel, Is.True);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.That(optionsOpened, Is.Zero);
        view.FindControl<Button>("ConfirmPageDownloadButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.That(optionsOpened, Is.EqualTo(1));
    }

    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(640)]
    public void PageDownloadNotificationStaysCompactAndKeepsThePageVisible(int width)
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), new Uri("https://page.example/"));
        using var view = new WebBrowserTabView(uri => new NativeWebView { Source = uri }, () => (true, null, false)) { DataContext = vm };
        var window = new Window { Width = width, Height = 520, Content = view };
        try
        {
            window.Show();
            view.OnWebMessageReceived(null, new WebMessageReceivedEventArgs
            { Body = new JsonObject { ["kind"] = "beutl-download", ["url"] = "https://files.example/" + new string('a', 500) + ".mp4" }.ToJsonString() });
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var status = view.FindControl<Grid>("DownloadStatusPanel")!;
            var confirm = view.FindControl<Button>("ConfirmPageDownloadButton")!;
            Assert.That(view.FindControl<Grid>("BrowserSurface")!.IsVisible, Is.True);
            Assert.That(status.Bounds.Height, Is.InRange(1, 110));
            var position = confirm.TranslatePoint(default, status)!.Value;
            Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(position.X + confirm.Bounds.Width, Is.LessThanOrEqualTo(status.Bounds.Width));
            if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(width, 520), new Avalonia.Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"page-download-request-{width}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("about:blank", false)]
    [TestCase("about:blank", true)]
    [TestCase("https://page.example/current", false)]
    [TestCase("https://page.example/current", true)]
    [TestCase("about:blank", false, true)]
    [TestCase("about:blank", true, true)]
    [TestCase("https://page.example/current", false, true)]
    [TestCase("https://page.example/current", true, true)]
    public async Task TypedMediaDownloadsKeepTheNavigationState(string initialAddress, bool confirm, bool navigationPending = false)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var initialUri = new Uri(initialAddress);
        var mediaUri = new Uri("https://downloads.example/clip.mp4");
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), initialUri, profile);
        var native = new NativeWebView { Source = initialUri };
        using var view = new WebBrowserTabView(_ => native, () => (true, null, false)) { DataContext = vm };
        using var client = new HttpClient(new MediaHandler());
        view.MediaDownloader = new BrowserMediaDownload(client);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Downloads.CollectionChanged += (_, _) => completed.TrySetResult();
        Uri? requested = null;
        view.DownloadOptionsSelector = (uri, _) =>
        {
            requested = uri;
            return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(confirm ? new(root, false) : null);
        };
        vm.CompleteNavigation(initialUri, true, false, false);
        Uri currentUri = navigationPending ? new Uri("https://pending.example/page") : initialUri;
        if (navigationPending) vm.BeginNavigation(currentUri);
        string header = vm.Header.Value;
        try
        {
            vm.Address.Value = mediaUri.AbsoluteUri;
            view.NavigateFromAddress();
            if (confirm) await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(requested, Is.EqualTo(mediaUri));
            Assert.That(vm.Address.Value, Is.EqualTo(WebBrowserTabViewModel.FormatAddress(currentUri)));
            Assert.That(vm.CurrentUri, Is.EqualTo(currentUri));
            Assert.That(native.Source, Is.EqualTo(initialUri));
            Assert.That(vm.Header.Value, Is.EqualTo(header));
            Assert.That(vm.HasWebAddress.Value, Is.EqualTo(currentUri != WebBrowserTabViewModel.BlankPage));
            Assert.That(vm.IsLoading.Value, Is.EqualTo(navigationPending));
            if (navigationPending)
            {
                view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = currentUri, IsSuccess = false });
                Assert.That(vm.IsLoading.Value, Is.False);
                Assert.That(vm.ErrorMessage.Value, Is.Not.Empty);
                Assert.That(vm.CurrentUri, Is.EqualTo(currentUri));
                Assert.That(vm.AddressSuggestions, Does.Not.Contain(currentUri.AbsoluteUri));
            }
            var saved = new JsonObject();
            vm.WriteToJson(saved);
            Assert.That(saved["source"]!.GetValue<string>(), Is.EqualTo(vm.Address.Value));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void UnsavedScene_DisablesProjectDestinationAndTimelineImport()
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()));
        Assert.That(vm.ProjectDownloadDirectory, Is.Null);
        Assert.That(vm.CanAddDownloadedMedia, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task RestoredMediaTabsKeepBlankStateWhenTheDownloadIsCanceledOrCompleted(bool confirm)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var media = new Uri("https://files.example/restored.mp4");
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()), WebBrowserTabViewModel.BlankPage, profile);
        vm.ReadFromJson(new JsonObject { ["source"] = media.AbsoluteUri });
        var native = new NativeWebView();
        using var view = new WebBrowserTabView(uri => { native.Source = uri; return native; }, () => (true, null, false));
        using var client = new HttpClient(new MediaHandler());
        view.MediaDownloader = new BrowserMediaDownload(client);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Downloads.CollectionChanged += (_, _) => completed.TrySetResult();
        Uri? requested = null;
        view.DownloadOptionsSelector = (uri, _) =>
        {
            requested = uri;
            return Task.FromResult<WebBrowserTabView.BrowserDownloadOptions?>(confirm ? new(root, false) : null);
        };
        try
        {
            view.DataContext = vm;
            Dispatcher.UIThread.RunJobs();
            if (confirm) await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(requested, Is.EqualTo(media));
            Assert.That(native.Source, Is.EqualTo(WebBrowserTabViewModel.BlankPage));
            Assert.That(vm.CurrentUri, Is.EqualTo(native.Source));
            Assert.That(vm.Address.Value, Is.Empty);
            Assert.That(vm.HasWebAddress.Value, Is.False);
            Assert.That(vm.IsLoading.Value, Is.False);
            Assert.That(vm.Header.Value, Does.Not.Contain(media.Host));
            var saved = new JsonObject();
            vm.WriteToJson(saved);
            Assert.That(saved["source"]!.GetValue<string>(), Is.Empty);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class MediaHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    private sealed class ReferrerHandler : HttpMessageHandler
    {
        internal Uri? Referrer { get; private set; }
        internal string? CookieHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Referrer = request.Headers.Referrer;
            CookieHeader = request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null;
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    private sealed class DownloadContext(Scene scene) : IEditorContext, IElementAdder
    {
        public List<ElementDescription> Imported { get; } = [];
        internal Action? OnImport { get; set; }
        public CoreObject Object => scene;
        public EditorExtension Extension => null!;
        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);
        public IKnownEditorCommands? Commands => null;
        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;
        public T? FindToolTab<T>() where T : IToolContext => default;
        public bool OpenToolTab(IToolContext item) => false;
        public void CloseToolTab(IToolContext item) { }
        public object? GetService(Type type) => type == typeof(IElementAdder) ? this : null;
        public IElementSourceHandlerRegistry SourceHandlers { get; } = new ElementSourceHandlerRegistry();
        public ValueTask<ElementAddResult> AddAsync(IReadOnlyList<ElementDescription> descriptions, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Imported.AddRange(descriptions);
            OnImport?.Invoke();
            return ValueTask.FromResult(ElementAddResult.Succeeded(descriptions
                .Select(description => new ElementAddItemResult(description, new Element(), [])).ToArray()));
        }
    }
}
