using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;

namespace Beutl.HeadlessUITests;

// A BrowserDownloadExtension takes over the files a page offers: the user still confirms each one in the prompt, and
// the file then goes into the project and to the extension instead of through the download options.
[TestFixture]
public sealed class BrowserDownloadExtensionTests
{
    private static readonly Uri s_speechPage = new("https://speech.example/maker");
    private readonly AudioDecoder _decoder = new();

    [SetUp]
    public void RegisterAudioDecoder() => DecoderRegistry.Register(_decoder);

    [TearDown]
    public void UnregisterAudioDecoder() => DecoderRegistry.Unregister(_decoder);

    [AvaloniaTest]
    [TestCase("\"{\\\"text\\\":\\\"こんにちは\\\"}\"", "{\"text\":\"こんにちは\"}", TestName = "A quoted result, as WebView2 returns it")]
    [TestCase("{\"text\":\"こんにちは\"}", "{\"text\":\"こんにちは\"}", TestName = "The string itself, as WebKit returns it")]
    [TestCase("null", null, TestName = "A script that found nothing")]
    public async Task AClaimedFileSkipsTheOptionsAndReachesTheExtensionWithWhatThePageShowed(string scriptResult, string? pageData)
    {
        using var temporary = new TemporaryDirectory();
        using var browser = Browser.Open(temporary.Path, s_speechPage);
        var scripts = new List<string>();
        browser.View.PageScriptRunner = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>(scriptResult);
        };
        var imported = browser.ImportCompletion();

        browser.View.OnGeneratedDownloadRequested(null, "speech.mp3", new GeneratedSource("speech.mp3"));
        Dispatcher.UIThread.RunJobs();
        var progressText = browser.View.FindControl<TextBlock>("DownloadProgressText")!;
        Assert.Multiple(() =>
        {
            // The page was read when it offered the file, before the user confirmed it.
            Assert.That(scripts, Is.EqualTo(new[] { SpeechSiteExtension.Script }));
            Assert.That(progressText.Text, Is.EqualTo(SpeechSiteExtension.Description));
            Assert.That(ToolTip.GetTip(progressText), Is.EqualTo("speech.mp3"));
        });

        browser.Confirm();
        Assert.That(browser.View.FindControl<ContentControl>("ToolPanelContent")!.Content,
            Is.Not.InstanceOf<BrowserDownloadOptionsView>());
        await imported.WaitAsync(TimeSpan.FromSeconds(5));

