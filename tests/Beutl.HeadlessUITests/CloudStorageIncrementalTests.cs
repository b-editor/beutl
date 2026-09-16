using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.CloudStorageTests;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageIncrementalTests
{
    [AvaloniaTest]
    public async Task AppendIsSingleFlightKeepsExistingItemsDeduplicatesAndRefreshStartsAtPageOne()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response(pageCount: 3));
        var first = vm.Items.ToArray();
        var append = vm.LoadMoreAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        await vm.LoadMoreAsync();
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
        Assert.That(vm.Items.ToArray(), Is.EqualTo(first));
        Assert.That(vm.IsLoadingMore.Value, Is.True);
        scope.Handler.Requests[1].Complete(Response(page: 2, pageCount: 3));
        await append;
        Assert.That(vm.Items, Has.Count.EqualTo(2), "Neither the folder nor overlapping files should be appended twice.");
        Assert.That(vm.Items[1], Is.SameAs(first[1]));
        append = vm.LoadMoreAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response(page: 3, pageCount: 3, fileId: "last"));
        await append;
        Assert.That(vm.Items, Has.Count.EqualTo(3));
        Assert.That(vm.HasMore.Value, Is.False);
        await vm.LoadMoreAsync();
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(3));

        var refresh = vm.LoadAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 4);
        Assert.That(scope.Handler.Requests[3].Uri.Query, Does.Contain("page=1"));
        scope.Handler.Requests[3].Complete(Response(fileId: "refreshed"));
        await refresh;
        Assert.That(vm.Items.Select(x => x.Id), Is.EqualTo(new[] { "folder & 日本", "refreshed" }));
    }

    [AvaloniaTest]
    public async Task AClampedLastPageStopsFurtherRequestsWithoutAppendingTheOldPage()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(pageCount: 2));
        var append = scope.ViewModel.LoadMoreAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete(Response(page: 1, pageCount: 1, fileId: "clamped"));
        await append;
        Assert.That(scope.ViewModel.Items.Select(x => x.Id), Does.Not.Contain("clamped"));
        Assert.That(scope.ViewModel.HasMore.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task FolderNavigationCancelsAppendAndDiscardsItsLateResponse()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response(pageCount: 2));
        var folder = vm.Items[0];
        var append = vm.LoadMoreAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        var navigation = vm.OpenFolderAsync(folder);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        Assert.That(scope.Handler.Requests[1].Token.IsCancellationRequested, Is.True);
        Assert.That(vm.Items, Is.Empty);
        Assert.That(vm.IsLoadingMore.Value, Is.False);
        Assert.That(scope.Handler.Requests[2].Uri.Query, Does.Contain("page=1"));
        scope.Handler.Requests[2].Complete(Response(folder: folder.Id, fileId: "current"));
        await navigation;
        await WaitFor(() => !vm.IsLoading.Value);
        scope.Handler.Requests[1].Complete(Response(page: 2, pageCount: 2, fileId: "stale"));
        await append;
        Assert.That(vm.Items.Select(x => x.Id), Is.EqualTo(new[] { "current" }));
        Assert.That(vm.LoadMoreError.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task SigningOutDuringAppendClearsListingAndLoadingState()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(pageCount: 2));
        var append = scope.ViewModel.LoadMoreAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        SignIn(scope.Clients, null);
        Assert.That(scope.Handler.Requests[1].Token.IsCancellationRequested, Is.True);
        Assert.That(scope.ViewModel.Items, Is.Empty);
        Assert.That(scope.ViewModel.IsLoadingMore.Value, Is.False);
        Assert.That(scope.ViewModel.HasMore.Value, Is.False);
        scope.Handler.Requests[1].Complete(Response(page: 2, pageCount: 2));
        await append;
        Assert.That(scope.ViewModel.Items, Is.Empty);
    }

    [AvaloniaTest]
    public async Task AccountChangedBeforeUiNotificationCannotSendTheOldListingWithTheNewAccount()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(pageCount: 2));
        SignInOnBackgroundThread(scope.Clients, "b");
        var append = scope.ViewModel.LoadMoreAsync();
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(1));
        await append;
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        Assert.That(scope.Handler.Requests[1].Authorization, Is.EqualTo("Bearer token-b"));
        Assert.That(scope.Handler.Requests[1].Uri.Query, Does.Contain("page=1"));
        scope.Handler.Requests[1].Complete(Response(fileId: "account-b"));
        await WaitFor(() => !scope.ViewModel.IsLoading.Value);
        Assert.That(scope.ViewModel.Items.Select(x => x.Id), Does.Contain("account-b"));
    }

    [AvaloniaTest]
    public async Task DeletedFolderRestartsAtRootInsteadOfAppendingRootFilesToTheOldFolder()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var navigate = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本", pageCount: 2));
        await navigate;
        var append = vm.LoadMoreAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response(folder: null, fileId: "wrong-location"));
        await WaitFor(() => scope.Handler.Requests.Count == 4);
        Assert.That(vm.Items, Is.Empty);
        Assert.That(scope.Handler.Requests[3].Uri.Query, Does.Not.Contain("folder="));
        scope.Handler.Requests[3].Complete(Response(fileId: "root-reloaded"));
        await append;
        Assert.That(vm.Items.Select(x => x.Id), Does.Contain("root-reloaded").And.Not.Contain("wrong-location"));
        Assert.That(vm.Breadcrumbs, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    [TestCase(FileBrowserViewMode.Icon)]
    [TestCase(FileBrowserViewMode.List)]
    public async Task ScrollingAppendsOnlyNearTheEndAndKeepsScrollPosition(FileBrowserViewMode mode)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        vm.ViewMode.Value = mode;
        await scope.LoadFirstAsync(Response(pageCount: 3, fileCount: 24));
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 320 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(1), "Opening a filled viewport must not drain all pages.");
            var scroll = view.FindControl<ListBox>("StorageItems")!.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height));
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
            HeadlessTestHelpers.Render();
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            double position = scroll.Offset.Y;
            Assert.That(vm.Items, Has.Count.EqualTo(25));
            scope.Handler.Requests[1].Complete(Response(page: 2, pageCount: 3, fileCount: 24));
            await WaitFor(() => !vm.IsLoadingMore.Value);
            HeadlessTestHelpers.Render();
            Assert.That(vm.Items, Has.Count.EqualTo(49));
            Assert.That(scroll.Offset.Y, Is.EqualTo(position).Within(1));
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
            HeadlessTestHelpers.Render();
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            scope.Handler.Requests[2].Complete(Response(page: 3, pageCount: 3, fileCount: 24));
            await WaitFor(() => !vm.IsLoadingMore.Value);
            HeadlessTestHelpers.Render();
            Assert.That(vm.Items, Has.Count.EqualTo(73));
            Assert.That(vm.Items.Select(x => x.Id).Distinct().Count(), Is.EqualTo(73));
            Assert.That(vm.HasMore.Value, Is.False);
            Assert.That(view.FindControl<Button>("PreviousButton"), Is.Null);
            Assert.That(view.FindControl<Button>("NextButton"), Is.Null);
            Assert.That(view.GetVisualDescendants().OfType<FileBrowserItemView>().Any(), Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task UnderfilledViewportLoadsMoreButFailureWaitsForExplicitRetry()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response(pageCount: 2));
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete("{}", HttpStatusCode.InternalServerError);
            await WaitFor(() => !vm.IsLoadingMore.Value);
            HeadlessTestHelpers.Render();
            await Task.Delay(50);
            Assert.That(vm.Items, Has.Count.EqualTo(2));
            Assert.That(vm.LoadMoreError.Value, Is.Not.Null);
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
            await vm.LoadMoreAsync();
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
            vm.RetryLoadMore.Execute();
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            Assert.That(scope.Handler.Requests[2].Uri.Query, Does.Contain("page=2"));
            scope.Handler.Requests[2].Complete(Response(page: 2, pageCount: 2, fileId: "retried"));
            await WaitFor(() => !vm.IsLoadingMore.Value);
            HeadlessTestHelpers.Render();
            Assert.That(vm.LoadMoreError.Value, Is.Null);
            Assert.That(vm.Items, Has.Count.EqualTo(3));
            Assert.That(vm.HasMore.Value, Is.False);
        }
        finally { window.Close(); }
    }

    private sealed class StorageScope : IAsyncDisposable
    {
        public Handler Handler { get; } = new();
        private readonly HttpClient _http;
        public BeutlApiApplication Clients { get; }
        public CloudStorageViewModel ViewModel { get; }

        public StorageScope()
        {
            _http = new HttpClient(Handler);
            Clients = new BeutlApiApplication(_http, new ExtensionProvider());
            SignIn(Clients, "a");
            ViewModel = new(Clients, () => throw new InvalidOperationException("Not used by this test"));
        }

        public async Task LoadFirstAsync(string response)
        {
            await WaitFor(() => Handler.Requests.Count == 1);
            Handler.Requests[0].Complete(response);
            await WaitFor(() => !ViewModel.IsLoading.Value);
        }

        public async ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            foreach (var request in Handler.Requests.Where(x => !x.Completion.Task.IsCompleted))
                request.Complete(Response(empty: true));
            await Clients.DisposeAsync();
            _http.Dispose();
            Handler.Dispose();
        }
    }
}
