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
using Beutl.Pages.SettingsPages;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
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
        using var vm = Create(new ViewConfig(), first, second);
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
    public void HidingServicesUpdatesEveryBrowserAndPreservesLocalLocation()
    {
        var config = new ViewConfig();
        var provider = new TestProvider("test", "Test storage");
        using var first = Create(config, provider);
        using var second = Create(config, provider);
        string directory = Path.GetTempPath();
        first.RootPath.Value = directory;
        first.OpenStorage(provider);
        second.OpenStorage(provider);
        config.ShowStorageServices = false;
        Assert.That(provider.Browsers.All(x => x.Disposed), Is.True);
        foreach (var vm in new[] { first, second })
        {
            Assert.That(vm.ShowStorageServices.Value, Is.False);
            Assert.That(vm.IsStorageView.Value, Is.False);
            Assert.That(vm.StorageBrowser.Value, Is.Null);
            vm.OpenStorage(provider);
            Assert.That(vm.StorageBrowser.Value, Is.Null);
        }
        Assert.That(first.RootPath.Value, Is.EqualTo(directory));
        config.ShowStorageServices = true;
        Assert.That(first.ShowStorageServices.Value, Is.True);
        Assert.That(provider.Browsers, Has.Count.EqualTo(2), "Re-enabling must not connect.");
    }

    [AvaloniaTest]
    public async Task BeutlConnectionIsLazyAndHidingCancelsRequestsAndAuthenticationSubscriptions()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        var provider = BeutlProvider(clients);
        var config = new ViewConfig();
        using var vm = Create(config, provider);
        Assert.That(handler.Requests, Is.Empty);
        vm.OpenStorage(provider);
        await WaitFor(() => handler.Requests.Count == 1);
        var browser = (CloudStorageViewModel)vm.StorageBrowser.Value!;
        config.ShowStorageServices = false;
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
        using var vm = Create(new ViewConfig(), provider);
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
    public void SettingIsPersistedAndTheSettingsPageUpdatesOpenBrowsers()
    {
        var config = GlobalConfiguration.Instance.ViewConfig;
        bool original = config.ShowStorageServices;
        try
        {
            config.ShowStorageServices = true;
            var provider = new TestProvider("test", "Test storage");
            using var browser = Create(config, provider);
            using var settings = new ViewSettingsPageViewModel(new(() => new EditorSettingsPageViewModel()));
            var page = new ViewSettingsPage { DataContext = settings };
            var window = new Window { Content = page, Width = 900, Height = 800 };
            try
            {
                window.Show();
                var toggle = page.FindControl<ToggleSwitch>("ShowStorageServicesToggle")!;
                toggle.IsChecked = false;
                Assert.That(config.ShowStorageServices, Is.False);
                Assert.That(browser.ShowStorageServices.Value, Is.False);
                var saved = CoreSerializer.SerializeToJsonObject(config);
                var restored = new ViewConfig();
                CoreSerializer.PopulateFromJsonObject(restored, saved);
                Assert.That(restored.ShowStorageServices, Is.False);
                Assert.That(new ViewConfig().ShowStorageServices, Is.True);
                config.ShowStorageServices = true;
                Assert.That(toggle.IsChecked, Is.True);
            }
            finally { window.Close(); }
        }
        finally { config.ShowStorageServices = original; }
    }

    [AvaloniaTest]
    public void HiddenServicesHaveNoEntryOrLocationMenu()
    {
        var provider = new TestProvider("test", "Test storage");
        using var vm = Create(new ViewConfig { ShowStorageServices = false }, provider);
        var view = new FileBrowserTabView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<ItemsControl>("StorageLocations"), Is.Null);
            Assert.That(view.FindControl<Button>("StorageLocationsButton")!.IsEffectivelyVisible, Is.False);
            Assert.That(provider.Browsers, Is.Empty);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RegistryRejectsDuplicateIdsAndHidingDuringCreationDisposesTheNewBrowser()
    {
        var provider = new TestProvider("test", "Test storage");
        Assert.Throws<ArgumentException>(() => new FileBrowserStorageProviderRegistry(provider, provider));
        var config = new ViewConfig();
        using var vm = Create(config, provider);
        provider.OnCreate = () => config.ShowStorageServices = false;
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
        using var vm = Create(new ViewConfig(), provider);
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
            await WaitFor(() => handler.Requests.Count == 3);
            Assert.That(handler.Requests[2].Uri.Query, Does.Not.Contain("folder="));
            handler.Requests[2].Complete(Response());
            await WaitFor(() => !browser.IsLoading.Value);
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
        using var vm = Create(new ViewConfig(), provider);
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

    private static FileBrowserTabViewModel Create(ViewConfig config, params IFileBrowserStorageProvider[] providers) =>
        new(new Mock<IEditorContext>().Object, config, new(providers));

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
