using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

using Moq;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserMediaDownloadTests
{
    [Test]
    public async Task Downloads_PreserveExistingFilesAndPublishOnlyCompletedMedia()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MediaHandler());
        var downloader = new BrowserMediaDownload(client);
        try
        {
            string first = await downloader.DownloadAsync(new Uri("https://example.com/clip.mp4"), directory, null, null, default);
            string second = await downloader.DownloadAsync(new Uri("https://example.com/clip.mp4"), directory, null, null, default);
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(File.ReadAllBytes(first), Has.Length.EqualTo(200000));
                Assert.That(File.ReadAllBytes(second), Has.Length.EqualTo(200000));
                Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(2));
            });
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Cancellation_RemovesPartialFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MediaHandler());
        using var cancellation = new CancellationTokenSource();
        try
        {
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await new BrowserMediaDownload(client).DownloadAsync(new Uri("https://example.com/clip.mp4"), directory,
                    null, new CancelProgress(cancellation), cancellation.Token));
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestCase("https://example.com/sound.opus", "audio/opus", "sound.opus")]
    [TestCase("https://example.com/download", "audio/opus", "download.opus")]
    [TestCase("https://example.com/sound.opus", "audio/ogg", "sound.opus")]
    public async Task OpusDownloadsAcceptTheirContentTypeAndPreserveTheExtension(string address, string mediaType, string expectedName)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MediaHandler(mediaType));
        var uri = new Uri(address);
        try
        {
            if (uri.AbsolutePath.EndsWith(".opus", StringComparison.Ordinal))
                Assert.That(BrowserMediaDownload.IsMediaLink(uri), Is.True);
            string file = await new BrowserMediaDownload(client).DownloadAsync(uri, directory, null, null, default);
            Assert.That(Path.GetFileName(file), Is.EqualTo(expectedName));
            Assert.That(File.ReadAllBytes(file), Has.Length.EqualTo(200000));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase("../../clip.mp4", "video/mp4", "clip.mp4")]
    [TestCase("..\\clip.mp4", "video/mp4", "clip.mp4")]
    [TestCase("program.exe", "video/mp4", "program.mp4")]
    [TestCase("image", "image/png", "image.png")]
    public void FileName_IsConfinedToDestinationAndUsesMediaExtension(string name, string type, string expected)
    {
        Assert.That(BrowserMediaDownload.CreateFileName(name, new Uri("https://example.com/download"), type), Is.EqualTo(expected));
    }

    [TestCase("text/html")]
    [TestCase("application/xhtml+xml")]
    public void HtmlResponse_IsNotSavedAsMedia(string mediaType)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BrowserMediaDownload.CreateFileName("clip.mp4",
            new Uri("https://example.com/clip.mp4"), mediaType));
        Assert.That(error!.Message, Is.EqualTo(Beutl.Language.Strings.WebDownloadHtmlResponse));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\"\"")]
    public void EmptyDownloadNamesFallBackToTheMediaUrl(string suggestedName)
    {
        Assert.That(BrowserMediaDownload.CreateFileName(suggestedName,
            new Uri("https://example.com/clip.mp4?download=1"), "application/octet-stream"), Is.EqualTo("clip.mp4"));
    }

    [Test]
    public async Task EmptyContentDispositionUsesTheDownloadAttributeName()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new EmptyFileNameHandler());
        try
        {
            string file = await new BrowserMediaDownload(client).DownloadAsync(new Uri("https://example.com/download"),
                directory, "clip.mp4", null, default);
            Assert.That(Path.GetFileName(file), Is.EqualTo("clip.mp4"));
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task ProjectDestinationAndImport_UseOwningProjectAndCurrentPlayhead()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var project = new Project { Uri = new Uri(Path.Combine(directory, "project.bep")) };
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "scene.scene")) };
        project.Items.Add(scene);
        var adder = new Mock<IElementAdder>();
        adder.Setup(x => x.AddAsync(It.IsAny<IReadOnlyList<ElementDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ElementDescription> descriptions, CancellationToken _) =>
                ValueTask.FromResult(ElementAddResult.Succeeded([new ElementAddItemResult(descriptions[0], new Element(), [])])));
        var clock = new Mock<IEditorClock>();
        using var time = new ReactivePropertySlim<TimeSpan>(TimeSpan.FromSeconds(12));
        clock.SetupGet(x => x.CurrentTime).Returns(time);
        var context = new Mock<IEditorContext>();
        context.SetupGet(x => x.Object).Returns(scene);
        context.Setup(x => x.GetService(typeof(IElementAdder))).Returns(adder.Object);
        context.Setup(x => x.GetService(typeof(IEditorClock))).Returns(clock.Object);
        using var vm = new WebBrowserTabViewModel(context.Object);
        Assert.That(vm.ProjectDownloadDirectory, Is.EqualTo(Path.Combine(directory, "resources", "downloads")));
        using var cancellation = new CancellationTokenSource();
        await vm.AddDownloadedMediaAsync("clip.mp4", cancellation.Token);
        adder.Verify(x => x.AddAsync(It.Is<IReadOnlyList<ElementDescription>>(items => items.Count == 1
            && ((ElementSource.File)items[0].Source).FileName == "clip.mp4"
            && items[0].Start == TimeSpan.FromSeconds(12) && items[0].Layer == 0 && items[0].Length == TimeSpan.FromSeconds(5)),
            cancellation.Token), Times.Once);
    }

    [Test]
    public void Import_AwaitsThePipelineAndReportsReturnedFailures()
    {
        var completion = new TaskCompletionSource<ElementAddResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adder = new Mock<IElementAdder>();
        adder.Setup(x => x.AddAsync(It.IsAny<IReadOnlyList<ElementDescription>>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<ElementAddResult>(completion.Task));
        var context = new Mock<IEditorContext>();
        context.SetupGet(x => x.Object).Returns(new Scene { Uri = new Uri(Path.Combine(Path.GetTempPath(), "browser-import.scene")) });
        context.Setup(x => x.GetService(typeof(IElementAdder))).Returns(adder.Object);
        using var vm = new WebBrowserTabViewModel(context.Object);

        Task pending = vm.AddDownloadedMediaAsync("clip.mp4", CancellationToken.None);
        Assert.That(pending.IsCompleted, Is.False);
        completion.SetResult(ElementAddResult.Failed(new ElementSourcePreflightFailure("Decoder unavailable")));
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.That(error!.Message, Is.EqualTo("Decoder unavailable"));
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<(long Received, long? Total)>
    {
        public void Report((long Received, long? Total) value) => cancellation.Cancel();
    }

    private sealed class MediaHandler(string mediaType = "video/mp4") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(new byte[200000]);
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    private sealed class EmptyFileNameHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "\"\"" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
