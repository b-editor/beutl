using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentIcons.Avalonia.Fluent;
using static Beutl.HeadlessUITests.CloudStorageTests;
using ProgressRing = FluentAvalonia.UI.Controls.FAProgressRing;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageProgressTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task DownloadProgressReplacesOnlyTheTargetIcon(bool icons, bool drag)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(fileCount: 2, fileSize: 8));
        var vm = scope.ViewModel;
        vm.ViewMode.Value = icons ? FileBrowserViewMode.Icon : FileBrowserViewMode.List;
        var target = vm.Items[1];
        var context = vm.CaptureActionContext([target])!;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520, RequestedThemeVariant = icons ? ThemeVariant.Dark : ThemeVariant.Light };
        string directory = Path.Combine(Path.GetTempPath(), "beutl-progress-" + Guid.NewGuid().ToString("N"));
        using var destination = new MemoryStream();
        using var stream = new GatedStream();
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            var itemView = list.ContainerFromIndex(1)!.GetVisualDescendants().OfType<FileBrowserItemView>().Single();
            var originalSize = itemView.Bounds.Size;
            var operation = StartTransfer(vm, context, destination, directory, drag);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            HeadlessTestHelpers.Render();
            Assert.That(itemView.IsProcessing, Is.True);
            Assert.That(itemView.IsProgressIndeterminate, Is.True);
            Assert.That(vm.Items.Where(item => !ReferenceEquals(item, target)).All(item => !item.Activity.IsActive.Value), Is.True);
            Assert.That(view.StorageDialog, Is.Null);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentLength = 8;
            scope.Handler.Requests[1].Completion.SetResult(response);
            await stream.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Render();
            var ring = itemView.GetVisualDescendants().OfType<ProgressRing>().Single(control => control.IsEffectivelyVisible);
            Assert.That(ring.IsIndeterminate, Is.False);
            Assert.That(ring.Value, Is.EqualTo(50));
            Assert.That(ring.Bounds.Width, Is.EqualTo(icons ? 48 : 20));
            Assert.That(itemView.Bounds.Size, Is.EqualTo(originalSize));
            Assert.That(itemView.GetVisualDescendants().OfType<FluentIcon>().All(icon => !icon.IsEffectivelyVisible), Is.True);
            await Capture(window, $"progress-{icons}-{drag}");

            stream.Resume.TrySetResult();
            Assert.That(await operation, Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(itemView.IsProcessing, Is.False);
            Assert.That(itemView.GetVisualDescendants().OfType<ProgressRing>().All(control => !control.IsEffectivelyVisible), Is.True);
            Assert.That(itemView.GetVisualDescendants().OfType<FluentIcon>().Any(icon => icon.IsEffectivelyVisible), Is.True);
            Assert.That(itemView.Bounds.Size, Is.EqualTo(originalSize));
        }
        finally
        {
            stream.Resume.TrySetResult();
            window.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    [TestCase(false, "cancel")]
    [TestCase(true, "cancel")]
    [TestCase(false, "failure")]
    [TestCase(true, "failure")]
    [TestCase(false, "signout")]
    [TestCase(true, "signout")]
    public async Task InterruptedDownloadsRestoreTheItemIcon(bool drag, string outcome)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(fileSize: 8));
        var vm = scope.ViewModel;
        var target = vm.Items[1];
        using var destination = new MemoryStream();
        using var stream = new GatedStream { FailOnResume = outcome == "failure" };
        string directory = Path.Combine(Path.GetTempPath(), "beutl-progress-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            var operation = StartTransfer(vm, vm.CaptureActionContext([target])!, destination, directory, drag);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentLength = 8;
            scope.Handler.Requests[1].Completion.SetResult(response);
            await stream.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(target.Activity.Progress.Value, Is.EqualTo(50));
            if (outcome == "cancel") vm.CancelTransfer.Execute();
            if (outcome == "signout") SignIn(scope.Clients, null);
            stream.Resume.TrySetResult();
            Assert.That(await operation, Is.False);
            Assert.That(target.Activity.IsActive.Value, Is.False);
            Assert.That(vm.IsTransferring.Value, Is.False);
            Assert.That(Directory.Exists(directory), Is.False);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaTest]
    public async Task UnknownLengthStaysIndeterminateAndOverlappingWorkKeepsTheItemBusy()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(fileSize: 0));
        var vm = scope.ViewModel;
        var item = vm.Items[1];
        using var outer = item.Activity.Begin();
        using var destination = new MemoryStream();
        using var stream = new GatedStream();
        var operation = vm.DownloadAsync(vm.CaptureActionContext([item])!, destination, CancellationToken.None);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        await stream.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(item.Activity.IsActive.Value, Is.True);
        Assert.That(item.Activity.IsIndeterminate.Value, Is.True);
        stream.Resume.TrySetResult();
        Assert.That(await operation, Is.True);
        Assert.That(item.Activity.IsActive.Value, Is.True, "Completing one operation must not clear another operation's indicator.");
        outer.Dispose();
        Assert.That(item.Activity.IsActive.Value, Is.False);
    }

    private static async Task<bool> StartTransfer(CloudStorageViewModel vm, StorageActionContext context, Stream destination, string directory, bool drag)
        => drag ? await vm.ExportStorageItemsAsync(context, directory) != null : await vm.DownloadAsync(context, destination, CancellationToken.None);

    private static async Task Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_STORAGE_PROGRESS_CAPTURE") is not { Length: > 0 } directory) return;
        // Allow the progress ring's composition animation to reach the requested value.
        await Task.Delay(800);
        HeadlessTestHelpers.Render(5);
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class GatedStream : Stream
    {
        private int _read;
        public bool FailOnResume { get; init; }
        public TaskCompletionSource SecondReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read++ == 0) { buffer.Span[..4].Fill(1); return 4; }
            if (_read == 2)
            {
                SecondReadStarted.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
                if (FailOnResume) throw new IOException("Interrupted download");
                buffer.Span[..4].Fill(2);
                return 4;
            }
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
