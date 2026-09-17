using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Moq;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.CloudStorageTests;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class FileBrowserStorageTests
{
    [AvaloniaTest]
    public void MultipleServicesAreListedAndCreatedOnlyWhenSelected()
    {
        var first = new TestProvider("first", "First storage");
        var second = new TestProvider("second", "Second storage");
        using var vm = Create(first, second);
        var view = new FileBrowserTabView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(first.Browsers, Is.Empty);
            Assert.That(second.Browsers, Is.Empty);
            Assert.That(view.FindControl<ItemsControl>("StorageLocations"), Is.Null);
            var menuButton = view.FindControl<Button>("StorageLocationsButton")!;
            var menu = (MenuFlyout)menuButton.Flyout!;
            menuButton.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Settle();
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(menuButton.Flyout, Is.SameAs(menu));
            Assert.That(menu.Items.OfType<MenuItem>().Count(), Is.EqualTo(3));
            menu.Items.OfType<MenuItem>().Single(x => ReferenceEquals(x.DataContext, first))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            HeadlessTestHelpers.Render();
            Assert.That(vm.ActiveStorageProvider.Value, Is.SameAs(first));
            Assert.That(vm.Header.Value, Is.EqualTo(first.DisplayName));
            Assert.That(first.Browsers, Has.Count.EqualTo(1));

            menuButton.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Settle();
            Assert.That(menu.IsOpen, Is.True);
            menu.Items.OfType<MenuItem>().Single(x => ReferenceEquals(x.DataContext, second))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            HeadlessTestHelpers.Render();
            Assert.That(first.Browsers[0].Disposed, Is.True);
            Assert.That(vm.ActiveStorageProvider.Value, Is.SameAs(second));
            Assert.That(second.Browsers, Has.Count.EqualTo(1));
            Assert.That(view.FindControl<ContentControl>("StorageContent")!.IsEffectivelyVisible, Is.True);
            vm.ShowLocalFiles();
            Assert.That(second.Browsers[0].Disposed, Is.True);
            Assert.That(vm.IsStorageView.Value, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void ReturningToLocalFilesPreservesTheLocationAndOtherTabsConnection()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-storage-local-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var provider = new TestProvider("test", "Test storage");
            using var first = Create(provider);
            using var second = Create(provider);
            first.RootPath.Value = directory;
            first.OpenStorage(provider);
            second.OpenStorage(provider);
            first.ShowLocalFiles();
            Assert.That(provider.Browsers[0].Disposed, Is.True);
            Assert.That(provider.Browsers[1].Disposed, Is.False);
            Assert.That(first.IsStorageView.Value, Is.False);
            Assert.That(first.StorageBrowser.Value, Is.Null);
            Assert.That(second.IsStorageView.Value, Is.True);
            Assert.That(first.RootPath.Value, Is.EqualTo(directory));
            Assert.That(first.HasStorageProviders, Is.True);
            first.OpenStorage(provider);
            Assert.That(provider.Browsers, Has.Count.EqualTo(3));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaTest]
    public async Task BeutlConnectionIsLazyAndReturningToLocalCancelsRequestsAndAuthenticationSubscriptions()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        var provider = BeutlProvider(clients);
        using var vm = Create(provider);
        Assert.That(handler.Requests, Is.Empty);
        vm.OpenStorage(provider);
        await WaitFor(() => handler.Requests.Count == 1);
        var browser = (CloudStorageViewModel)vm.StorageBrowser.Value!;
        vm.ShowLocalFiles();
        Assert.That(handler.Requests[0].Token.IsCancellationRequested, Is.True);
        handler.Requests[0].Complete(Response());
        SignIn(clients, "b");
        await Task.Delay(50);
        HeadlessTestHelpers.Settle();
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(browser.Items, Is.Empty);
        Assert.That(vm.StorageBrowser.Value, Is.Null);
    }

    [AvaloniaTest]
    public void RecreatedViewsReuseTheBrowserWithoutCreatingAnotherConnection()
    {
        var provider = new TestProvider("test", "Test storage");
        using var vm = Create(provider);
        vm.OpenStorage(provider);
        var window = new Window { Width = 320, Height = 520 };
        try
        {
            window.Content = new FileBrowserTabView { DataContext = vm };
            window.Show();
            HeadlessTestHelpers.Render();
            window.Content = null;
            window.Content = new FileBrowserTabView { DataContext = vm };
            HeadlessTestHelpers.Render();
            Assert.That(provider.Browsers, Has.Count.EqualTo(1));
            Assert.That(provider.Browsers[0].ViewsCreated, Is.EqualTo(2));
            Assert.That(provider.Browsers[0].Disposed, Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void LegacyStorageVisibilitySettingDoesNotHideRegisteredServices()
    {
        var config = new ViewConfig();
        CoreSerializer.PopulateFromJsonObject(config, new JsonObject { ["ShowStorageServices"] = false });
        Assert.That(CoreSerializer.SerializeToJsonObject(config).ContainsKey("ShowStorageServices"), Is.False);
        var provider = new TestProvider("test", "Test storage");
        using var vm = Create(provider);
        Assert.That(vm.HasStorageProviders, Is.True);
        Assert.That(provider.Browsers, Is.Empty, "Being available in the menu must not connect automatically.");
        vm.OpenStorage(provider);
        Assert.That(vm.ActiveStorageProvider.Value, Is.SameAs(provider));
    }

    [AvaloniaTest]
    public void NoRegisteredServicesHaveNoLocationMenu()
    {
        using var vm = Create();
        var view = new FileBrowserTabView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<ItemsControl>("StorageLocations"), Is.Null);
            Assert.That(view.FindControl<Button>("StorageLocationsButton")!.IsEffectivelyVisible, Is.False);
            Assert.That(vm.StorageProviders, Is.Empty);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RegistryRejectsDuplicateIdsAndDisposingDuringCreationDisposesTheNewBrowser()
    {
        var provider = new TestProvider("test", "Test storage");
        Assert.Throws<ArgumentException>(() => new FileBrowserStorageProviderRegistry(provider, provider));
        using var vm = Create(provider);
        provider.OnCreate = vm.Dispose;
        vm.OpenStorage(provider);
        Assert.That(vm.StorageBrowser.Value, Is.Null);
        Assert.That(vm.IsStorageView.Value, Is.False);
        Assert.That(provider.Browsers.Single().Disposed, Is.True);
    }

    [AvaloniaTest]
    public async Task StorageUsesSharedFileItemsAndToolbarNavigation()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        var provider = BeutlProvider(clients);
        using var vm = Create(provider);
        vm.OpenStorage(provider);
        var browser = (CloudStorageViewModel)vm.StorageBrowser.Value!;
        await WaitFor(() => handler.Requests.Count == 1);
        handler.Requests[0].Complete(Response());
        await WaitFor(() => !browser.IsLoading.Value);
        var view = new FileBrowserTabView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(browser.ViewMode.Value, Is.EqualTo(FileBrowserViewMode.Icon));
            var items = view.GetVisualDescendants().OfType<FileBrowserItemView>().ToArray();
            Assert.That(items, Is.Not.Empty);
            Assert.That(items.All(x => x.IsIconView), Is.True);
            var modeButton = view.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "StorageViewModeButton");
            modeButton.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Render();
            Assert.That(browser.ViewMode.Value, Is.EqualTo(FileBrowserViewMode.List));
            Assert.That(view.GetVisualDescendants().OfType<FileBrowserItemView>().All(x => !x.IsIconView), Is.True);

            var navigation = browser.OpenFolderAsync(browser.Items[0]);
            await WaitFor(() => handler.Requests.Count == 2);
            handler.Requests[1].Complete(Response(folder: "folder & 日本"));
            await navigation;
            HeadlessTestHelpers.Render();
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                .Any(x => x.IsEffectivelyVisible && x.Text == "素材"), Is.True,
                "The existing toolbar must observe breadcrumb collection changes.");
            var root = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(x => x.IsEffectivelyVisible && x.Text == Strings.CloudStorage);
            var point = root.TranslatePoint(new Point(root.Bounds.Width / 2, root.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await WaitFor(() => browser.Breadcrumbs.Count == 1 && !browser.IsLoading.Value);
            Assert.That(handler.Requests, Has.Count.EqualTo(2), "Returning to a recently visited folder should use its cache.");
            Assert.That(browser.Breadcrumbs, Has.Count.EqualTo(1));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task IntegratedStorageRemainsUsableAtNarrowWidths(int width, bool light)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        var provider = BeutlProvider(clients);
        using var vm = Create(provider);
        vm.OpenStorage(provider);
        await WaitFor(() => handler.Requests.Count == 1);
        handler.Requests[0].Complete(Response(name: "非常に長い日本語の映像素材-September-2026.mp4"));
        await WaitFor(() => !((CloudStorageViewModel)vm.StorageBrowser.Value!).IsLoading.Value);
        var view = new FileBrowserTabView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 520,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(view.GetVisualDescendants().OfType<CloudStorageView>().Count(), Is.EqualTo(1));
            foreach (var name in new[] { "StorageRefreshButton", "StorageHomeButton", "StorageLocationsButton" })
            {
                var button = view.FindControl<Button>(name)!;
                Assert.That(button.IsEffectivelyVisible, Is.True);
                Assert.That(button.TranslatePoint(default, view)!.Value.X + button.Bounds.Width,
                    Is.LessThanOrEqualTo(width));
            }
            if (Environment.GetEnvironmentVariable("BEUTL_STORAGE_CAPTURE") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(path, $"integrated-storage-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task DraggingMoveOnlyItemsToTheRootBreadcrumbDoesNotExportThem(bool isFolder)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        var provider = BeutlProvider(clients);
        using var vm = Create(provider);
        vm.OpenStorage(provider);
        var browser = (CloudStorageViewModel)vm.StorageBrowser.Value!;
        await WaitFor(() => handler.Requests.Count == 1);
        handler.Requests[0].Complete(Response());
        await WaitFor(() => !browser.IsLoading.Value);
        var navigation = browser.OpenFolderAsync(browser.Items[0]);
        await WaitFor(() => handler.Requests.Count == 2);
        var response = JsonNode.Parse(Response(folder: "folder & 日本"))!;
        response["entries"]![0]!["actions"] = new JsonArray("move");
        response["entries"]![0]!["kind"] = isFolder ? "folder" : "file";
        handler.Requests[1].Complete(response.ToJsonString());
        await navigation;
        var view = new FileBrowserTabView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var storageView = view.GetVisualDescendants().OfType<CloudStorageView>().Single();
            var list = storageView.FindControl<ListBox>("StorageItems")!;
            var itemPoint = list.ContainerFromIndex(0)!.TranslatePoint(new Point(20, 20), window)!.Value;
            var root = view.GetVisualDescendants().OfType<TextBlock>().Single(x => x.IsEffectivelyVisible && x.Text == Strings.CloudStorage);
            var rootPoint = root.TranslatePoint(new Point(root.Bounds.Width / 2, root.Bounds.Height / 2), window)!.Value;
            window.MouseDown(itemPoint, MouseButton.Left);
            var toolbarGap = storageView.TranslatePoint(new Point(storageView.Bounds.Width - 2, -1), window)!.Value;
            window.MouseMove(toolbarGap, RawInputModifiers.LeftMouseButton);
            Assert.That(handler.Requests, Has.Count.EqualTo(2), "Crossing toolbar padding must not start an export.");
            window.MouseMove(rootPoint, RawInputModifiers.LeftMouseButton);
            window.MouseUp(rootPoint, MouseButton.Left);
            await WaitFor(() => handler.Requests.Count == 3);
            Assert.That(handler.Requests[2].Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.Requests[2].Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/entries/move"));
            var move = JsonNode.Parse(await handler.Requests[2].ReadBodyAsync())!;
            Assert.That(move["parentId"], Is.Null);
            Assert.That(move["entries"]![0]!["id"]!.GetValue<string>(), Is.EqualTo("file"));
            handler.Requests[2].Complete("{\"affected\":1}");
            await WaitFor(() => handler.Requests.Count == 4);
            handler.Requests[3].Complete(Response(folder: "folder & 日本", empty: true));
            await WaitFor(() => !browser.IsLoading.Value);
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(browser.ActionError.Value, Is.Null);
            Assert.That(browser.IsTransferring.Value, Is.False);
        }
        finally { window.Close(); }
    }

    private static FileBrowserTabViewModel Create(params IFileBrowserStorageProvider[] providers) =>
        new(new Mock<IEditorContext>().Object, new(providers));

    private static BeutlStorageProvider BeutlProvider(BeutlApiApplication clients) =>
        new(clients, () => throw new InvalidOperationException("Not used by this test"));

    private sealed class TestProvider(string id, string displayName) : IFileBrowserStorageProvider
    {
        public string Id => id;
        public string DisplayName => displayName;
        public List<TestBrowser> Browsers { get; } = [];
        public Action? OnCreate { get; set; }
        public IFileBrowserStorageBrowser CreateBrowser()
        {
            OnCreate?.Invoke();
            var browser = new TestBrowser();
            Browsers.Add(browser);
            return browser;
        }
    }

    private sealed class TestBrowser : IFileBrowserStorageBrowser
    {
        private readonly ReactiveCommand _refresh = new();
        public bool Disposed { get; private set; }
        public int ViewsCreated { get; private set; }
        public ICommand Refresh => _refresh;
        public Control CreateView() { ViewsCreated++; return new TextBlock { Text = "Storage browser" }; }
        public void Dispose() { Disposed = true; _refresh.Dispose(); }
    }
}
