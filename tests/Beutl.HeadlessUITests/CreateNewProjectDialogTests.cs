using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;

using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class CreateNewProjectDialogTests
{
    // These two tests read the logical tree: a ContentDialog not opened with ShowAsync never realizes
    // its Carousel pages.
    [AvaloniaTest]
    public void NumericInputs_show_unit_suffixes()
    {
        CreateNewProjectViewModel vm = CreateViewModel(new ProjectService());
        var dialog = new CreateNewProject { DataContext = vm };

        var carousel = dialog.Content as Carousel;
        Assert.That(carousel, Is.Not.Null, "dialog should host the wizard Carousel as its content");

        // Page 0 is Name/Location; page 1 hosts the Size/FrameRate/SampleRate numeric inputs.
        var numericPage = carousel!.Items[1] as Panel;
        Assert.That(numericPage, Is.Not.Null, "the second Carousel page should host the numeric inputs");

        // The frame rate box picks a rate or takes one typed, with its unit beside it.
        List<string?> units = numericPage!.Children
            .Select(child => child switch
            {
                TextBox box => (box.InnerRightContent as TextBlock)?.Text,
                Grid grid when grid.Children.OfType<ComboBox>().Any()
                    => grid.Children.OfType<Panel>().Single().Children.OfType<TextBlock>().Single().Text,
                _ => null,
            })
            .OfType<string>()
            .ToList<string?>();

        Assert.That(units, Is.EqualTo(new[] { "px", "fps", "Hz" }),
            "Size, FrameRate and SampleRate inputs should carry their unit suffixes in order");
    }

    // A whole rate, a fraction, or an NTSC rate as its decimal, kept reduced since a whole rate such as 60/2 would be
    // written as its numerator.
    [TestCase("30", 30, 1)]
    [TestCase(" 24 ", 24, 1)]
    [TestCase("30.0", 30, 1)]
    [TestCase("29.97", 30000, 1001)]
    [TestCase("23.976", 24000, 1001)]
    [TestCase("59.94", 60000, 1001)]
    [TestCase("30000/1001", 30000, 1001)]
    [TestCase("60/2", 30, 1)]
    [TestCase("12.5", 25, 2)]
    public void The_frame_rate_box_reads_whole_fractional_and_NTSC_rates(string text, long numerator, long denominator)
    {
        Rational? rate = NewDocumentValidation.ParseFrameRate(text);

        Assert.Multiple(() =>
        {
            Assert.That(rate?.Numerator, Is.EqualTo(numerator));
            Assert.That(rate?.Denominator, Is.EqualTo(denominator));
            Assert.That(NewDocumentValidation.ValidateFrameRate(text), Is.Null);
        });
    }

    [TestCase("", true)]
    [TestCase("fps", true)]
    [TestCase("1e3", true)]
    [TestCase("0", false)]
    [TestCase("-29.97", false)]
    [TestCase("30/0", false)]
    public void The_frame_rate_box_rejects_what_is_not_a_positive_rate(string text, bool notANumber)
    {
        Assert.That(
            NewDocumentValidation.ValidateFrameRate(text),
            Is.EqualTo(notANumber ? MessageStrings.InvalidString : MessageStrings.ValueLessThanOrEqualToZero));
    }

    [AvaloniaTest]
    public async Task A_project_created_at_an_NTSC_rate_keeps_the_fraction()
    {
        await TestReset.ResetShellAsync();
        try
        {
            CreateNewProjectViewModel viewModel = CreateViewModel(TestShell.Project);
            string location = Path.Combine(Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!, "create-ntsc");
            Directory.CreateDirectory(location);
            viewModel.Location.Value = location;
            viewModel.Name.Value = "ntsc-project";
            viewModel.FrameRate.Value = "29.97";
            viewModel.ChannelLayout.Value = SpeakerLayout.Surround71;
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            Assert.That(viewModel.CanCreate.Value, Is.True);

            await viewModel.Create.ExecuteAsync();

            Project project = TestShell.Project.CurrentProject.Value!;
            System.Text.Json.Nodes.JsonNode file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(project.Uri!.LocalPath))!;
            Assert.Multiple(() =>
            {
                Assert.That(project.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround71));
                Assert.That((int?)file["ChannelLayout"], Is.EqualTo((int)SpeakerLayout.Surround71));
                Assert.That(project.FrameRate.Value, Is.EqualTo(new Rational(30000, 1001)));
                Assert.That((string?)file["FrameRate"], Is.EqualTo("30000/1001"));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public void Audio_layout_choice_is_bound_and_visible()
    {
        var vm = CreateViewModel(new ProjectService());
        var dialog = new CreateNewProject { DataContext = vm };
        var carousel = (Carousel)dialog.Content!;
        var page = (Panel)carousel.Items[1]!;
        carousel.Items.Remove(page);
        var window = new Window { Content = page, DataContext = vm, Width = 420, Height = 540 };
        try
        {
            window.Show();
            Beutl.Testing.Headless.HeadlessTestHelpers.Render();
            ComboBox choice = page.Children.OfType<ComboBox>().Single();
            Assert.That(choice.SelectedIndex, Is.EqualTo(1));
            choice.SelectedIndex = 2;
            Assert.That(vm.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
            Assert.That(choice.Bounds.Width, Is.GreaterThan(100));
            if (Environment.GetEnvironmentVariable("BEUTL_SCENE_SETTINGS_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                bitmap!.Save(Path.Combine(directory, "new-project.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void A_frame_rate_that_is_not_a_rate_blocks_creation()
    {
        CreateNewProjectViewModel viewModel = CreateViewModel(new ProjectService());
        viewModel.FrameRate.Value = "fast";
        Beutl.Testing.Headless.HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanCreate.Value, Is.False);
            Assert.That(viewModel.FrameRate.HasErrors, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Track_history_uses_the_configured_default_and_is_present_in_the_dialog()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldValue = config.EnableForNewProjects.Value;
        try
        {
            config.EnableForNewProjects.Value = false;
            CreateNewProjectViewModel vm = CreateViewModel(new ProjectService());
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
            config.EnableForNewProjects.Value = oldValue;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Enable_version_control_command_is_gated_by_the_open_project_state_and_mapped_as_a_context_command()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousPath = config.GitExecutablePath.Value;
        try
        {
            GitAvailability detected = await TestShell.VersionControl.GetAvailabilityAsync();
            if (detected is not { State: GitAvailabilityState.Installed, GitPath: { } gitPath })
            {
                Assert.Ignore("git is not available on this machine.");
                return;
            }

            config.GitExecutablePath.Value = gitPath;
            GitAvailability configured = await TestShell.VersionControl.GetAvailabilityAsync();
            Assert.That(configured.State, Is.EqualTo(GitAvailabilityState.Installed));

            var command = TestShell.MainViewModel.MenuBar.EnableVersionControl;
            Assert.That(((System.Windows.Input.ICommand)command).CanExecute(null), Is.False);

            await TestShell.CreateProjectAsync("command-gating");

            Assert.Multiple(() =>
            {
                Assert.That(((System.Windows.Input.ICommand)command).CanExecute(null), Is.True);
                Assert.That(
                    MainViewExtension.Instance.CanRun(
                        TestShell.MainViewModel, new ContextCommandExecution("main.enableVersionControl")),
                    Is.True);
            });
        }
        finally
        {
            config.GitExecutablePath.Value = previousPath;
            await TestShell.VersionControl.GetAvailabilityAsync();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Enable_version_control_command_is_disabled_when_git_is_unavailable()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousPath = config.GitExecutablePath.Value;
        try
        {
            config.GitExecutablePath.Value = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "missing-git");
            GitAvailability availability
                = await TestShell.VersionControl.GetAvailabilityAsync();
            Assert.That(availability.State, Is.EqualTo(GitAvailabilityState.NotInstalled));

            await TestShell.CreateProjectAsync("unavailable-command-gating");

            Assert.That(
                ((System.Windows.Input.ICommand)TestShell.MainViewModel.MenuBar.EnableVersionControl)
                .CanExecute(null),
                Is.False);
        }
        finally
        {
            config.GitExecutablePath.Value = previousPath;
            await TestShell.VersionControl.GetAvailabilityAsync();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Tracking_default_is_applied_only_after_git_availability_is_visible()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool previousDefault = config.EnableForNewProjects.Value;
        var availabilitySource = new TaskCompletionSource<GitAvailability>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            config.EnableForNewProjects.Value = true;
            var initializer = new TestVersionControlInitializer(
                _ => availabilitySource.Task,
                (_, _) => Task.FromResult(true));
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                initializer);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsGitAvailable.Value, Is.False);
                Assert.That(viewModel.TrackHistory.Value, Is.False);
            });

            availabilitySource.SetResult(s_installedGit);
            await Beutl.Testing.Headless.HeadlessTestHelpers.WaitUntilAsync(
                () => viewModel.IsGitAvailable.Value, TimeSpan.FromSeconds(10));

            Assert.That(viewModel.TrackHistory.Value, Is.True);
        }
        finally
        {
            config.EnableForNewProjects.Value = previousDefault;
            availabilitySource.TrySetResult(GitAvailability.NotInstalled);
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Creation_during_git_detection_does_not_enable_tracking_without_visible_opt_in()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool previousDefault = config.EnableForNewProjects.Value;
        var availabilitySource = new TaskCompletionSource<GitAvailability>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int initializationRequests = 0;
        Task? createTask = null;
        try
        {
            config.EnableForNewProjects.Value = true;
            var initializer = new TestVersionControlInitializer(
                _ => availabilitySource.Task,
                (_, _) =>
                {
                    Interlocked.Increment(ref initializationRequests);
                    return Task.FromResult(true);
                });
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                initializer);
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
            await Beutl.Testing.Headless.HeadlessTestHelpers.WaitUntilAsync(
                () => TestShell.Project.CurrentProject.Value is not null, TimeSpan.FromSeconds(10));
            availabilitySource.SetResult(s_installedGit);
            await createTask.WaitAsync(TimeSpan.FromSeconds(10));
            await availabilityPublished.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsGitAvailable.Value, Is.True);
                Assert.That(viewModel.TrackHistory.Value, Is.True);
                Assert.That(Volatile.Read(ref initializationRequests), Is.Zero);
                Assert.That(initializer.BeginCalls, Is.Zero);
            });
        }
        finally
        {
            config.EnableForNewProjects.Value = previousDefault;
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
                initializer);
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

    [AvaloniaTest]
    public async Task Version_control_initializes_the_created_project_before_its_editor_opens()
    {
        await TestReset.ResetShellAsync();
        Project? initializationTarget = null;
        Project? openProjectDuringInitialization = null;
        int tabsDuringInitialization = -1;
        bool projectFileWrittenBeforeInitialization = false;
        var initializationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? createTask = null;
        try
        {
            var initializer = new TestVersionControlInitializer(
                _ => Task.FromResult(s_installedGit),
                async (project, _) =>
                {
                    initializationTarget = project;
                    openProjectDuringInitialization = TestShell.Project.CurrentProject.Value;
                    tabsDuringInitialization = TestShell.Editor.TabItems.Count;
                    projectFileWrittenBeforeInitialization = File.Exists(project.Uri!.LocalPath);
                    initializationEntered.TrySetResult();
                    await releaseInitialization.Task;
                    return true;
                });
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                initializer);
            await Beutl.Testing.Headless.HeadlessTestHelpers.WaitUntilAsync(
                () => viewModel.IsGitAvailable.Value, TimeSpan.FromSeconds(10));
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "create-explicit-version-control-target");
            Directory.CreateDirectory(location);
            viewModel.Location.Value = location;
            viewModel.Name.Value = "created-project";
            viewModel.TrackHistory.Value = true;
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();

            createTask = viewModel.Create.ExecuteAsync();
            await initializationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(openProjectDuringInitialization, Is.Null);
                Assert.That(tabsDuringInitialization, Is.Zero);
                Assert.That(projectFileWrittenBeforeInitialization, Is.True);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(TestShell.Editor.TabItems, Is.Empty);
                Assert.That(createTask.IsCompleted, Is.False);
            });

            releaseInitialization.TrySetResult();
            await createTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(initializationTarget, Is.Not.Null);
                Assert.That(initializationTarget, Is.SameAs(TestShell.Project.CurrentProject.Value));
                Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(1));
                Assert.That(initializer.BeginCalls, Is.EqualTo(1));
                Assert.That(initializer.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseInitialization.TrySetResult();
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
    public async Task Version_control_initialization_failure_keeps_created_project_open_and_notifies_user()
    {
        await TestReset.ResetShellAsync();
        int initializationRequests = 0;
        try
        {
            using var notifications = new NotificationCapture();
            var initializer = new TestVersionControlInitializer(
                _ => Task.FromResult(s_installedGit),
                (_, _) =>
                {
                    Interlocked.Increment(ref initializationRequests);
                    return Task.FromException<bool>(
                        new IOException("simulated Git initialization failure"));
                });
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                initializer);
            await Beutl.Testing.Headless.HeadlessTestHelpers.WaitUntilAsync(
                () => viewModel.IsGitAvailable.Value, TimeSpan.FromSeconds(10));
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "create-initialization-failure");
            Directory.CreateDirectory(location);
            viewModel.Location.Value = location;
            viewModel.Name.Value = "created-project";
            viewModel.TrackHistory.Value = true;
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();

            await viewModel.Create.ExecuteAsync();

            Notification notification = notifications.All.Single();
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(
                    Path.GetFileName(TestShell.Project.CurrentProject.Value?.Uri?.LocalPath),
                    Is.EqualTo("created-project.bep"));
                Assert.That(Volatile.Read(ref initializationRequests), Is.EqualTo(1));
                Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
                Assert.That(notification.Message, Is.EqualTo("simulated Git initialization failure"));
                Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(1));
                Assert.That(initializer.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static readonly GitAvailability s_installedGit =
        new(GitAvailabilityState.Installed, "git", new Version(2, 50, 0), LfsInstalled: false);

    private static CreateNewProjectViewModel CreateViewModel(ProjectService projectService)
    {
        return new CreateNewProjectViewModel(
            projectService,
            new TestVersionControlInitializer(
                _ => Task.FromResult(GitAvailability.NotInstalled),
                (_, _) => Task.FromResult(true)));
    }

    private sealed class TestVersionControlInitializer(
        Func<CancellationToken, Task<GitAvailability>> getAvailabilityAsync,
        Func<Project, CancellationToken, Task<bool>> initializeNewProjectAsync)
        : IProjectVersionControlInitializer
    {
        private readonly Func<Project, CancellationToken, Task<bool>> _initializeNewProjectAsync =
            initializeNewProjectAsync;
        private int _beginCalls;
        private int _disposeCalls;

        public int BeginCalls => Volatile.Read(ref _beginCalls);

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        public Task<GitAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            return getAvailabilityAsync(cancellationToken);
        }

        // The dialog records the first version before the project opens, so it never initializes the
        // open project.
        public Task<bool> InitializeCurrentProjectAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "The new-project dialog must not initialize the open project.");
        }

        public INewProjectVersionControlSetup BeginNewProject()
        {
            Interlocked.Increment(ref _beginCalls);
            return new Setup(this);
        }

        private sealed class Setup(TestVersionControlInitializer owner) : INewProjectVersionControlSetup
        {
            public Task<bool> InitializeAsync(Project project, CancellationToken cancellationToken)
            {
                return owner._initializeNewProjectAsync(project, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposeCalls);
                return ValueTask.CompletedTask;
            }
        }
    }
}
