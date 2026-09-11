using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

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
    public void UnsavedScene_DisablesProjectDestinationAndTimelineImport()
    {
        using var vm = new WebBrowserTabViewModel(new DownloadContext(new Scene()));
        Assert.That(vm.ProjectDownloadDirectory, Is.Null);
        Assert.That(vm.CanAddDownloadedMedia, Is.False);
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

    private sealed class DownloadContext(Scene scene) : IEditorContext, IElementAdder
    {
        public List<ElementDescription> Imported { get; } = [];
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
            return ValueTask.FromResult(ElementAddResult.Succeeded(descriptions
                .Select(description => new ElementAddItemResult(description, new Element(), [])).ToArray()));
        }
    }
}
