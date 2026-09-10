using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
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
    public void Constructor_initializes_navigation_without_accessing_uninitialized_controls()
    {
        SettingsDialog? dialog = null;
        try
        {
            dialog = new SettingsDialog();
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
    public async Task Account_requested_before_ShowDialog_is_rendered_on_first_open()
    {
        await VerifyInitialPageAsync(requestAccount: true, requestInformation: false);
    }

    [AvaloniaTest]
    public async Task Latest_request_before_ShowDialog_wins_without_an_extra_back_entry()
    {
        await VerifyInitialPageAsync(requestAccount: true, requestInformation: true);
    }

    [AvaloniaTest]
    public async Task Opening_without_a_request_renders_the_default_account_page()
    {
        await VerifyInitialPageAsync(requestAccount: false, requestInformation: false);
    }

    private static async Task VerifyInitialPageAsync(bool requestAccount, bool requestInformation)
    {
        await TestReset.ResetShellAsync();
        using SettingsDialogViewModel viewModel = TestShell.MainViewModel.CreateSettingsDialog();
        var dialog = new SettingsDialog { DataContext = viewModel, Width = 800, Height = 600 };
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
