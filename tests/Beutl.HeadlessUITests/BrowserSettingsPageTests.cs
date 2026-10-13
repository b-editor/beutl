using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserSettingsPageTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task BackgroundFilterChangesUpdateStatusOnTheUiThread(bool loadCache)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new BrowserAdBlockTestRules.FilterResponseHandler());
        string cache = Path.Combine(root, "filters.json");
        var filters = new BrowserAdBlockFilterStore(cache, client);
        if (loadCache)
        {
            await filters.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None);
            filters = new BrowserAdBlockFilterStore(cache, client);
        }
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: filters);
        using var vm = new BrowserSettingsPageViewModel(profile, () => null);
        var notifications = new List<bool>();
        using var subscription = vm.FilterStatus.Skip(1).Subscribe(_ => notifications.Add(Dispatcher.UIThread.CheckAccess()));
        try
        {
            await Task.Run(() => loadCache
                ? filters.GetAsync([BrowserAdBlockFilterStore.EasyListUrl])
                : filters.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None));
            Dispatcher.UIThread.RunJobs();
            Assert.That(notifications, Is.EqualTo(new[] { true }));
            Assert.That(vm.FilterStatus.Value, Does.Contain("1"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void QueuedFilterStatusChangeIsIgnoredAfterDisposal()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new BrowserAdBlockTestRules.FilterResponseHandler());
        var filters = new BrowserAdBlockFilterStore(Path.Combine(root, "filters.json"), client);
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: filters);
        using var vm = new BrowserSettingsPageViewModel(profile, () => null);
        int notifications = 0;
        using var subscription = vm.FilterStatus.Skip(1).Subscribe(_ => Interlocked.Increment(ref notifications));
        try
        {
            // Hold the UI thread until the worker has queued its notification, then close the page.
            Task update = Task.Run(() => filters.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None));
            Assert.That(update.Wait(TimeSpan.FromSeconds(10)), Is.True);
            vm.Dispose();
            Dispatcher.UIThread.RunJobs();
            Assert.That(notifications, Is.Zero);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public async Task SettingsHost_UsesRequestingWindowAndReusesOpenDialog()
    {
        var host = new BrowserSettingsHost(TestShell.MainViewModel.CreateSettingsDialog);
        var owner = new Window();
        try
        {
            owner.Show();
            Task pending = host.OpenBrowserSettingsAsync(owner);
            Dispatcher.UIThread.RunJobs();
            var dialog = owner.OwnedWindows.OfType<SettingsDialog>().Single();
            Assert.That(dialog.FindControl<FAFrame>("frame")!.Content, Is.TypeOf<BrowserSettingsPage>());
            await host.OpenBrowserSettingsAsync(owner);
            Assert.That(owner.OwnedWindows, Has.Count.EqualTo(1));
            dialog.Close();
            await pending;
        }
        finally { owner.Close(); }
    }

    [AvaloniaTest]
    public async Task Page_ImmediatelyPersistsBoundSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new BrowserAdBlockTestRules.FilterResponseHandler());
        var filters = new BrowserAdBlockFilterStore(Path.Combine(root, "filters.json"), client);
        await filters.UpdateAsync([BrowserAdBlockFilterStore.EasyListUrl], CancellationToken.None);
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: filters);
        using var vm = new BrowserSettingsPageViewModel(profile, () => null);
        var page = new BrowserSettingsPage { DataContext = vm };
        var window = new Window { Content = page, Width = 640, Height = 840, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(page.GetVisualDescendants().OfType<OptionsDisplayItem>().Count(), Is.EqualTo(6));
            page.FindControl<ToggleSwitch>("BlockAdsToggle")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            if (HeadlessCapture.IsRequested)
            {
                // Capture the expanded settings after its opacity/chevron transition settles.
                await Task.Delay(350);
                HeadlessCapture.SaveIfRequested(window, nameof(BrowserSettingsPageTests), "settings");
            }
            page.FindControl<ComboBox>("SearchEngineComboBox")!.SelectedIndex = 1;
            page.FindControl<ToggleSwitch>("SuggestionsToggle")!.IsChecked = false;
            var saved = new BrowserProfile(Path.Combine(root, "profile.json"));
            Assert.That(saved.Engine, Is.EqualTo(BrowserSearchEngine.Bing));
            Assert.That(saved.SuggestionsEnabled, Is.False);
            Assert.That(saved.BlockAds, Is.True);
            Assert.That(vm.CanClearCookies, Is.False);
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void SettingsDialog_NavigatesDirectlyToBrowserPage()
    {
        using var vm = TestShell.MainViewModel.CreateSettingsDialog();
        var dialog = HeadlessAppWindow.Create(() => new SettingsDialog());
        dialog.DataContext = vm;
        try
        {
            vm.GoToBrowserSettingsPage();
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            var frame = dialog.FindControl<FAFrame>("frame")!;
            Assert.That(frame.Content, Is.TypeOf<BrowserSettingsPage>());
            Assert.That(((Control)frame.Content!).DataContext, Is.SameAs(vm.Browser));
            var nav = dialog.FindControl<FANavigationView>("nav")!;
            Assert.That(((FANavigationViewItem)nav.SelectedItem!).Tag, Is.EqualTo(typeof(BrowserSettingsPage)));
        }
        finally { dialog.Close(); }
    }

    [AvaloniaTest]
    public async Task CookieDeletion_RequiresConfirmationAndCanFinishAfterPageDisposal()
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profile.json");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        var vm = new BrowserSettingsPageViewModel(new BrowserProfile(file), () => () => { requests++; return pending.Task; });
        await vm.ClearCookiesAsync();
        Assert.That(requests, Is.Zero);
        vm.CookieDeletionConfirmed.Value = true;
        Task operation = vm.ClearCookiesAsync();
        Assert.That(requests, Is.EqualTo(1));
        vm.Dispose();
        pending.SetResult();
        await operation;
    }
}
