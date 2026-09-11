using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Headless.NUnit;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Beutl.Controls;
using Avalonia.Threading;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Services;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserSettingsPageTests
{
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
    public void Page_ImmediatelyPersistsBoundSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"));
        using var vm = new BrowserSettingsPageViewModel(profile, null);
        var page = new BrowserSettingsPage { DataContext = vm };
        var window = new Window { Content = page, Width = 760, Height = 840 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(page.GetVisualDescendants().OfType<OptionsDisplayItem>().Count(), Is.EqualTo(5));
            if (Environment.GetEnvironmentVariable("BEUTL_BROWSER_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, "settings.png"), PngBitmapEncoderOptions.Default);
            }
            page.FindControl<ComboBox>("SearchEngineComboBox")!.SelectedIndex = 1;
            page.FindControl<ToggleSwitch>("SuggestionsToggle")!.IsChecked = false;
            var saved = new BrowserProfile(Path.Combine(root, "profile.json"));
            Assert.That(saved.Engine, Is.EqualTo(BrowserSearchEngine.Bing));
            Assert.That(saved.SuggestionsEnabled, Is.False);
            Assert.That(vm.CanClearCookies, Is.False);
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void SettingsDialog_NavigatesDirectlyToBrowserPage()
    {
        using var vm = TestShell.MainViewModel.CreateSettingsDialog();
        var dialog = new SettingsDialog { DataContext = vm };
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
        var vm = new BrowserSettingsPageViewModel(new BrowserProfile(file), () => { requests++; return pending.Task; });
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
