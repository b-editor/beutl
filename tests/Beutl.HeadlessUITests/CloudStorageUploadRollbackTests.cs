using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Avalonia.Platform.Storage;
using Beutl.Language;
using Moq;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageUploadRollbackTests
{
    [AvaloniaTest]
    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, true)]
    public async Task InterruptedFolderUploadsDeleteOnlyTheirOwnFilesAndEmptyFolders(bool cancel, bool cleanupConflict, bool closeBrowser)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var read = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool readingSecond = false;
        var first = FileSource("first.bin", () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("abc"))));
        var second = FileSource("second.bin", () => { readingSecond = true; return read.Task; });
        var tree = Folder("new-outer", Folder("new-inner", first, second));
        var upload = vm.UploadStorageItemsAsync(vm.CaptureActionContext([])!, [tree], "folder & 日本");
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete("{\"id\":\"outer-created\"}", HttpStatusCode.Created);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete("{\"id\":\"inner-created\"}", HttpStatusCode.Created);
        await WaitFor(() => scope.Handler.Requests.Count == 4);
        using var start = JsonDocument.Parse(await scope.Handler.Requests[3].ReadBodyAsync());
        string id = start.RootElement.GetProperty("id").GetString()!;
        scope.Handler.Requests[3].Complete(JsonSerializer.Serialize(new { id, partSize = 4, partCount = 1 }));
        await WaitFor(() => scope.Handler.Requests.Count == 5);
        scope.Handler.Requests[4].Complete("{\"partNumber\":1,\"etag\":\"etag\"}");
        await WaitFor(() => scope.Handler.Requests.Count == 6);
        scope.Handler.Requests[5].Complete("{\"id\":\"file-created\"}");
        await WaitFor(() => scope.Handler.Requests.Count == 7);
        scope.Handler.Requests[6].Complete("", HttpStatusCode.NoContent);
        await WaitFor(() => readingSecond);
        if (closeBrowser) vm.Dispose();
        else if (cancel) vm.CancelTransfer.Execute();
        read.SetResult(new MemoryStream());

        await WaitFor(() => scope.Handler.Requests.Count == 8);
        var deletion = scope.Handler.Requests[7];
        Assert.That(deletion.Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/files/batch"));
        using var body = JsonDocument.Parse(await deletion.ReadBodyAsync());
        Assert.That(body.RootElement.GetProperty("operation").GetString(), Is.EqualTo("delete"));
        Assert.That(body.RootElement.GetProperty("ids").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[] { "file-created" }));
        deletion.Complete("{\"affected\":1}");
        foreach (var (index, folder) in new[] { (8, "inner-created"), (9, "outer-created") })
        {
            await WaitFor(() => scope.Handler.Requests.Count == index + 1);
            var request = scope.Handler.Requests[index];
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Delete));
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo($"/api/v3/storage/folders/{folder}"));
            Assert.That(request.Uri.Query, Is.EqualTo("?recursive=false"));
            request.Complete(cleanupConflict ? "{\"error_code\":\"storageFolderNotEmpty\"}" : "{\"deletedFiles\":0,\"deletedFolders\":1}", cleanupConflict ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        }
        if (!closeBrowser)
        {
            await WaitFor(() => scope.Handler.Requests.Count == 11);
            scope.Handler.Requests[10].Complete(Response());
        }
        Assert.That(await upload, Is.False);
        Assert.That(scope.Handler.Requests.All(request => request.Authorization == "Bearer token-a"), Is.True);
        if (closeBrowser) return;
        if (cleanupConflict) Assert.That(vm.ActionError.Value, Does.Contain(Strings.CloudStorageUploadRollbackIncomplete));
        else if (cancel) Assert.That(vm.ActionError.Value, Is.Null);
        else Assert.That(vm.ActionError.Value, Is.EqualTo(Strings.CloudStorageEmptyUpload));
        Assert.That(vm.IsBusy.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task UnknownCreationOutcomesAreReportedWithoutDeletingTheDestination()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var upload = vm.UploadStorageItemsAsync(vm.CaptureActionContext([])!, [Folder("new")], "folder & 日本");
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete("{}", HttpStatusCode.ServiceUnavailable);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response());
        Assert.That(await upload, Is.False);
        Assert.That(vm.ActionError.Value, Does.Contain(Strings.CloudStorageUploadRollbackIncomplete));
        Assert.That(scope.Handler.Requests.Any(request => request.Method == HttpMethod.Delete), Is.False);
    }

    private static IStorageFile FileSource(string name, Func<Task<Stream>> read)
    {
        var file = new Mock<IStorageFile>();
        file.SetupGet(x => x.Name).Returns(name);
        file.SetupGet(x => x.Path).Returns(new Uri($"content://files/{name}"));
        file.Setup(x => x.OpenReadAsync()).Returns(read);
        return file.Object;
    }

    private static IStorageFolder Folder(string name, params IStorageItem[] children)
    {
        var folder = new Mock<IStorageFolder>();
        folder.SetupGet(x => x.Name).Returns(name);
        folder.SetupGet(x => x.Path).Returns(new Uri($"content://folders/{name}"));
        folder.Setup(x => x.GetItemsAsync()).Returns(Children(children));
        return folder.Object;
    }

    private static async IAsyncEnumerable<IStorageItem> Children(IStorageItem[] children)
    {
        foreach (var child in children) yield return child;
        await Task.CompletedTask;
    }
}
