using System.Net;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageFolderLoadingTests
{
    [AvaloniaTest]
    public async Task RevisitingFoldersRestoresTheirListingAndUsageSynchronously()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var navigate = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本", fileId: "child"));
        await navigate;
        navigate = vm.NavigateToAsync(vm.Breadcrumbs[0]);
        Assert.That(navigate.IsCompletedSuccessfully, Is.True);
        Assert.That(vm.Items.Select(x => x.Id), Is.EqualTo(new[] { "folder & 日本", "file" }));
        Assert.That(vm.HasUsage.Value, Is.True);
        Assert.That(vm.IsLoading.Value, Is.False);
        navigate = vm.OpenFolderAsync(vm.Items[0]);
        Assert.That(navigate.IsCompletedSuccessfully, Is.True);
        Assert.That(vm.Items.Single().Id, Is.EqualTo("child"));
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
        var refresh = vm.LoadAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response(folder: "folder & 日本", fileId: "updated"));
        await refresh;
        Assert.That(vm.Items.Single().Id, Is.EqualTo("updated"), "Explicit refresh must bypass the folder cache.");
    }

    [AvaloniaTest]
    public async Task ReturningToCachedParentClearsAFailedChildNavigationError()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var navigate = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete("{}", HttpStatusCode.InternalServerError);
        await navigate;
        Assert.That(vm.Error.Value, Is.Not.Null);
        navigate = vm.NavigateToAsync(vm.Breadcrumbs[0]);
        Assert.That(navigate.IsCompletedSuccessfully, Is.True);
        Assert.That(vm.Error.Value, Is.Null);
        Assert.That(vm.Items.Last().Id, Is.EqualTo("file"));
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task CacheExpiresEvenWhenTheFolderWasRecentlyRevisited()
    {
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        await using var scope = new StorageScope(() => now);
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        now = now.AddSeconds(5);
        var navigate = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本"));
        await navigate;
        now = now.AddSeconds(15);
        await vm.NavigateToAsync(vm.Breadcrumbs[0]);
        await vm.OpenFolderAsync(vm.Items[0]);
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
        now = now.AddSeconds(11);
        navigate = vm.NavigateToAsync(vm.Breadcrumbs[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        Assert.That(vm.Items, Is.Empty);
        scope.Handler.Requests[2].Complete(Response(fileId: "fresh"));
        await navigate;
        Assert.That(vm.Items.Last().Id, Is.EqualTo("fresh"));
    }

    [AvaloniaTest]
    public async Task CacheEvictsTheLeastRecentlyVisitedFolderAtItsLimit()
    {
        await using var scope = new StorageScope(() => DateTimeOffset.UnixEpoch);
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(FolderResponse(null));
        for (int i = 0; i < 7; i++) await VisitNewFolder(i);
        await vm.OpenFolderAsync(vm.Items.Single(x => x.Id == "folder-0"));
        await vm.NavigateToAsync(vm.Breadcrumbs[0]);
        await VisitNewFolder(7);
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(9));
        await vm.OpenFolderAsync(vm.Items.Single(x => x.Id == "folder-0"));
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(9), "The recently visited folder should remain cached.");
        await vm.NavigateToAsync(vm.Breadcrumbs[0]);
        await VisitNewFolder(1);
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(10), "The least recently used folder should be fetched again.");

        async Task VisitNewFolder(int index)
        {
            int count = scope.Handler.Requests.Count;
            string id = $"folder-{index}";
            var navigate = vm.OpenFolderAsync(vm.Items.Single(x => x.Id == id));
            await WaitFor(() => scope.Handler.Requests.Count == count + 1);
            scope.Handler.Requests[count].Complete(FolderResponse(id));
            await navigate;
            await vm.NavigateToAsync(vm.Breadcrumbs[0]);
        }

        static string FolderResponse(string? folder)
        {
            var json = JsonNode.Parse(Response(folder: folder))!;
            json["folders"] = new JsonArray(Enumerable.Range(0, 8).Select(i => (JsonNode)new JsonObject
            {
                ["id"] = $"folder-{i}",
                ["name"] = $"Folder {i}",
                ["parentId"] = null,
            }).ToArray());
            return json.ToJsonString();
        }
    }

    [AvaloniaTest]
    public async Task CompletedPrefetchMakesTheFirstVisitImmediate()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var folder = vm.Items[0];
        var prefetch = vm.PrefetchFolderAsync(folder);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        Assert.That(vm.IsLoading.Value, Is.False);
        Assert.That(vm.Items.Last().Id, Is.EqualTo("file"));
        scope.Handler.Requests[1].Complete(Response(folder: folder.Id, fileId: "prefetched"));
        await prefetch;
        var navigate = vm.OpenFolderAsync(folder);
        Assert.That(navigate.IsCompletedSuccessfully, Is.True);
        Assert.That(vm.Items.Single().Id, Is.EqualTo("prefetched"));
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task NavigationReusesPendingPrefetchAndAccountChangesCancelAndDiscardIt()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var folder = vm.Items[0];
        var prefetch = vm.PrefetchFolderAsync(folder);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        var navigate = vm.OpenFolderAsync(folder);
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
        SignIn(scope.Clients, null);
        Assert.That(scope.Handler.Requests[1].Token.IsCancellationRequested, Is.True);
        scope.Handler.Requests[1].Complete(Response(folder: folder.Id, fileId: "old-account"));
        await Task.WhenAll(prefetch, navigate);
        SignIn(scope.Clients, "b");
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response());
        await WaitFor(() => !vm.IsLoading.Value);
        navigate = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 4);
        Assert.That(scope.Handler.Requests[3].Authorization, Is.EqualTo("Bearer token-b"));
        scope.Handler.Requests[3].Complete(Response(folder: folder.Id, fileId: "new-account"));
        await navigate;
        Assert.That(vm.Items.Single().Id, Is.EqualTo("new-account"));
    }

    [AvaloniaTest]
    public async Task FailedPrefetchIsSilentAndNavigationRetriesIt()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var folder = vm.Items[0];
        var prefetch = vm.PrefetchFolderAsync(folder);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete("{}", HttpStatusCode.InternalServerError);
        await prefetch;
        Assert.That(vm.Error.Value, Is.Null);
        Assert.That(vm.Items.Last().Id, Is.EqualTo("file"));
        var navigate = vm.OpenFolderAsync(folder);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response(folder: folder.Id, fileId: "retried"));
        await navigate;
        Assert.That(vm.Items.Single().Id, Is.EqualTo("retried"));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task PointerOrKeyboardIntentPrefetchesTheFolderAndOpeningUsesThatRequest(bool pointer)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            list.SelectedIndex = 0;
            var container = list.ContainerFromIndex(0)!;
            if (pointer)
                window.MouseMove(container.TranslatePoint(new Point(20, 20), window)!.Value);
            else
            {
                window.MouseMove(new Point(310, 510));
                container.Focus();
            }
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            Assert.That(vm.IsLoading.Value, Is.False);
            if (pointer)
                _ = vm.OpenFolderAsync(vm.Items[0]);
            else
            {
                Assert.That(container.IsKeyboardFocusWithin, Is.True, "The folder must retain keyboard focus during prefetch.");
                Assert.That(list.SelectedIndex, Is.EqualTo(0));
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.That(vm.IsLoading.Value, Is.True, "Enter should promote the pending folder request.");
            }
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
            scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本", fileId: "child"));
            await WaitFor(() => !vm.IsLoading.Value && vm.Items.Count == 1);
            Assert.That(vm.Items.Single().Id, Is.EqualTo("child"));
        }
        finally { window.Close(); }
    }
}