        BrowserDownloadImportContext import = browser.Extension.Imports.Single();
        Assert.Multiple(() =>
        {
            Assert.That(Path.GetDirectoryName(import.FileName), Is.EqualTo(Path.Combine(temporary.Path, "resources", "downloads")));
            Assert.That(File.Exists(import.FileName), Is.True);
            Assert.That(import.PageData, Is.EqualTo(pageData));
            Assert.That(import.EditorContext, Is.SameAs(browser.Context));
            Assert.That(import.Offer.Page, Is.EqualTo(s_speechPage));
            // A blob: address cannot be requested again, so the page that made the file stands for it.
            Assert.That(import.Offer.Source, Is.EqualTo(s_speechPage));
            Assert.That(import.Offer.IsGenerated, Is.True);
            Assert.That(import.Offer.SuggestedName, Is.EqualTo("speech.mp3"));
            Assert.That(browser.Context.Imported, Is.Empty);
            Assert.That(browser.ViewModel.Profile.Downloads.Single().FilePath, Is.EqualTo(import.FileName));
        });
    }

    [AvaloniaTest]
    public async Task AFileTheExtensionDeclinesGoesToTheTimelineAsOneElement()
    {
        using var temporary = new TemporaryDirectory();
        using var browser = Browser.Open(temporary.Path, s_speechPage);
        browser.Extension.Import = _ => ValueTask.FromResult(false);
        var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Context.OnImport = () => added.TrySetResult();

        browser.View.OnGeneratedDownloadRequested(null, "speech.mp3", new GeneratedSource("speech.mp3"));
        Dispatcher.UIThread.RunJobs();
        browser.Confirm();
        await added.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(browser.Extension.Imports, Has.Count.EqualTo(1));
            Assert.That(((ElementSource.File)browser.Context.Imported.Single().Source).FileName,
                Is.EqualTo(browser.Extension.Imports[0].FileName));
        });
    }

    [AvaloniaTest]
    public void AnOfferNoExtensionClaimsKeepsTheDownloadOptions()
    {
        using var temporary = new TemporaryDirectory();
        using var browser = Browser.Open(temporary.Path, new Uri("https://other.example/"));
        var scripts = new List<string>();
        browser.View.PageScriptRunner = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>(null);
        };

        browser.View.OnGeneratedDownloadRequested(null, "speech.mp3", new GeneratedSource("speech.mp3"));
        Dispatcher.UIThread.RunJobs();
        Assert.That(browser.View.FindControl<TextBlock>("DownloadProgressText")!.Text, Is.EqualTo("speech.mp3"));
        browser.Confirm();

        Assert.Multiple(() =>
        {
            Assert.That(browser.Extension.Offers.Single().Page, Is.EqualTo(new Uri("https://other.example/")));
            Assert.That(scripts, Is.Empty);
            Assert.That(browser.View.FindControl<ContentControl>("ToolPanelContent")!.Content,
                Is.InstanceOf<BrowserDownloadOptionsView>());
            Assert.That(browser.Extension.Imports, Is.Empty);
        });
    }

    [AvaloniaTest]
    public void AnEditorThatCannotSaveIntoAProjectOffersNothingToExtensions()
    {
        var extension = new SpeechSiteExtension();
        var provider = new ExtensionProvider();
        provider.AddExtensions(-47004, [extension]);
        // A scene that was never saved has no project folder to put the file in.
        var context = new BrowserEditorContext(new Scene(), extensions: provider);
        using var vm = new WebBrowserTabViewModel(context, s_speechPage);
        using var view = CreateView(vm);
        var window = new Window { Content = view };
        try
        {
            window.Show();
            view.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = s_speechPage, IsSuccess = true });
            view.OnGeneratedDownloadRequested(null, "speech.mp3", new GeneratedSource("speech.mp3"));
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(extension.Offers, Is.Empty);
                Assert.That(view.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible, Is.True);
                Assert.That(view.FindControl<TextBlock>("DownloadProgressText")!.Text, Is.EqualTo("speech.mp3"));
            });
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task AFailedImportIsReportedAndKeepsTheSavedFile()
    {
        using var temporary = new TemporaryDirectory();
        using var browser = Browser.Open(temporary.Path, s_speechPage);
        browser.Extension.Import = _ => throw new InvalidOperationException("No character for this voice.");
        browser.View.PageScriptRunner = _ => throw new InvalidOperationException("The page has gone.");

        browser.View.OnGeneratedDownloadRequested(null, "speech.mp3", new GeneratedSource("speech.mp3"));
        Dispatcher.UIThread.RunJobs();
        browser.Confirm();
        await WaitUntilAsync(() => browser.Extension.Imports.Count == 1 && !browser.View.FindControl<Button>("DownloadCancelButton")!.IsVisible);

        BrowserDownloadImportContext import = browser.Extension.Imports.Single();
        Assert.Multiple(() =>
        {
            Assert.That(import.PageData, Is.Null);
            Assert.That(File.Exists(import.FileName), Is.True);
            Assert.That(browser.View.FindControl<TextBlock>("DownloadStatusText")!.Text, Is.EqualTo(string.Format(
                Beutl.Language.Strings.WebDownloadImportFailed, Path.GetFileName(import.FileName), "No character for this voice.")));
            Assert.That(browser.Context.Imported, Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task AFileOfferedWhileAnotherWaitsKeepsWhatThePageShowedWhenItArrived()
    {
        using var temporary = new TemporaryDirectory();
        using var browser = Browser.Open(temporary.Path, s_speechPage);
        int reads = 0;
        browser.View.PageScriptRunner = _ => Task.FromResult<string?>($"line {++reads}");

        browser.View.OnGeneratedDownloadRequested(null, "first.mp3", new GeneratedSource("first.mp3"));
        Dispatcher.UIThread.RunJobs();
        // The second file waits behind the first offer, but the page already shows what produced it.
        browser.View.OnGeneratedDownloadRequested(null, "second.mp3", new GeneratedSource("second.mp3"));
        Dispatcher.UIThread.RunJobs();
        Assert.That(reads, Is.EqualTo(2));

        var first = browser.ImportCompletion();
        browser.Confirm();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => browser.View.FindControl<Button>("ConfirmPageDownloadButton")!.IsVisible);
        var second = browser.ImportCompletion();
        browser.Confirm();
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(browser.Extension.Imports.Select(import => import.Offer.SuggestedName), Is.EqualTo(new[] { "first.mp3", "second.mp3" }));
            Assert.That(browser.Extension.Imports.Select(import => import.PageData), Is.EqualTo(new[] { "line 1", "line 2" }));
            Assert.That(reads, Is.EqualTo(2));
        });
    }

    private static WebBrowserTabView CreateView(WebBrowserTabViewModel vm)
        => new(uri => new NativeWebView { Source = uri }, () => (true, null, false)) { DataContext = vm };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("The browser did not settle in time.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    // A browser tab in a saved project, showing the given page, with SpeechSiteExtension loaded.
    private sealed class Browser : IDisposable
    {
        private readonly Window _window;

        private Browser(string root, Uri page)
        {
            var project = new Project { Uri = new Uri(Path.Combine(root, "project.bep")) };
            var scene = new Scene { Uri = new Uri(Path.Combine(root, "scene.scene")) };
            project.Items.Add(scene);
            var provider = new ExtensionProvider();
            provider.AddExtensions(-47001, [Extension]);
            Context = new BrowserEditorContext(scene, extensions: provider);
            ViewModel = new WebBrowserTabViewModel(Context, page, new BrowserProfile(Path.Combine(root, "profile.json")));
            View = CreateView(ViewModel);
            View.PageScriptRunner = _ => Task.FromResult<string?>(null);
            _window = new Window { Content = View };
            _window.Show();
            View.OnNavigationCompleted(null, new WebViewNavigationCompletedEventArgs { Request = page, IsSuccess = true });
        }

        public SpeechSiteExtension Extension { get; } = new();

        public BrowserEditorContext Context { get; }

        public WebBrowserTabViewModel ViewModel { get; }

        public WebBrowserTabView View { get; }

        public static Browser Open(string root, Uri page) => new(root, page);

        public Task ImportCompletion()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<BrowserDownloadImportContext, ValueTask<bool>> import = Extension.Import;
            Extension.Import = context =>
            {
                completion.TrySetResult();
                return import(context);
            };
            return completion.Task;
        }

        public void Confirm()
        {
            var confirm = View.FindControl<Button>("ConfirmPageDownloadButton")!;
            Assert.That(confirm.IsVisible, Is.True);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        public void Dispose()
        {
            _window.Close();
            View.Dispose();
            ViewModel.Dispose();
        }
    }

    // Takes over the files of one text-to-speech site and reads the spoken text from its page.
    private sealed class SpeechSiteExtension : BrowserDownloadExtension
    {
        public const string Script = "JSON.stringify({text: document.getElementById('text').value})";
        public const string Description = "Add as speech with its character";

        public List<BrowserDownloadOffer> Offers { get; } = [];

        public List<BrowserDownloadImportContext> Imports { get; } = [];

        public Func<BrowserDownloadImportContext, ValueTask<bool>> Import { get; set; } = _ => ValueTask.FromResult(true);

        public override BrowserDownloadClaim? Claim(BrowserDownloadOffer offer)
        {
            Offers.Add(offer);
            return offer.Page.Host == s_speechPage.Host ? new BrowserDownloadClaim { PageScript = Script, Description = Description } : null;
        }

        public override ValueTask<bool> ImportAsync(BrowserDownloadImportContext context, CancellationToken cancellationToken)
        {
            Imports.Add(context);
            return Import(context);
        }
    }

    private sealed class GeneratedSource(string name) : IBrowserDownloadSource
    {
        public bool IsGenerated => true;

        public Task<string> SaveAsync(string directory, IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken)
        {
            string staged = Path.Combine(directory, ".staging", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllBytes(staged, [0x49, 0x44, 0x33]);
            return Task.FromResult(BrowserMediaDownload.PublishGeneratedFile(staged, directory, name, cancellationToken));
        }

        public void Dispose()
        {
        }
    }

    private sealed class AudioDecoder : IDecoderInfo
    {
        public string Name => "Browser download extension tests";
        public IEnumerable<string> VideoExtensions() => [];
        public IEnumerable<string> AudioExtensions() => [".mp3"];
        public MediaReader? Open(string file, MediaOptions options) => null;
    }
}
