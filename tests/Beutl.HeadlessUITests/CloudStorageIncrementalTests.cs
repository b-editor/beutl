using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
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
        Assert.That(vm.Breadcrumbs.Last().FolderId, Is.EqualTo(folder.Id), "Navigation should acknowledge the destination immediately.");
        Assert.That(vm.HasUsage.Value, Is.False);
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
    public async Task QueuedAccountChangesLoadOnlyTheLatestAccountOnce()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        SignInOnBackgroundThread(scope.Clients, "b");
        SignInOnBackgroundThread(scope.Clients, "c");
        // Both notifications are queued while the UI thread is still in this test.
        HeadlessTestHelpers.Settle();
        await WaitFor(() => scope.Handler.Requests.Count >= 2);
        await Task.Delay(50);
        HeadlessTestHelpers.Settle();
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
        Assert.That(scope.Handler.Requests[1].Authorization, Is.EqualTo("Bearer token-c"));
        scope.Handler.Requests[1].Complete(Response(fileId: "account-c"));
        await WaitFor(() => !scope.ViewModel.IsLoading.Value);
        Assert.That(scope.ViewModel.Items.Select(x => x.Id), Does.Contain("account-c"));
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
    public async Task ScrollingPrefetchesBeforeTheEndAndKeepsScrollPosition(FileBrowserViewMode mode)
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
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height * 1.75);
            HeadlessTestHelpers.Render();
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            double position = scroll.Offset.Y;
            Assert.That(vm.Items, Has.Count.EqualTo(25));
            Assert.That(scroll.Extent.Height - scroll.Viewport.Height - position,
                Is.GreaterThan(scroll.Viewport.Height * 0.5), "Fetch while there are still files ahead of the viewport.");
            await WaitFor(() => vm.IsLoadingMoreVisible.Value);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset.Y, Is.EqualTo(position).Within(1), "Loading feedback must not shift the viewport.");
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
    public async Task IconViewVirtualizesLargeListingsAcrossScrollingResizingAndModeChanges()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(fileCount: 1000));
        var vm = scope.ViewModel;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 320 };
        var list = view.FindControl<ListBox>("StorageItems")!;
        int prepared = 0;
        list.ContainerPrepared += (_, _) => prepared++;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(prepared, Is.LessThan(100), "Initial layout must not temporarily realize the whole listing.");
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            AssertVirtualized();
            list.SelectedIndex = 0;
            list.ContainerFromIndex(0)!.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            HeadlessTestHelpers.Render();
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            HeadlessTestHelpers.Render();
            Assert.That(list.SelectedIndex, Is.GreaterThan(2), "Down should advance by a tile row.");
            window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.End, RawInputModifiers.None);
            HeadlessTestHelpers.Render();
            Assert.That(list.SelectedIndex, Is.EqualTo(1000));
            Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
            AssertVirtualized();
            var last = list.ContainerFromIndex(1000)!;
            var lastPosition = last.TranslatePoint(default, scroll)!.Value;
            Assert.That(lastPosition.Y, Is.InRange(0, scroll.Viewport.Height));

            window.Width = 640;
            HeadlessTestHelpers.Render();
            AssertVirtualized();
            Assert.That(scroll.Offset.Y, Is.LessThanOrEqualTo(scroll.Extent.Height - scroll.Viewport.Height + 1));
            scroll.ScrollToHome();
            HeadlessTestHelpers.Render();
            AssertVirtualized();
            list.SelectedIndex = 1;
            vm.ViewMode.Value = FileBrowserViewMode.List;
            HeadlessTestHelpers.Render();
            AssertVirtualized();
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Assert.That(list.GetLogicalChildren().OfType<ListBoxItem>()
                .All(x => TopLevel.GetTopLevel(x) == window), Is.True, "Switching modes must release old tile containers.");
            vm.ViewMode.Value = FileBrowserViewMode.Icon;
            HeadlessTestHelpers.Render();
            AssertVirtualized();
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            vm.Items.Clear();
            HeadlessTestHelpers.Render();
            Assert.That(list.GetRealizedContainers(), Is.Empty);
            Assert.That(scroll.Offset.Y, Is.Zero);
            Assert.That(list.SelectedIndex, Is.EqualTo(-1));

            void AssertVirtualized()
            {
                Assert.That(list.GetRealizedContainers().Count(), Is.InRange(1, 60));
                Assert.That(list.GetVisualDescendants().OfType<FileBrowserItemView>().Count(), Is.InRange(1, 60));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(FileBrowserViewMode.Icon)]
    [TestCase(FileBrowserViewMode.List)]
    public async Task RefreshKeepsVisibleContentUsageAndScrollWhileWaiting(FileBrowserViewMode mode)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        vm.ViewMode.Value = mode;
        await scope.LoadFirstAsync(Response(fileCount: 24));
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 320 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            list.SelectedIndex = 1;
            scroll.Offset = new Vector(0, 50);
            HeadlessTestHelpers.Render();
            var items = vm.Items.ToArray();
            string usage = vm.UsageText.Value;
            var bounds = list.Bounds;
            double position = scroll.Offset.Y;
            var refresh = vm.LoadAsync();
            await WaitFor(() => scope.Handler.Requests.Count == 2 && vm.IsLoadingVisible.Value);
            HeadlessTestHelpers.Render();
            Assert.That(vm.Items.ToArray(), Is.EqualTo(items));
            Assert.That(vm.UsageText.Value, Is.EqualTo(usage));
            Assert.That(vm.ShowPlaceholders.Value, Is.False);
            Assert.That(list.SelectedItem, Is.SameAs(items[1]));
            Assert.That(list.Bounds, Is.EqualTo(bounds));
            Assert.That(scroll.Offset.Y, Is.EqualTo(position).Within(1));
            scope.Handler.Requests[1].Complete(Response(fileId: "refreshed"));
            await refresh;
            Assert.That(vm.Items.Select(x => x.Id), Is.EqualTo(new[] { "folder & 日本", "refreshed" }));
            Assert.That(vm.IsLoadingVisible.Value, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FailedRefreshKeepsTheLastListingAndDoesNotAutomaticallyLoadAnotherPage()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response(pageCount: 2, fileCount: 24));
        var items = vm.Items.ToArray();
        string usage = vm.UsageText.Value;
        var refresh = vm.LoadAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            scope.Handler.Requests[1].Complete("{}", HttpStatusCode.InternalServerError);
            await refresh;
            HeadlessTestHelpers.Render();
            await vm.LoadMoreAsync();
            Assert.That(vm.Error.Value, Is.Not.Null);
            Assert.That(vm.Items.ToArray(), Is.EqualTo(items));
            Assert.That(vm.UsageText.Value, Is.EqualTo(usage));
            Assert.That(vm.IsLoadingVisible.Value, Is.False);
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));
            refresh = vm.LoadAsync();
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            scope.Handler.Requests[2].Complete(Response(empty: true));
            await refresh;
            Assert.That(vm.Items, Is.Empty);
            Assert.That(vm.IsEmpty.Value, Is.True);
            Assert.That(vm.Error.Value, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(FileBrowserViewMode.Icon, false)]
    [TestCase(FileBrowserViewMode.List, false)]
    [TestCase(FileBrowserViewMode.Icon, true)]
    [TestCase(FileBrowserViewMode.List, true)]
    public async Task SlowInitialLoadShowsBoundedNonInteractivePlaceholders(FileBrowserViewMode mode, bool light)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        vm.ViewMode.Value = mode;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 320,
            Height = 520,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        try
        {
            window.Show();
            await WaitFor(() => scope.Handler.Requests.Count == 1 && vm.ShowPlaceholders.Value);
            HeadlessTestHelpers.Render();
            var skeleton = view.FindControl<ItemsControl>("LoadingSkeleton")!;
            Assert.That(skeleton.IsEffectivelyVisible, Is.True);
            Assert.That(skeleton.IsHitTestVisible, Is.False);
            Assert.That(skeleton.Items, Has.Count.EqualTo(24));
            Assert.That(view.FindControl<ListBox>("StorageItems")!.GetRealizedContainers(), Is.Empty);
            Assert.That(view.FindControl<TextBlock>("EmptyState")!.IsEffectivelyVisible, Is.False);
            if (Environment.GetEnvironmentVariable("BEUTL_STORAGE_CAPTURE") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(path, $"loading-{mode}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
            scope.Handler.Requests[0].Complete(Response(empty: true));
            await WaitFor(() => !vm.IsLoading.Value);
            HeadlessTestHelpers.Render();
            Assert.That(skeleton.IsEffectivelyVisible, Is.False);
            Assert.That(view.FindControl<TextBlock>("EmptyState")!.IsEffectivelyVisible, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CompletedAndCancelledRequestsCannotShowDelayedLoadingFeedback()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        await Task.Delay(250);
        Assert.That(vm.IsLoadingVisible.Value, Is.False);
        Assert.That(vm.ShowPlaceholders.Value, Is.False);
        var refresh = vm.LoadAsync();
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        SignIn(scope.Clients, null);
        await Task.Delay(250);
        Assert.That(vm.IsLoadingVisible.Value, Is.False);
        Assert.That(vm.IsLoadingMoreVisible.Value, Is.False);
        Assert.That(vm.ShowPlaceholders.Value, Is.False);
        Assert.That(vm.Items, Is.Empty);
        scope.Handler.Requests[1].Complete(Response());
        await refresh;
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

    internal sealed class StorageScope : IAsyncDisposable
    {
        public Handler Handler { get; } = new();
        private readonly HttpClient _http;
        public BeutlApiApplication Clients { get; }
        public CloudStorageViewModel ViewModel { get; }

        public StorageScope(Func<DateTimeOffset>? utcNow = null)
        {
            _http = new HttpClient(Handler);
            Clients = new BeutlApiApplication(_http, new ExtensionProvider());
            SignIn(Clients, "a");
            ViewModel = new(Clients, () => throw new InvalidOperationException("Not used by this test"), utcNow);
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
