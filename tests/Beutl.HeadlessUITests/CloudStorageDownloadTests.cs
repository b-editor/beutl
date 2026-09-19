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
    public async Task LocalDestinationIsReplacedOnlyAfterASuccessfulDownload(string outcome)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
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
            if (outcome == "failure")
            {
                scope.Handler.Requests[1].Completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new FailingReadStream()),
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
