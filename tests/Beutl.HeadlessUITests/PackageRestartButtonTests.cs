using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Pages.ExtensionsPages;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.UI.Controls;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// A change scheduled for the next launch offers a restart where Cancel used to be; Cancel stays reachable.
[TestFixture]
public class PackageRestartButtonTests
{
    [AvaloniaTest]
    public void Library_row_with_a_scheduled_change_offers_a_restart_and_cancels_from_the_menu()
    {
        using var viewModel = new StubRow(scheduled: true);
        (Window window, List<Button> buttons) = ShowRow(viewModel);

        try
        {
            Button restart = buttons.Single(x => x.Command == viewModel.Restart);
            Assert.Multiple(() =>
            {
                Assert.That(restart.Classes, Does.Not.Contain("transparent"));
                Assert.That(restart.IsEffectivelyEnabled, Is.True);
                Assert.That(restart.Classes, Does.Contain("accent"));
                Assert.That(restart.Content, Is.EqualTo(Strings.Restart));
                Assert.That(buttons.Where(x => x.Command == viewModel.Cancel), Is.Empty, "Cancel leaves the action cell.");
                Assert.That(buttons.Single(x => x.Command == viewModel.Install).Classes, Does.Contain("transparent"));
                Assert.That(buttons.Single(x => x.Command == viewModel.Update).Classes, Does.Contain("transparent"));
                Assert.That(buttons.Single(x => x.Command == viewModel.Uninstall).Classes, Does.Contain("transparent"));
            });

            restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(viewModel.Restarts, Is.EqualTo(1));

            FAMenuFlyoutItem cancel = OpenMenuItem(buttons, Strings.Cancel);
            Assert.Multiple(() =>
            {
                Assert.That(cancel.IsVisible, Is.True);
                Assert.That(cancel.Command, Is.SameAs(viewModel.Cancel));
            });
        }
        finally
        {
            CloseMenus(buttons);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Library_row_without_a_scheduled_change_shows_neither_restart_nor_cancel()
    {
        using var viewModel = new StubRow(scheduled: false);
        (Window window, List<Button> buttons) = ShowRow(viewModel);

        try
        {
            Button restart = buttons.Single(x => x.Command == viewModel.Restart);
            Assert.Multiple(() =>
            {
                Assert.That(restart.Classes, Does.Contain("transparent"));
                // The transparent class leaves a button reachable with Tab; this one would quit Beutl.
                Assert.That(restart.IsEffectivelyEnabled, Is.False, "The hidden restart must not take focus or run.");
            });
            Assert.That(OpenMenuItem(buttons, Strings.Cancel).IsVisible, Is.False);
        }
        finally
        {
            CloseMenus(buttons);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Details_page_with_a_scheduled_change_offers_a_restart_above_cancel()
    {
        PackageResponse response = TestStore.CreatePackageResponse("Beutl.RestartButton");
        using var handler = new StoreHandler(response, TestStore.CreateReleaseResponse("1.0.0"));
        using var http = new HttpClient(handler);
        var provider = new ExtensionProvider();
        await using var app = new BeutlApiApplication(http, provider);
        var package = await app.GetResource<DiscoverService>().GetPackage(response.Name, CancellationToken.None);
        using var viewModel = new PackageDetailsPageViewModel(package, app, new EditorService(provider), new ProjectService());
        await viewModel.IsBusy.FirstAsync(busy => !busy).ToTask().WaitAsync(TimeSpan.FromSeconds(10));
        PackageChangesQueue queue = app.GetResource<PackageChangesQueue>();
        var page = new PackageDetailsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button restart = FindButton(page, viewModel.Restart);
            Button cancel = FindButton(page, viewModel.Cancel);
            Assert.Multiple(() =>
            {
                Assert.That(restart.IsEffectivelyVisible, Is.False);
                Assert.That(cancel.IsEffectivelyVisible, Is.False);
                Assert.That(FindButton(page, viewModel.Install).IsEffectivelyVisible, Is.True);
            });

            queue.UninstallQueue(new PackageIdentity(response.Name, NuGetVersion.Parse("1.0.0")));
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(restart.IsEffectivelyVisible, Is.True);
                Assert.That(restart.Classes, Does.Contain("accent"));
                Assert.That(restart.Content, Is.EqualTo(Strings.Restart));
                Assert.That(cancel.IsEffectivelyVisible, Is.True);
                Assert.That(restart.TranslatePoint(default, page)!.Value.Y, Is.LessThan(cancel.TranslatePoint(default, page)!.Value.Y));
                Assert.That(FindButton(page, viewModel.Install).IsEffectivelyVisible, Is.False);
                Assert.That(FindButton(page, viewModel.Update).IsEffectivelyVisible, Is.False);
                Assert.That(FindButton(page, viewModel.Uninstall).IsEffectivelyVisible, Is.False);
            });
        }
        finally
        {
            queue.Cancel(response.Name);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static (Window Window, List<Button> Buttons) ShowRow(StubRow viewModel)
    {
        var page = new LibraryPage();
        var template = (IDataTemplate)page.Resources["UserPackageItemTemplate"]!;
        Control content = new ContentPresenter
        {
            Content = viewModel,
            ContentTemplate = template,
        };
        var window = new Window { Content = content, Width = 800, Height = 300 };
        window.Show();
        HeadlessTestHelpers.Render();
        return (window, content.GetVisualDescendants().OfType<Button>().ToList());
    }

    // Opens the row's overflow menu as its button does, and returns the item with the text.
    private static FAMenuFlyoutItem OpenMenuItem(List<Button> buttons, string text)
    {
        Button overflow = buttons.Single(x => x.ContextFlyout is FAMenuFlyout);
        overflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();
        return ((FAMenuFlyout)overflow.ContextFlyout!).Items.OfType<FAMenuFlyoutItem>().Single(x => x.Text == text);
    }

    private static void CloseMenus(List<Button> buttons)
    {
        foreach (Button button in buttons)
        {
            button.ContextFlyout?.Hide();
        }
    }

    private static Button FindButton(Control page, object command)
    {
        return page.GetVisualDescendants().OfType<Button>().Single(x => x.Command == command);
    }

    private sealed class StubRow : IUserPackageViewModel
    {
        public StubRow(bool scheduled)
        {
            DisplayName = Observable.Return<string?>("Package").ToReadOnlyReactivePropertySlim();
            LogoUrl = Observable.Return<string?>(null).ToReadOnlyReactivePropertySlim();
            IsInstallButtonVisible = Observable.Return(false).ToReadOnlyReactivePropertySlim();
            IsUninstallButtonVisible = Observable.Return(!scheduled).ToReadOnlyReactivePropertySlim();
            IsUpdateButtonVisible = Observable.Return(false).ToReadOnlyReactivePropertySlim();
            CanCancel = Observable.Return(scheduled).ToReadOnlyReactivePropertySlim();
            Restart.Subscribe(() =>
            {
                Restarts++;
                return Task.CompletedTask;
            });
        }

        public int Restarts { get; private set; }

        public string Name => "Package";

        public IReadOnlyReactiveProperty<string?> DisplayName { get; }

        public IReadOnlyReactiveProperty<string?> LogoUrl { get; }

        public string Publisher => "Publisher";

        public bool IsRemote => false;

        public ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

        public IReadOnlyReactiveProperty<bool> IsUpdateButtonVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

        public AsyncReactiveCommand Install { get; } = new();

        public AsyncReactiveCommand Update { get; } = new();

        public AsyncReactiveCommand Uninstall { get; } = new();

        public AsyncReactiveCommand Cancel { get; } = new();

        public AsyncReactiveCommand Restart { get; } = new();

        public ReactivePropertySlim<bool> IsBusy { get; } = new();

        public ReactivePropertySlim<string?> StatusText { get; } = new();

        public void Dispose()
        {
            DisplayName.Dispose();
            LogoUrl.Dispose();
            IsInstallButtonVisible.Dispose();
            IsUninstallButtonVisible.Dispose();
            IsUpdateButtonVisible.Dispose();
            CanCancel.Dispose();
            Install.Dispose();
            Update.Dispose();
            Uninstall.Dispose();
            Cancel.Dispose();
            Restart.Dispose();
            IsBusy.Dispose();
            StatusText.Dispose();
        }
    }
}
