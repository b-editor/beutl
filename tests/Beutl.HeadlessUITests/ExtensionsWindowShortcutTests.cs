using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class ExtensionsWindowShortcutTests
{
    [AvaloniaTest]
    public async Task Refresh_follows_the_visible_page_and_uses_the_saved_keymap()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        using var keymapFile = new AppRootFileBackup("keymap.json");
        MainViewModel main = TestShell.MainViewModel;
        var configuration = TestShell.Configuration;
        using var http = new HttpClient(new EmptyJsonArrayHandler());
        await using var clients = new BeutlApiApplication(http,
            new ExtensionProvider(configuration.ExtensionConfig), configuration.ViewConfig, configuration.UpdateConfig);
        var extension = ExtensionsToolWindowExtension.Instance;
        clients.GetResource<PackageManager>().LoadExtension(extension, restoreSettings: false);
        var manager = clients.GetResource<ContextCommandManager>();
        using var viewModel = new ExtensionsPageViewModel(clients, main.EditorService, main.ProjectService);
        var window = new ExtensionsPage { DataContext = viewModel };
        manager.Attach(window, extension);
        using var host = new ControlHost(window);

        var discover = viewModel.Discover;
        await HeadlessTestHelpers.WaitUntilAsync(discover.Refresh.CanExecute);
        int discoverRefreshes = 0;
        using var discoverSubscription = discover.Refresh.Subscribe(() =>
        {
            discoverRefreshes++;
            return Task.CompletedTask;
        });

        Assert.That(window.FindControl<TextBox>("searchTextBox")!.Focus(), Is.True);
        Press(window, PhysicalKey.F5);
        await HeadlessTestHelpers.WaitUntilAsync(() => discoverRefreshes == 1 && discover.Refresh.CanExecute());

        var frame = window.FindControl<FAFrame>("frame")!;
        Assert.That(frame.Navigate(typeof(SearchPage), "keyboard shortcut"), Is.True);
        HeadlessTestHelpers.Render();
        var searchPage = (SearchPage)frame.Content!;
        var search = (SearchPageViewModel)searchPage.DataContext!;
        await HeadlessTestHelpers.WaitUntilAsync(search.Refresh.CanExecute);
        int searchRefreshes = 0;
        using var searchSubscription = search.Refresh.Subscribe(() =>
        {
            searchRefreshes++;
            return Task.CompletedTask;
        });

        Assert.That(searchPage.Focus(), Is.True);
        Press(window, PhysicalKey.R, RawInputModifiers.Control);
        await HeadlessTestHelpers.WaitUntilAsync(() => searchRefreshes == 1 && search.Refresh.CanExecute());

        using var model = new KeyMapSettingsPageViewModel(manager, TestShell.Extensions);
        var f5 = model.Group.Single(group => group.Extension == extension).Items.Single(item => item.GestureIndex == 0);
        f5.SetKeyGesture(new KeyGesture(Key.F7));

        Press(window, PhysicalKey.F5);
        Assert.That(searchRefreshes, Is.EqualTo(1));
        Press(window, PhysicalKey.F7);
        await HeadlessTestHelpers.WaitUntilAsync(() => searchRefreshes == 2 && search.Refresh.CanExecute());
        Assert.That(discoverRefreshes, Is.EqualTo(1));

        var restoredManager = new ContextCommandManager(new ContextCommandSettingsStore());
        restoredManager.Register(extension);
        var entry = restoredManager.GetDefinitions<ExtensionsToolWindowExtension>().Single();
        Assert.That(ContextCommandManager.CurrentGestures(entry),
            Is.EqualTo(new[] { KeyGesture.Parse("F7"), KeyGesture.Parse("Ctrl+R"), KeyGesture.Parse("Cmd+R") }));
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        HeadlessTestHelpers.Settle();
    }

    private sealed class EmptyJsonArrayHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
    }
}
