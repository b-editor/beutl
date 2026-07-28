using Avalonia.Controls;
using Avalonia.Headless.NUnit;

using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.HeadlessUITests;

// Reads the logical content tree rather than showing the dialog: a ContentDialog not opened via
// ShowAsync keeps its content collapsed (close animation sets LayoutRoot IsVisible=False), so the
// Carousel pages are never realized in the visual tree. The unit suffixes are static XAML
// InnerRightContent labels, which exist in the logical tree at construction without any rendering.
[TestFixture]
public class CreateNewProjectDialogTests
{
    [AvaloniaTest]
    public void NumericInputs_show_unit_suffixes()
    {
        var vm = new CreateNewProjectViewModel(new ProjectService());
        var dialog = new CreateNewProject { DataContext = vm };

        var carousel = dialog.Content as Carousel;
        Assert.That(carousel, Is.Not.Null, "dialog should host the wizard Carousel as its content");

        // Page 0 is Name/Location; page 1 hosts the Size/FrameRate/SampleRate numeric inputs.
        var numericPage = carousel!.Items[1] as Panel;
        Assert.That(numericPage, Is.Not.Null, "the second Carousel page should host the numeric inputs");

        List<string?> units = numericPage!.Children.OfType<TextBox>()
            .Select(tb => (tb.InnerRightContent as TextBlock)?.Text)
            .ToList();

        Assert.That(units, Is.EqualTo(new[] { "px", "fps", "Hz" }),
            "Size, FrameRate and SampleRate inputs should carry their unit suffixes in order");
    }

    [AvaloniaTest]
    public void Track_history_uses_the_configured_default_and_is_present_in_the_dialog()
    {
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldValue = config.EnableForNewProjects;
        try
        {
            config.EnableForNewProjects = false;
            var vm = new CreateNewProjectViewModel(new ProjectService());
            var dialog = new CreateNewProject { DataContext = vm };
            var carousel = (Carousel)dialog.Content!;
            var optionsPage = (Panel)carousel.Items[1]!;
            CheckBox checkbox = optionsPage.Children.OfType<CheckBox>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(vm.TrackHistory.Value, Is.False);
                Assert.That(checkbox.Content, Is.EqualTo(Strings.VersionControl_TrackHistory));
            });
        }
        finally
        {
            config.EnableForNewProjects = oldValue;
        }
    }

    [AvaloniaTest]
    public async Task Enable_version_control_command_is_gated_by_the_open_project_state_and_mapped_as_a_context_command()
    {
        await TestReset.ResetShellAsync();
        var command = TestShell.MainViewModel.MenuBar.EnableVersionControl;
        Assert.That(((System.Windows.Input.ICommand)command).CanExecute(null), Is.False);

        string location = Path.Combine(Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!, "command-gating");
        Directory.CreateDirectory(location);
        await TestShell.Project.CreateProject(640, 480, 30, 44100, "project", location);
        Beutl.Testing.Headless.HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(((System.Windows.Input.ICommand)command).CanExecute(null), Is.True);
            Assert.That(
                TestShell.MainViewModel.MenuBar.FindContextCommand("EnableVersionControl"),
                Is.SameAs(command));
        });

        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public async Task Enable_version_control_command_is_disabled_when_git_is_unavailable()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousPath = config.GitExecutablePath;
        try
        {
            config.GitExecutablePath = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "missing-git");
            GitAvailability availability
                = await TestShell.VersionControl.GetAvailabilityAsync();
            Assert.That(availability.State, Is.EqualTo(GitAvailabilityState.NotInstalled));

            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "unavailable-command-gating");
            Directory.CreateDirectory(location);
            await TestShell.Project.CreateProject(640, 480, 30, 44100, "project", location);
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();

            Assert.That(
                ((System.Windows.Input.ICommand)TestShell.MainViewModel.MenuBar.EnableVersionControl)
                .CanExecute(null),
                Is.False);
        }
        finally
        {
            config.GitExecutablePath = previousPath;
            await TestShell.VersionControl.GetAvailabilityAsync();
            await TestReset.ResetShellAsync();
        }
    }
}
