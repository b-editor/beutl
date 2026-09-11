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

    [TestCase("../../clip.mp4", "video/mp4", "clip.mp4")]
    [TestCase("..\\clip.mp4", "video/mp4", "clip.mp4")]
    [TestCase("program.exe", "video/mp4", "program.mp4")]
    [TestCase("image", "image/png", "image.png")]
    public void FileName_IsConfinedToDestinationAndUsesMediaExtension(string name, string type, string expected)
    {
        Assert.That(BrowserMediaDownload.CreateFileName(name, new Uri("https://example.com/download"), type), Is.EqualTo(expected));
    }

    [Test]
    public void HtmlResponse_IsNotSavedAsMedia()
    {
        Assert.Throws<InvalidOperationException>(() => BrowserMediaDownload.CreateFileName("clip.mp4",
            new Uri("https://example.com/clip.mp4"), "text/html"));
    }

    [Test]
    public void ProjectDestinationAndImport_UseOwningProjectAndCurrentPlayhead()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var project = new Project { Uri = new Uri(Path.Combine(directory, "project.bep")) };
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "scene.scene")) };
        project.Items.Add(scene);
        var adder = new Mock<IElementAdder>();
        var clock = new Mock<IEditorClock>();
        using var time = new ReactivePropertySlim<TimeSpan>(TimeSpan.FromSeconds(12));
        clock.SetupGet(x => x.CurrentTime).Returns(time);
        var context = new Mock<IEditorContext>();
        context.SetupGet(x => x.Object).Returns(scene);
        context.Setup(x => x.GetService(typeof(IElementAdder))).Returns(adder.Object);
        context.Setup(x => x.GetService(typeof(IEditorClock))).Returns(clock.Object);
        using var vm = new WebBrowserTabViewModel(context.Object);
        Assert.That(vm.ProjectDownloadDirectory, Is.EqualTo(Path.Combine(directory, "resources", "downloads")));
        vm.AddDownloadedMedia("clip.mp4");
        adder.Verify(x => x.AddElement(It.Is<ElementDescription>(d => d.FileName == "clip.mp4"
            && d.Start == TimeSpan.FromSeconds(12) && d.Layer == 0)), Times.Once);
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<(long Received, long? Total)>
    {
        public void Report((long Received, long? Total) value) => cancellation.Cancel();
    }

    private sealed class MediaHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(new byte[200000]);
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
