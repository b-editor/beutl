using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using Moq;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageDownloadTests
{
    [AvaloniaTest]
    public async Task NonlocalDestinationIsRejectedBeforeOpeningOrDownloading()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var file = new Mock<IStorageFile>();
        file.SetupGet(x => x.Path).Returns(new Uri("content://provider/existing.mp4"));
        using var destination = new MemoryStream(Encoding.UTF8.GetBytes("existing content"));
        file.Setup(x => x.OpenWriteAsync()).ReturnsAsync(destination);
        var view = new CloudStorageView { DataContext = scope.ViewModel };
        var window = CreateWindow(view, file.Object);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var operation = view.ExecuteStorageActionAsync("download", scope.ViewModel.CaptureActionContext([scope.ViewModel.Items[1]])!);
            await WaitFor(() => operation.IsCompleted || scope.Handler.Requests.Count == 2);
            if (scope.Handler.Requests.Count == 2) scope.Handler.Requests[1].Complete("partial");
            await operation;

            file.Verify(x => x.OpenWriteAsync(), Times.Never);
            Assert.That(Encoding.UTF8.GetString(destination.ToArray()), Is.EqualTo("existing content"));
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(1));
            Assert.That(scope.ViewModel.ActionError.Value, Is.Not.Null.And.Not.Empty);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("success")]
    [TestCase("cancel")]
    [TestCase("failure")]
    [TestCase("detach")]
    [TestCase("truncated")]
    [TestCase("header-short")]
    [TestCase("header-long")]
    public async Task LocalDestinationIsReplacedOnlyAfterASuccessfulDownload(string outcome)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(fileSize: Encoding.UTF8.GetByteCount("new content")));
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-storage-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "existing.mp4");
        await File.WriteAllTextAsync(path, "existing content");
        var file = new Mock<IStorageFile>();
        file.SetupGet(x => x.Path).Returns(new Uri(path));
        var view = new CloudStorageView { DataContext = scope.ViewModel };
        var window = CreateWindow(view, file.Object);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var operation = view.ExecuteStorageActionAsync("download", scope.ViewModel.CaptureActionContext([scope.ViewModel.Items[1]])!);
            await WaitFor(() => scope.Handler.Requests.Count == 2 && scope.ViewModel.Items[1].Activity.IsActive.Value);
            Assert.That(view.StorageDialog, Is.Null, "Download progress belongs on the file icon.");
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("existing content"));
            if (outcome == "cancel")
            {
                view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await WaitFor(() => scope.Handler.Requests[1].Token.IsCancellationRequested);
            }
            if (outcome == "detach")
            {
                window.Close();
                await WaitFor(() => scope.Handler.Requests[1].Token.IsCancellationRequested);
            }
            if (outcome is "header-short" or "header-long")
            {
                scope.Handler.Requests[1].Complete(outcome == "header-short" ? "short" : "longer than expected");
            }
            else if (outcome == "truncated")
            {
                scope.Handler.Requests[1].Completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new HeaderlessStream([1, 2, 3])) });
            }
            else if (outcome == "failure")
            {
                var content = new StreamContent(new FailingReadStream());
                content.Headers.ContentLength = Encoding.UTF8.GetByteCount("new content");
                scope.Handler.Requests[1].Completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
            }
            else scope.Handler.Requests[1].Complete("new content");
            await operation;
            Assert.That(scope.ViewModel.Items[1].Activity.IsActive.Value, Is.False);

            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(outcome == "success" ? "new content" : "existing content"));
            Assert.That(Directory.GetFiles(directory), Is.EqualTo(new[] { path }), "Temporary downloads must be cleaned up.");
            file.Verify(x => x.OpenWriteAsync(), Times.Never);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaTest]
    [TestCase(false, 4, false)]
    [TestCase(false, 8, false)]
    [TestCase(false, 12, false)]
    [TestCase(true, 4, false)]
    [TestCase(true, 8, false)]
    [TestCase(true, 12, false)]
    [TestCase(false, 4, true)]
    [TestCase(false, 8, true)]
    [TestCase(false, 12, true)]
    [TestCase(true, 4, true)]
    [TestCase(true, 8, true)]
    [TestCase(true, 12, true)]
    public async Task ExportsValidateMetadataWithOrWithoutAHeaderIncludingFolderChildren(bool folder, int bytes, bool withHeader)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(fileSize: 8));
        var vm = scope.ViewModel;
        var item = vm.Items[folder ? 0 : 1];
        var reported = new List<double>();
        using var subscription = item.Activity.Progress.Subscribe(reported.Add);
        string directory = Path.Combine(Path.GetTempPath(), "beutl-eof-" + Guid.NewGuid().ToString("N"));
        try
        {
            var export = vm.ExportStorageItemsAsync(vm.CaptureActionContext([item])!, directory);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            int contentIndex = 1;
            if (folder)
            {
                scope.Handler.Requests[1].Complete(Response(folder: item.Id, fileSize: 8));
                await WaitFor(() => scope.Handler.Requests.Count == 3);
                contentIndex = 2;
            }
            using var stream = new HeaderlessStream(new byte[bytes]);
            var content = new StreamContent(stream);
            Assert.That(content.Headers.ContentLength, Is.Null);
            if (withHeader) content.Headers.ContentLength = bytes;
            scope.Handler.Requests[contentIndex].Completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            var result = await export;
            if (bytes == 8)
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(new FileInfo(Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Single()).Length, Is.EqualTo(8));
            }
            else
            {
                Assert.That(result, Is.Null);
                Assert.That(Directory.Exists(directory), Is.False);
                Assert.That(reported, Does.Not.Contain(100));
                Assert.That(vm.ActionError.Value, Is.Not.Null);
                if (withHeader) Assert.That(stream.ReadCalls, Is.Zero, "Contradictory metadata must be rejected before reading content.");
            }
            Assert.That(item.Activity.IsActive.Value, Is.False);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class HeaderlessStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int ReadCalls { get; private set; }
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private static Window CreateWindow(CloudStorageView view, IStorageFile file)
    {
        var provider = new Mock<IStorageProvider>(MockBehavior.Strict);
        provider.Setup(x => x.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>())).ReturnsAsync(file);
        var window = new Window { Content = view, Width = 640, Height = 520 };
        TestStorageProviderFactory.SetProvider(window, provider.Object);
        return window;
    }

    private sealed class FailingReadStream : MemoryStream
    {
        private bool _read;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) throw new IOException("Download interrupted after partial content.");
            _read = true;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
            throw new IOException("Download interrupted after partial content.");
        }
    }
}
