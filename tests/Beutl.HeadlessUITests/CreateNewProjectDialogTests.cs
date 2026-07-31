using System.Reactive.Linq;

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
    public async Task Track_history_uses_the_configured_default_and_is_present_in_the_dialog()
    {
        await TestReset.ResetShellAsync();
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
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Enable_version_control_command_is_gated_by_the_open_project_state_and_mapped_as_a_context_command()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousPath = config.GitExecutablePath;
        try
        {
            GitAvailability detected = await TestShell.VersionControl.GetAvailabilityAsync();
            if (detected is not { State: GitAvailabilityState.Installed, GitPath: { } gitPath })
            {
                Assert.Ignore("git is not available on this machine.");
                return;
            }

            config.GitExecutablePath = gitPath;
            GitAvailability configured = await TestShell.VersionControl.GetAvailabilityAsync();
            Assert.That(configured.State, Is.EqualTo(GitAvailabilityState.Installed));

            var command = TestShell.MainViewModel.MenuBar.EnableVersionControl;
            Assert.That(((System.Windows.Input.ICommand)command).CanExecute(null), Is.False);

            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "command-gating");
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
        }
        finally
        {
            config.GitExecutablePath = previousPath;
            await TestShell.VersionControl.GetAvailabilityAsync();
            await TestReset.ResetShellAsync();
        }
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

    [AvaloniaTest]
    public async Task Tracking_default_is_applied_only_after_git_availability_is_visible()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool previousDefault = config.EnableForNewProjects;
        var availabilitySource = new TaskCompletionSource<GitAvailability>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            config.EnableForNewProjects = true;
            var initializer = new TestVersionControlInitializer(
                _ => availabilitySource.Task,
                (_, _) => Task.FromResult(true));
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                initializer,
                _ => Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Headless Test", "headless@example.invalid")));

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsGitAvailable.Value, Is.False);
                Assert.That(viewModel.TrackHistory.Value, Is.False);
            });

            availabilitySource.SetResult(new GitAvailability(
                GitAvailabilityState.Installed,
                "git",
                new Version(2, 50, 0),
                LfsInstalled: false));
            await WaitUntilAsync(() => viewModel.IsGitAvailable.Value);

            Assert.That(viewModel.TrackHistory.Value, Is.True);
        }
        finally
        {
            config.EnableForNewProjects = previousDefault;
            availabilitySource.TrySetResult(GitAvailability.NotInstalled);
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Creation_during_git_detection_does_not_enable_tracking_without_visible_opt_in()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool previousDefault = config.EnableForNewProjects;
        var availabilitySource = new TaskCompletionSource<GitAvailability>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int initializationRequests = 0;
        Task? createTask = null;
        try
        {
            config.EnableForNewProjects = true;
            var initializer = new TestVersionControlInitializer(
                _ => availabilitySource.Task,
                (_, _) =>
                {
                    Interlocked.Increment(ref initializationRequests);
                    return Task.FromResult(true);
                });
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                initializer,
                _ => Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Headless Test", "headless@example.invalid")));
            var availabilityPublished = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable availabilitySubscription = viewModel.IsGitAvailable
                .Where(static value => value)
                .Take(1)
                .Subscribe(_ => availabilityPublished.TrySetResult());
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "create-detection-race");
            Directory.CreateDirectory(location);
            viewModel.Location.Value = location;
            viewModel.Name.Value = "untracked-project";
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.CanCreate.Value, Is.True);
                Assert.That(viewModel.IsGitAvailable.Value, Is.False);
                Assert.That(viewModel.TrackHistory.Value, Is.False);
            });

            createTask = viewModel.Create.ExecuteAsync();
            await WaitUntilAsync(() => TestShell.Project.CurrentProject.Value is not null);
            availabilitySource.SetResult(new GitAvailability(
                GitAvailabilityState.Installed,
                "git",
                new Version(2, 50, 0),
                LfsInstalled: false));
            await createTask.WaitAsync(TimeSpan.FromSeconds(10));
            await availabilityPublished.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsGitAvailable.Value, Is.True);
                Assert.That(viewModel.TrackHistory.Value, Is.True);
                Assert.That(Volatile.Read(ref initializationRequests), Is.Zero);
            });
        }
        finally
        {
            config.EnableForNewProjects = previousDefault;
            availabilitySource.TrySetResult(GitAvailability.NotInstalled);
            try
            {
                if (createTask is not null)
                {
                    await createTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                await TestReset.ResetShellAsync();
            }
        }
    }

    [AvaloniaTest]
    public async Task Git_detection_failure_does_not_fail_project_creation()
    {
        await TestReset.ResetShellAsync();
        int initializationRequests = 0;
        try
        {
            var initializer = new TestVersionControlInitializer(
                _ => Task.FromException<GitAvailability>(
                    new IOException("simulated Git probe failure")),
                (_, _) =>
                {
                    Interlocked.Increment(ref initializationRequests);
                    return Task.FromResult(true);
                });
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                initializer,
                _ => Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Headless Test", "headless@example.invalid")));
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "create-detection-failure");
            Directory.CreateDirectory(location);
            viewModel.Location.Value = location;
            viewModel.Name.Value = "untracked-project";
            viewModel.TrackHistory.Value = true;
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();

            await viewModel.Create.ExecuteAsync();
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(viewModel.IsGitAvailable.Value, Is.False);
                Assert.That(Volatile.Read(ref initializationRequests), Is.Zero);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TestVersionControlInitializer(
        Func<CancellationToken, Task<GitAvailability>> getAvailabilityAsync,
        Func<
            Func<CancellationToken, Task<GitIdentity?>>,
            CancellationToken,
            Task<bool>> initializeCurrentProjectAsync)
        : IProjectVersionControlInitializer
    {
        public Task<GitAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            return getAvailabilityAsync(cancellationToken);
        }

        public Task<bool> InitializeCurrentProjectAsync(
            Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync,
            CancellationToken cancellationToken)
        {
            return initializeCurrentProjectAsync(requestIdentityAsync, cancellationToken);
        }
    }
}
