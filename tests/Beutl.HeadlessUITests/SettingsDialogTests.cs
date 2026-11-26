using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Language;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SettingsDialogTests
{
    [AvaloniaTest]
    [TestCase(typeof(EditorSettingsPage), "editor")]
    [TestCase(typeof(PreviewSettingsPage), "preview")]
    [TestCase(typeof(PerformanceSettingsPage), "performance")]
    [TestCase(typeof(VersionControlSettingsPage), "version-control")]
    [TestCase(typeof(PrivacySettingsPage), "privacy")]
    [TestCase(typeof(TerminalSettingsPage), "terminal")]
    [TestCase(typeof(InformationPage), "about")]
    public async Task Settings_pages_are_reachable_from_the_navigation_pane(Type pageType, string imageName)
    {
        await TestReset.ResetShellAsync();
        var configuration = TestShell.Configuration;
        var telemetry = configuration.TelemetryConfig;
        var previousTelemetry = (telemetry.Beutl_Application.Value, telemetry.Beutl_PackageManagement.Value,
            telemetry.Beutl_Api_Client.Value, telemetry.Beutl_Logging.Value);
        string? previousGpu = configuration.GraphicsConfig.SelectedGpuName.Value;
        var viewModel = TestShell.MainViewModel.CreateSettingsDialog();
        var dialog = HeadlessAppWindow.Create(() => new SettingsDialog());
        dialog.DataContext = viewModel;
        dialog.Width = 1080;
        dialog.Height = 800;
        dialog.RequestedThemeVariant = ThemeVariant.Dark;
        try
        {
            dialog.Show();
            var nav = dialog.FindControl<FANavigationView>("nav")!;
            var frame = dialog.FindControl<FAFrame>("frame")!;
            nav.PaneDisplayMode = FANavigationViewPaneDisplayMode.Left;
            nav.IsPaneOpen = true;
            HeadlessTestHelpers.Render(3);

            FANavigationViewItem item = nav.MenuItemsSource!.OfType<FANavigationViewItem>()
                .Single(item => Equals(item.Tag, pageType));
            item.BringIntoView();
            HeadlessTestHelpers.Render(3);
            HeadlessTestHelpers.Click(item, ticks: 3);
            await HeadlessTestHelpers.WaitUntilAsync(() => frame.Content?.GetType() == pageType);
            var page = (Control)frame.Content!;
            Assert.Multiple(() =>
            {
                Assert.That(nav.SelectedItem, Is.SameAs(item));
                Assert.That(item.IconSource, Is.Not.Null);
                Assert.That(item.Content, Is.Not.Empty);
                Assert.That(page.DataContext?.GetType().Name, Is.EqualTo(pageType.Name + "ViewModel"));
            });
            if (page is InformationPage)
            {
                string[] headers = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                    .Select(row => row.Header).OfType<string>().ToArray();
                Assert.That(headers, Does.Contain(SettingsStrings.AutoDownloadUpdates));
                Assert.That(headers, Does.Not.Contain(SettingsStrings.Telemetry));
            }

            if (HeadlessCapture.IsRequested)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => !page.IsAnimating(Visual.OpacityProperty));
                HeadlessTestHelpers.Render(5);
                HeadlessCapture.SaveIfRequested(dialog, "settings-split", imageName);
                if (page is PreviewSettingsPage or PerformanceSettingsPage)
                {
                    page.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    HeadlessTestHelpers.Render(3);
                    HeadlessCapture.SaveIfRequested(dialog, "settings-split", page is PreviewSettingsPage
                        ? "preview-guides" : "performance-proxies");
                }
            }
        }
        finally
        {
            dialog.Close();
            viewModel.Dispose();
            (telemetry.Beutl_Application.Value, telemetry.Beutl_PackageManagement.Value,
                telemetry.Beutl_Api_Client.Value, telemetry.Beutl_Logging.Value) = previousTelemetry;
            configuration.GraphicsConfig.SelectedGpuName.Value = previousGpu;
        }
    }

    [AvaloniaTest]
    public void Constructor_initializes_navigation_without_accessing_uninitialized_controls()
    {
        SettingsDialog? dialog = null;
        try
        {
            dialog = HeadlessAppWindow.Create(() => new SettingsDialog());
            Assert.Multiple(() =>
            {
                Assert.That(dialog.FindControl<FANavigationView>("nav"), Is.Not.Null);
                Assert.That(dialog.FindControl<FAFrame>("frame"), Is.Not.Null);
            });
        }
        finally
        {
            dialog?.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(true, false, TestName = "Account_requested_before_ShowDialog_is_rendered_on_first_open")]
    [TestCase(true, true, TestName = "Latest_request_before_ShowDialog_wins_without_an_extra_back_entry")]
    [TestCase(false, false, TestName = "Opening_without_a_request_renders_the_default_account_page")]
    public async Task Initial_page_follows_the_requests_made_before_ShowDialog(bool requestAccount, bool requestInformation)
    {
        await TestReset.ResetShellAsync();
        using SettingsDialogViewModel viewModel = TestShell.MainViewModel.CreateSettingsDialog();
        var dialog = HeadlessAppWindow.Create(() => new SettingsDialog());
        dialog.DataContext = viewModel;
        dialog.Width = 800;
        dialog.Height = 600;
        var owner = new Window();
        FAFrame frame = dialog.FindControl<FAFrame>("frame")!;

        try
        {
            // Match App.OpenSettingsClicked: set the DataContext, request navigation, then show.
            if (requestAccount)
                viewModel.GoToAccountSettingsPage();
            if (requestInformation)
                viewModel.GoToSettingsPage();

            owner.Show();
            Task closed = dialog.ShowDialog(owner);
            HeadlessTestHelpers.Render(3);

            Type expectedPage = requestInformation ? typeof(InformationPage) : typeof(AccountSettingsPage);
            object expectedContext = requestInformation ? viewModel.Information : viewModel.Account;
            Assert.Multiple(() =>
            {
                Assert.That(frame.Content, Is.TypeOf(expectedPage));
                Assert.That(((Control)frame.Content!).DataContext, Is.SameAs(expectedContext));
                Assert.That(dialog.GetVisualDescendants().Any(control => control.GetType() == expectedPage), Is.True);
                Assert.That(frame.BackStack, Is.Empty);
            });

            if (requestAccount && !requestInformation)
            {
                viewModel.GoToSettingsPage();
                HeadlessTestHelpers.Render(3);
                Assert.Multiple(() =>
                {
                    Assert.That(frame.Content, Is.TypeOf<InformationPage>());
                    Assert.That(frame.BackStack, Has.Count.EqualTo(1));
                });
            }

            // Closing clears navigation after initialization and completes the modal lifetime.
            dialog.Close();
            await closed;
            Assert.Multiple(() =>
            {
                Assert.That(frame.BackStack, Is.Empty);
                Assert.That(frame.ForwardStack, Is.Empty);
            });
        }
        finally
        {
            dialog.Close();
            owner.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
