using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

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
    public async Task Downloads_SkipDirectoryAndFileNameCollisions()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MediaHandler());
        try
        {
            string existingDirectory = Path.Combine(directory, "clip.mp4");
            Directory.CreateDirectory(existingDirectory);
            string existingFile = Path.Combine(directory, "clip (1).mp4");
            await File.WriteAllTextAsync(existingFile, "existing media");
            string downloaded = await new BrowserMediaDownload(client).DownloadAsync(
                new Uri("https://example.com/clip.mp4"), directory, null, null, default);
            Assert.That(Path.GetFileName(downloaded), Is.EqualTo("clip (2).mp4"));
            Assert.That(File.ReadAllBytes(downloaded), Has.Length.EqualTo(200000));
            Assert.That(Directory.Exists(existingDirectory), Is.True);
            Assert.That(await File.ReadAllTextAsync(existingFile), Is.EqualTo("existing media"));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(2));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
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

    [TestCase("CON.mp4", "_CON.mp4")]
    [TestCase("prn.wav", "_prn.mp4")]
    [TestCase("AUX", "_AUX.mp4")]
    [TestCase("CONIN$.mp4", "_CONIN$.mp4")]
    [TestCase("conout$.backup.mp4", "_conout$.backup.mp4")]
    [TestCase("CONOUT$", "_CONOUT$.mp4")]
    [TestCase("nul.backup.mp4", "_nul.backup.mp4")]
    [TestCase("COM1.mp4", "_COM1.mp4")]
    [TestCase("COM9.mp4", "_COM9.mp4")]
    [TestCase("LPT1.mp4", "_LPT1.mp4")]
    [TestCase("LPT9.mp4", "_LPT9.mp4")]
    [TestCase("COM¹.mp4", "_COM¹.mp4")]
    [TestCase("LPT².mp4", "_LPT².mp4")]
    [TestCase("com³.mp4", "_com³.mp4")]
    [TestCase(" CON .mp4. ", "_CON .mp4")]
    [TestCase("CONCERT.mp4", "CONCERT.mp4")]
    [TestCase("COM0.mp4", "COM0.mp4")]
    [TestCase("LPT10.mp4", "LPT10.mp4")]
    [TestCase("report.CON.mp4", "report.CON.mp4")]
    [TestCase("CONINPUT.mp4", "CONINPUT.mp4")]
    public void DownloadNamesAvoidWindowsDevicesWithoutChangingOrdinaryNames(string name, string expected)
    {
        Assert.That(BrowserMediaDownload.CreateFileName(name, new Uri("https://example.com/media"), "video/mp4"), Is.EqualTo(expected));
    }

    [TestCase("japanese")]
    [TestCase("emoji")]
    [TestCase("combining")]
    [TestCase("ascii")]
    public void DownloadNamesFitTheEncodedComponentLimitWithoutSplittingUnicode(string kind)
    {
        string stem = kind switch
        {
            "japanese" => new string('界', 100),
            "emoji" => "a" + string.Concat(Enumerable.Repeat("😀", 100)),
            "combining" => string.Concat(Enumerable.Repeat("e\u0301", 150)),
            _ => new string('a', 300)
        };
        string name = BrowserMediaDownload.CreateFileName(stem + ".mp4", new Uri("https://example.com/media"), "video/mp4");
        Assert.That(new UTF8Encoding(false, true).GetByteCount(name), Is.LessThanOrEqualTo(240));
        Assert.That(Path.GetExtension(name), Is.EqualTo(".mp4"));
        Assert.That(stem.StartsWith(Path.GetFileNameWithoutExtension(name), StringComparison.Ordinal), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MultibyteNamesAndCollisionSuffixesStayWithinTheComponentLimit(bool useResponseHeader)
    {
        string name = new string('界', 100) + ".mp4";
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MediaHandler(fileName: useResponseHeader ? name : null));
        var downloader = new BrowserMediaDownload(client);
        var uri = new Uri("https://example.com/" + (useResponseHeader ? "media.mp4" : Uri.EscapeDataString(name)));
        try
        {
            string first = await downloader.DownloadAsync(uri, directory, null, null, default);
            string second = await downloader.DownloadAsync(uri, directory, null, null, default);
            Assert.That(first, Is.Not.EqualTo(second));
            foreach (string file in new[] { first, second })
            {
                Assert.That(Encoding.UTF8.GetByteCount(Path.GetFileName(file)), Is.LessThanOrEqualTo(255));
                Assert.That(Path.GetExtension(file), Is.EqualTo(".mp4"));
                Assert.That(File.ReadAllBytes(file), Has.Length.EqualTo(200000));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void TruncationCannotCreateAReservedWindowsName()
    {
        string name = BrowserMediaDownload.CreateFileName("CON" + new string(' ', 500) + "tail.mp4",
            new Uri("https://example.com/media"), "video/mp4");
        Assert.That(name, Is.EqualTo("_CON.mp4"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReservedNamesFromUrlsAndResponseHeadersPublishDistinctSafeFiles(bool useResponseHeader)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MediaHandler(fileName: useResponseHeader ? "CON.mp4" : null));
        var uri = new Uri(useResponseHeader ? "https://example.com/media.mp4" : "https://example.com/CON.mp4");
        var downloader = new BrowserMediaDownload(client);
        try
        {
            string first = await downloader.DownloadAsync(uri, directory, null, null, default);
            string second = await downloader.DownloadAsync(uri, directory, null, null, default);
            Assert.That(Path.GetFileName(first), Is.EqualTo("_CON.mp4"));
            Assert.That(Path.GetFileName(second), Is.EqualTo("_CON (1).mp4"));
            Assert.That(File.ReadAllBytes(first), Has.Length.EqualTo(200000));
            Assert.That(File.ReadAllBytes(second), Has.Length.EqualTo(200000));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(2));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
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

    private sealed class MediaHandler(string mediaType = "video/mp4", string? fileName = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(new byte[200000]);
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            if (fileName != null) content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
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
