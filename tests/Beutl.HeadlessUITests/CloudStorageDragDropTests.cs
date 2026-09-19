using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using Moq;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageDragDropTests
{
    private static IStorageFile FileSource(string name, string content)
    {
        var file = new Mock<IStorageFile>();
        file.SetupGet(x => x.Name).Returns(name);
        file.SetupGet(x => x.Path).Returns(new Uri("file:///source/" + Uri.EscapeDataString(name)));
        file.Setup(x => x.OpenReadAsync()).ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));
        return file.Object;
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task StorageDropsRefreshTheSourceOnceAndRefreshADifferentTargetBrowser(bool differentBrowser)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var source = scope.ViewModel;
        using var second = differentBrowser ? new Beutl.ViewModels.Tools.CloudStorageViewModel(scope.Clients, () => throw new InvalidOperationException()) : null;
        var target = second ?? source;
        if (second != null)
        {
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete(Response());
            await WaitFor(() => !second.IsLoading.Value);
        }
        int before = scope.Handler.Requests.Count;
        var context = source.CaptureActionContext([source.Items[1]])!;
        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(StorageDragData.Format, new StorageDragData("beutl", context.User,
            [new("file", "video.mp4", false)], [], () => source.IsActionCurrent(context), destination => source.MoveDroppedEntriesAsync(context, destination), source)));
        var drop = target.DropAsync(data, "folder & 日本");
        await WaitFor(() => scope.Handler.Requests.Count == before + 1);
        scope.Handler.Requests[before].Complete("{\"affected\":1}");
        await WaitFor(() => scope.Handler.Requests.Count == before + 2);
        scope.Handler.Requests[before + 1].Complete(Response());
        await WaitFor(() => drop.IsCompleted || scope.Handler.Requests.Count == before + 3);
        if (scope.Handler.Requests.Count == before + 3) scope.Handler.Requests[before + 2].Complete(differentBrowser ? Response() : "{}", differentBrowser ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        await drop;
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(before + (differentBrowser ? 3 : 2)));
        Assert.That(target.Error.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task DroppingAFolderUploadsItsHierarchyIntoTheHoveredFolder()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var folder = new Mock<IStorageFolder>();
        folder.SetupGet(x => x.Name).Returns("Clips");
        folder.SetupGet(x => x.Path).Returns(new Uri("content://provider/clips"));
        folder.Setup(x => x.GetItemsAsync()).Returns(Children(FileSource("nested.bin", "abc")));
        var view = new CloudStorageView { DataContext = scope.ViewModel };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            var point = list.ContainerFromIndex(0)!.TranslatePoint(new Point(20, 20), view)!.Value;
            using var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(folder.Object));
            var over = new DragEventArgs(DragDrop.DragOverEvent, data, view, point, KeyModifiers.None);
            view.RaiseEvent(over);
            Assert.That(over.DragEffects, Is.EqualTo(DragDropEffects.Copy));
            view.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, view, point, KeyModifiers.None));
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            Assert.That(scope.ViewModel.Items[0].Activity.IsActive.Value, Is.True);
            Assert.That(scope.ViewModel.Items[1].Activity.IsActive.Value, Is.False);
            using var creation = JsonDocument.Parse(await scope.Handler.Requests[1].ReadBodyAsync());
            Assert.That(creation.RootElement.GetProperty("parentId").GetString(), Is.EqualTo("folder & 日本"));
            scope.Handler.Requests[1].Complete("{\"id\":\"created-folder\"}", HttpStatusCode.Created);
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            using var start = JsonDocument.Parse(await scope.Handler.Requests[2].ReadBodyAsync());
            string id = start.RootElement.GetProperty("id").GetString()!;
            scope.Handler.Requests[2].Complete(JsonSerializer.Serialize(new { id, partSize = 4, partCount = 1 }));
            await WaitFor(() => scope.Handler.Requests.Count == 4);
            scope.Handler.Requests[3].Complete("{\"partNumber\":1,\"etag\":\"etag\"}");
            await WaitFor(() => scope.Handler.Requests.Count == 5);
            scope.Handler.Requests[4].Complete("{\"id\":\"new-file\"}");
            await WaitFor(() => scope.Handler.Requests.Count == 6);
            using var moved = JsonDocument.Parse(await scope.Handler.Requests[5].ReadBodyAsync());
            Assert.That(moved.RootElement.GetProperty("parentId").GetString(), Is.EqualTo("created-folder"));
            scope.Handler.Requests[5].Complete("", HttpStatusCode.NoContent);
            await WaitFor(() => scope.Handler.Requests.Count == 7);
            scope.Handler.Requests[6].Complete(Response());
            await WaitFor(() => !scope.ViewModel.IsLoading.Value);
            Assert.That(scope.ViewModel.ActionError.Value, Is.Null);
        }
        finally { window.Close(); }
    }

    private static async IAsyncEnumerable<IStorageItem> Children(params IStorageItem[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    [AvaloniaTest]
    public async Task ChangingAccountsCancelsUploadWithoutWritingForTheNextAccount()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var context = vm.CaptureActionContext([])!;
        var upload = vm.UploadStorageItemsAsync(context, [FileSource("clip.bin", "abc")], null);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        using var start = JsonDocument.Parse(await scope.Handler.Requests[1].ReadBodyAsync());
        string id = start.RootElement.GetProperty("id").GetString()!;
        scope.Handler.Requests[1].Complete(JsonSerializer.Serialize(new { id, partSize = 4, partCount = 1 }));
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        SignIn(scope.Clients, "b");
        await WaitFor(() => scope.Handler.Requests.Count == 4);
        scope.Handler.Requests[2].Complete("{\"partNumber\":1,\"etag\":\"etag\"}");
        Assert.That(await upload, Is.False);
        scope.Handler.Requests[3].Complete(Response(fileId: "account-b"));
        await WaitFor(() => !vm.IsLoading.Value);
        Assert.That(scope.Handler.Requests.Where(x => x.Authorization == "Bearer token-b").All(x => x.Method == HttpMethod.Get), Is.True);
        using var drag = new DataTransfer();
        drag.Add(DataTransferItem.Create(StorageDragData.Format, new StorageDragData("beutl", context.User, [], [], () => true, _ => throw new InvalidOperationException())));
        Assert.That(vm.CanDrop(drag, null), Is.False);
    }

    [AvaloniaTest]
    public async Task UploadStreamsPartsThenMovesTheCompletedFileToTheDropFolder()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var upload = vm.UploadStorageItemsAsync(vm.CaptureActionContext([])!, [FileSource("clip.bin", "abcdefghij")], "folder & 日本");
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        using var start = JsonDocument.Parse(await scope.Handler.Requests[1].ReadBodyAsync());
        string id = start.RootElement.GetProperty("id").GetString()!;
        Assert.That(start.RootElement.GetProperty("size").GetInt64(), Is.EqualTo(10));
        scope.Handler.Requests[1].Complete(JsonSerializer.Serialize(new { id, partSize = 4, partCount = 3 }), HttpStatusCode.Created);
        for (int part = 1; part <= 3; part++)
        {
            await WaitFor(() => scope.Handler.Requests.Count == part + 2);
            var request = scope.Handler.Requests[part + 1];
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo($"/api/v3/storage/uploads/{id}/parts/{part}"));
            Assert.That(await request.ReadBodyAsync(), Is.EqualTo(part == 1 ? "abcd" : part == 2 ? "efgh" : "ij"));
            request.Complete(JsonSerializer.Serialize(new { partNumber = part, etag = $"etag-{part}" }));
        }
        await WaitFor(() => scope.Handler.Requests.Count == 6);
        using var completion = JsonDocument.Parse(await scope.Handler.Requests[5].ReadBodyAsync());
        Assert.That(completion.RootElement.GetProperty("parts").GetArrayLength(), Is.EqualTo(3));
        scope.Handler.Requests[5].Complete("{\"id\":\"uploaded\"}");
        await WaitFor(() => scope.Handler.Requests.Count == 7);
        Assert.That(scope.Handler.Requests[6].Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/files/uploaded"));
        Assert.That(await scope.Handler.Requests[6].ReadBodyAsync(), Does.Contain("parentId"));
        scope.Handler.Requests[6].Complete("", HttpStatusCode.NoContent);
        await WaitFor(() => scope.Handler.Requests.Count == 8);
        scope.Handler.Requests[7].Complete(Response());
        Assert.That(await upload, Is.True);
        Assert.That(vm.IsTransferring.Value, Is.False);
        Assert.That(vm.TransferProgress.Value, Is.EqualTo(100));
        Assert.That(scope.Handler.Requests.All(x => x.Authorization == "Bearer token-a"), Is.True);
    }

    [AvaloniaTest]
    public async Task CancellingAnUploadAbortsTheOwnedReservationAndDoesNotCompleteIt()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var upload = vm.UploadStorageItemsAsync(vm.CaptureActionContext([])!, [FileSource("clip.bin", "abc")], null);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        using var start = JsonDocument.Parse(await scope.Handler.Requests[1].ReadBodyAsync());
        string id = start.RootElement.GetProperty("id").GetString()!;
        scope.Handler.Requests[1].Complete(JsonSerializer.Serialize(new { id, partSize = 4, partCount = 1 }));
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        vm.CancelTransfer.Execute();
        scope.Handler.Requests[2].Complete("{\"partNumber\":1,\"etag\":\"etag\"}");
        await WaitFor(() => scope.Handler.Requests.Count == 4);
        Assert.That(scope.Handler.Requests[3].Method, Is.EqualTo(HttpMethod.Delete));
        scope.Handler.Requests[3].Complete("", HttpStatusCode.NoContent);
        await WaitFor(() => scope.Handler.Requests.Count == 5);
        scope.Handler.Requests[4].Complete(Response());
        Assert.That(await upload, Is.False);
        Assert.That(scope.Handler.Requests.Any(x => x.Uri.AbsolutePath.EndsWith("/complete")), Is.False);
        Assert.That(vm.IsBusy.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task PointerDragWithinStorageMovesWithoutDownloadingTheFile()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var view = new CloudStorageView { DataContext = scope.ViewModel };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            var file = list.ContainerFromIndex(1)!.TranslatePoint(new Point(20, 20), window)!.Value;
            var folder = list.ContainerFromIndex(0)!.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseDown(file, MouseButton.Left);
            window.MouseMove(folder, RawInputModifiers.LeftMouseButton);
            window.MouseUp(folder, MouseButton.Left);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            Assert.That(scope.Handler.Requests[1].Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/entries/move"));
            using var moveBody = JsonDocument.Parse(await scope.Handler.Requests[1].ReadBodyAsync());
            Assert.That(moveBody.RootElement.GetProperty("parentId").GetString(), Is.EqualTo("folder & 日本"));
            scope.Handler.Requests[1].Complete("{\"affected\":1}");
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            scope.Handler.Requests[2].Complete(Response(empty: true));
            await WaitFor(() => !scope.ViewModel.IsLoading.Value);
            Assert.That(scope.Handler.Requests.Any(x => x.Uri.AbsolutePath.EndsWith("/content")), Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task ExportedFilesAreCompleteAndSceneImportsSurviveRemovalOfTheDragDownload()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(name: "../clip?.bin"));
        var vm = scope.ViewModel;
        var context = vm.CaptureActionContext([vm.Items[1]])!;
        string root = Path.Combine(Path.GetTempPath(), "beutl-storage-drop-" + Guid.NewGuid().ToString("N"));
        string downloads = Path.Combine(root, "downloads");
        try
        {
            var export = vm.ExportStorageItemsAsync(context, downloads);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete("content");
            var paths = (await export)!;
            Assert.That(paths, Has.Length.EqualTo(1));
            Assert.That(Path.GetDirectoryName(paths[0]), Is.EqualTo(downloads));
            Assert.That(await File.ReadAllTextAsync(paths[0]), Is.EqualTo("content"));
            var scene = new Scene { Uri = new Uri(Path.Combine(root, "scene.beutl")) };
            var data = new StorageDragData("beutl", context.User, [new("file", "clip.bin", false)], paths, () => vm.IsActionCurrent(context), _ => Task.FromResult(false));
            using var imported = await data.ImportToSceneAsync(scene);
            imported.Retain(imported.Paths.Single());
            Directory.Delete(downloads, true);
            Assert.That(await File.ReadAllTextAsync(imported.Paths.Single()), Is.EqualTo("content"));
            Assert.That(imported.Paths.Single(), Does.Contain(Path.Combine("resources", "storage")));
            SignIn(scope.Clients, null);
            using var staleImport = await data.ImportToSceneAsync(scene);
            Assert.That(staleImport.Paths, Is.Empty);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task AcceptedFolderResourcesSurviveWhileRejectedSelectionsAreRemoved()
    {
        string root = Path.Combine(Path.GetTempPath(), "beutl-storage-import-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "downloads", "folder");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "accepted.png"), "image");
            File.WriteAllText(Path.Combine(folder, "dependency.bin"), "dependency");
            string rejected = Path.Combine(root, "downloads", "rejected.bin");
            File.WriteAllText(rejected, "rejected");
            var scene = new Scene { Uri = new Uri(Path.Combine(root, "scene.beutl")) };
            var data = new StorageDragData("test", new object(), [], [folder, rejected], () => true, _ => Task.FromResult(false));
            string accepted;
            string rejectedCopy;
            using (var import = await data.ImportToSceneAsync(scene))
            {
                accepted = import.Paths.Single(path => Path.GetFileName(path) == "accepted.png");
                rejectedCopy = import.Paths.Single(path => Path.GetFileName(path) == "rejected.bin");
                import.Retain(accepted);
            }
            Assert.That(File.Exists(accepted), Is.True);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(accepted)!, "dependency.bin")), Is.True);
            Assert.That(File.Exists(rejectedCopy), Is.False);
            Assert.That(File.Exists(rejected), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task FolderExportsReadEveryPageAndKeepTheFolderStructure()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        string root = Path.Combine(Path.GetTempPath(), "beutl-storage-tree-" + Guid.NewGuid().ToString("N"));
        try
        {
            var export = vm.ExportStorageItemsAsync(vm.CaptureActionContext([vm.Items[0]])!, root);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本", name: "one.bin", fileId: "one", pageCount: 2));
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            scope.Handler.Requests[2].Complete("one");
            await WaitFor(() => scope.Handler.Requests.Count == 4);
            Assert.That(scope.Handler.Requests[3].Uri.Query, Does.Contain("cursor=cursor-2"));
            scope.Handler.Requests[3].Complete(Response(folder: "folder & 日本", name: "two.bin", fileId: "two", page: 2, pageCount: 2));
            await WaitFor(() => scope.Handler.Requests.Count == 5);
            scope.Handler.Requests[4].Complete("two");
            string directory = (await export)!.Single();
            Assert.That(Path.GetFileName(directory), Is.EqualTo("素材"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "one.bin")), Is.EqualTo("one"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "two.bin")), Is.EqualTo("two"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task CancelledExportRemovesIncompleteLocalFiles()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        string root = Path.Combine(Path.GetTempPath(), "beutl-storage-cancel-" + Guid.NewGuid().ToString("N"));
        var export = vm.ExportStorageItemsAsync(vm.CaptureActionContext([vm.Items[1]])!, root);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        vm.CancelTransfer.Execute();
        scope.Handler.Requests[1].Complete("cancelled");
        Assert.That(await export, Is.Null);
        Assert.That(Directory.Exists(root), Is.False);
    }

    [AvaloniaTest]
    public async Task ReturningAPreparingDragToItsSourceCancelsWithoutFooterControls()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(name: "clip.bin"));
        var vm = scope.ViewModel;
        var item = vm.Items[1];
        var view = new CloudStorageView { DataContext = vm };
        int nativeStarts = 0;
        view.DragStarter = (_, _) => { nativeStarts++; return Task.FromResult(DragDropEffects.Copy); };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        string directory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "storage", "downloads");
        string[] original = Directory.Exists(directory) ? Directory.GetDirectories(directory) : [];
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            var point = list.ContainerFromIndex(1)!.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(new Point(700, 40), RawInputModifiers.LeftMouseButton);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            Assert.That(item.Activity.IsActive.Value, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<Button>().Any(button => ReferenceEquals(button.Command, vm.CancelTransfer)), Is.False);
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("clip.bin", StringComparison.Ordinal) == true && text.Text != "clip.bin"), Is.False);
            window.MouseMove(point, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Assert.That(scope.Handler.Requests[1].Token.IsCancellationRequested, Is.True);
            scope.Handler.Requests[1].Complete("late response");
            await WaitFor(() => !vm.IsTransferring.Value && !item.Activity.IsActive.Value);
            Assert.That(nativeStarts, Is.Zero);
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
            Assert.That(Directory.GetDirectories(directory).Except(original), Is.Empty);
            Assert.That(vm.ActionError.Value, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReturningAReadyDragToItsOriginalFolderRejectsTheDropAndDeletesDownloads(bool nested)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(name: "clip.bin", folder: nested ? "parent" : null));
        var vm = scope.ViewModel;
        var view = new CloudStorageView { DataContext = vm };
        var provider = new Mock<IStorageProvider>();
        provider.Setup(x => x.TryGetFileFromPathAsync(It.IsAny<Uri>())).ReturnsAsync((Uri uri) =>
        {
            var file = new Mock<IStorageFile>();
            file.SetupGet(x => x.Path).Returns(uri);
            return file.Object;
        });
        var window = new Window { Content = view, Width = 640, Height = 520 };
        TestStorageProviderFactory.SetProvider(window, provider.Object);
        string? download = null;
        Point point = default;
        DragDropEffects overEffect = DragDropEffects.Copy, dropEffect = DragDropEffects.Copy;
        view.DragStarter = (_, data) =>
        {
            download = data.TryGetFile()!.TryGetLocalPath();
            var over = new DragEventArgs(DragDrop.DragOverEvent, data, view, point, KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
            view.RaiseEvent(over);
            overEffect = over.DragEffects;
            // Also reject a drop delivered after the last drag-over event.
            var drop = new DragEventArgs(DragDrop.DropEvent, data, view, point, KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
            view.RaiseEvent(drop);
            dropEffect = drop.DragEffects;
            return Task.FromResult(overEffect);
        };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            var container = list.ContainerFromIndex(nested ? 0 : 1)!;
            point = container.TranslatePoint(new Point(20, 20), view)!.Value;
            var press = container.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(new Point(700, 40), RawInputModifiers.LeftMouseButton);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete("exported");
            await WaitFor(() => download != null);
            Assert.That(overEffect, Is.EqualTo(DragDropEffects.None));
            Assert.That(dropEffect, Is.EqualTo(DragDropEffects.None));
            await WaitFor(() => !Directory.Exists(Path.GetDirectoryName(download!)!));
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
            Assert.That(vm.ActionError.Value, Is.Null);
        }
        finally
        {
            window.MouseUp(new Point(700, 40), MouseButton.Left);
            window.Close();
            if (download != null && Directory.Exists(Path.GetDirectoryName(download))) Directory.Delete(Path.GetDirectoryName(download)!, true);
        }
    }

    [AvaloniaTest]
    public async Task NativeDragContainsFilesAndAnInProcessPayloadAndRetainsSuccessfulDownloads()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(name: "clip.bin"));
        var provider = new Mock<IStorageProvider>();
        provider.Setup(x => x.TryGetFileFromPathAsync(It.IsAny<Uri>())).ReturnsAsync((Uri uri) =>
        {
            var file = new Mock<IStorageFile>();
            file.SetupGet(x => x.Path).Returns(uri);
            return file.Object;
        });
        string? retained = null;
        DataFormat[][]? formats = null;
        var view = new CloudStorageView { DataContext = scope.ViewModel };
        view.DragStarter = (_, data) =>
        {
            formats = data.Items.Select(item => item.Formats.ToArray()).ToArray();
            retained = data.TryGetFile()!.TryGetLocalPath();
            Assert.That(File.ReadAllText(retained!), Is.EqualTo("exported"));
            Assert.That(data.TryGetValue(StorageDragData.Format)?.IsCurrent(), Is.True);
            Assert.That(StorageDragData.Format.Kind, Is.EqualTo(DataFormatKind.InProcess));
            return Task.FromResult(DragDropEffects.Copy);
        };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        TestStorageProviderFactory.SetProvider(window, provider.Object);
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            var point = list.ContainerFromIndex(1)!.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(new Point(700, 40), RawInputModifiers.LeftMouseButton);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete("exported");
            await WaitFor(() => retained != null);
            Assert.That(File.Exists(retained), Is.True);
            Assert.That(formats, Has.Length.EqualTo(1));
            Assert.That(formats![0], Does.Contain(DataFormat.File));
            Assert.That(formats[0], Does.Contain(StorageDragData.Format));
        }
        finally
        {
            window.MouseUp(new Point(700, 40), MouseButton.Left);
            window.Close();
            if (retained != null) Directory.Delete(Path.GetDirectoryName(retained)!, true);
        }
    }
}
