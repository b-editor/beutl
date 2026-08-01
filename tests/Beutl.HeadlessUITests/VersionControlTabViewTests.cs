using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Components.VersionControl.Views;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.Components.VersionControlTab.Views;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlTabViewTests
{
    [AvaloniaTest]
    public async Task Title_bar_branch_widget_is_hidden_until_a_tracked_project_is_ready()
    {
        await TestReset.ResetShellAsync();
        using var gitEnvironment = new IsolatedGitEnvironment();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousGitPath = config.GitExecutablePath;
        Func<string, CancellationToken, Task<bool>> previousBranchConfirmation =
            TestShell.VersionControl.ConfirmSwitchBranchAsync;
        var window = new Window { Width = 420, Height = 120 };
        try
        {
            config.GitExecutablePath = ProbeGitOrIgnore();
            TitleBarBranchViewModel viewModel =
                TestShell.MainViewModel.TitleBarBranch;
            var view = new TitleBarBranchView { DataContext = viewModel };
            window.Content = view;
            window.Show();
            await viewModel.Initialization;
            HeadlessTestHelpers.Render();

            Assert.That(view.IsVisible, Is.False);

            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "title-bar-branch-widget");
            Directory.CreateDirectory(location);
            await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "title-bar-branch-widget",
                location);
            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                    "Beutl Headless Test",
                    "headless@example.invalid")));
            Assert.That(initialized, Is.True);
            await WaitUntilAsync(() => viewModel.IsVisible.Value);

            TestShell.VersionControl.ConfirmSwitchBranchAsync =
                static (_, _) => Task.FromResult(true);
            bool branchCreated = await TestShell.VersionControl.CreateBranchAsync(
                "feature",
                CancellationToken.None);
            Assert.That(branchCreated, Is.True);
            await WaitUntilAsync(() =>
                viewModel.IsVisible.Value
                && viewModel.DisplayText.Value.StartsWith(
                    "feature",
                    StringComparison.Ordinal));
            HeadlessTestHelpers.Render();

            Button branchButton =
                view.FindControl<Button>("TitleBarBranchButton")!;
            Assert.Multiple(() =>
            {
                Assert.That(view.IsVisible, Is.True);
                Assert.That(branchButton.IsVisible, Is.True);
                Assert.That(branchButton.Bounds.Width, Is.GreaterThan(32));
                Assert.That(viewModel.DisplayText.Value, Does.StartWith("feature"));
            });

            IProjectVersionControlService service =
                TestShell.VersionControl.CurrentService!;
            RunGit(
                config.GitExecutablePath!,
                service.Repository!.RepoRoot,
                "branch",
                "flyout-refresh");

            branchButton.Flyout!.ShowAt(branchButton);
            await WaitUntilAsync(() => viewModel.Branches.Count == 3);
            HeadlessTestHelpers.Render();

            ItemsControl branchList =
                view.FindControl<ItemsControl>("BranchList")!;
            int currentIndex = viewModel.Branches
                .Select((branch, index) => (branch, index))
                .Single(item => item.branch.IsCurrent)
                .index;
            Control currentContainer =
                (Control)branchList.ContainerFromIndex(currentIndex)!;
            FluentIcon currentMark = currentContainer
                .GetVisualDescendants()
                .OfType<FluentIcon>()
                .Single(icon => icon.Name == "CurrentBranchMark");

            Assert.Multiple(() =>
            {
                Assert.That(branchList.Items, Has.Count.EqualTo(3));
                Assert.That(
                    viewModel.Branches.Select(branch => branch.Name),
                    Does.Contain("feature"));
                Assert.That(
                    viewModel.Branches.Select(branch => branch.Name),
                    Does.Contain("flyout-refresh"));
                Assert.That(currentMark.Icon.ToString(), Is.EqualTo("Checkmark"));
                Assert.That(currentMark.IsVisible, Is.True);
            });

            Task<string?> branchNameTask = viewModel.RequestNewBranchNameAsync();
            HeadlessTestHelpers.Render();

            VersionControlPickerFlyout branchPrompt = view.PromptFlyout;
            Button acceptButton = GetPickerButton(branchPrompt, "AcceptButton");
            Assert.Multiple(() =>
            {
                Assert.That(branchButton.Flyout.IsOpen, Is.False);
                Assert.That(branchPrompt.IsOpen, Is.True);
                Assert.That(branchPrompt.Target, Is.SameAs(branchButton));
                Assert.That(branchPrompt.Presenter, Is.TypeOf<PickerFlyoutPresenter>());
                Assert.That(branchPrompt.Presenter!.Width, Is.EqualTo(320));
                Assert.That(branchPrompt.Presenter.Padding, Is.EqualTo(new Thickness(8, 4)));
                Assert.That(
                    branchPrompt.TitleTextBlock.Text,
                    Is.EqualTo(Strings.VersionControl_NewBranch));
                Assert.That(
                    branchPrompt.PrimaryTextBox.Watermark,
                    Is.EqualTo(Strings.VersionControl_BranchName));
                Assert.That(branchPrompt.PrimaryTextBox.Text, Is.Null);
                Assert.That(branchPrompt.MessageTextBlock.IsVisible, Is.False);
            });

            acceptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Multiple(() =>
            {
                Assert.That(branchPrompt.IsOpen, Is.True);
                Assert.That(branchNameTask.IsCompleted, Is.False);
            });

            branchPrompt.PrimaryTextBox.Text = "headless-branch";
            acceptButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            string? requestedBranchName = await branchNameTask;

            Assert.Multiple(() =>
            {
                Assert.That(requestedBranchName, Is.EqualTo("headless-branch"));
                Assert.That(branchPrompt.IsOpen, Is.False);
            });
        }
        finally
        {
            window.Close();
            config.GitExecutablePath = previousGitPath;
            TestShell.VersionControl.ConfirmSwitchBranchAsync =
                previousBranchConfirmation;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Title_bar_branch_handlers_surface_async_failures_as_notifications()
    {
        await TestReset.ResetShellAsync();
        using var gitEnvironment = new IsolatedGitEnvironment();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousGitPath = config.GitExecutablePath;
        INotificationServiceHandler previousNotificationHandler =
            NotificationService.Handler;
        var notifications = new CaptureNotificationHandler();
        var window = new Window { Width = 420, Height = 120 };
        string? gitDirectory = null;
        string? disabledGitDirectory = null;

        try
        {
            config.GitExecutablePath = ProbeGitOrIgnore();
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "title-bar-branch-handler-errors");
            Directory.CreateDirectory(location);
            await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "title-bar-branch-handler-errors",
                location);
            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                    "Beutl Headless Test",
                    "headless@example.invalid")));
            Assert.That(initialized, Is.True);

            TitleBarBranchViewModel viewModel =
                TestShell.MainViewModel.TitleBarBranch;
            var view = new TitleBarBranchView { DataContext = viewModel };
            window.Content = view;
            window.Show();
            await WaitUntilAsync(() => viewModel.IsVisible.Value);

            IProjectVersionControlService service =
                TestShell.VersionControl.CurrentService!;
            gitDirectory = Path.Combine(service.Repository!.RepoRoot, ".git");
            disabledGitDirectory = Path.Combine(
                service.Repository.RepoRoot,
                ".git-disabled-for-handler-test");
            Directory.Move(gitDirectory, disabledGitDirectory);
            NotificationService.Handler = notifications;

            await view.HandleBranchFlyoutOpeningAsync();
            using var invalidBranch = new TitleBarBranchItemViewModel(
                new BranchInfo(string.Empty, false, null),
                viewModel.IsBusy);
            var invalidBranchButton = new Button { DataContext = invalidBranch };
            await view.HandleBranchClickAsync(invalidBranchButton);

            Assert.Multiple(() =>
            {
                Assert.That(notifications.Notifications, Has.Count.EqualTo(2));
                Assert.That(
                    notifications.Notifications.All(notification =>
                        notification.Type == NotificationType.Error),
                    Is.True);
            });
        }
        finally
        {
            NotificationService.Handler = previousNotificationHandler;
            if (gitDirectory is not null
                && disabledGitDirectory is not null
                && Directory.Exists(disabledGitDirectory)
                && !Directory.Exists(gitDirectory))
            {
                Directory.Move(disabledGitDirectory, gitDirectory);
            }

            window.Close();
            config.GitExecutablePath = previousGitPath;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Restored_tab_observes_the_service_published_after_view_creation()
    {
        await TestReset.ResetShellAsync();
        using var gitEnvironment = new IsolatedGitEnvironment();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousGitPath = config.GitExecutablePath;
        var window = new Window { Width = 900, Height = 700 };
        try
        {
            config.GitExecutablePath = ProbeGitOrIgnore();
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-tab-startup-race");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "tracked-before-tab",
                location))!;
            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                    "Beutl Headless Test",
                    "headless@example.invalid")));
            Assert.That(initialized, Is.True);

            IProjectVersionControlService readyService =
                TestShell.VersionControl.CurrentService!;
            TestShell.Editor.PublishProjectVersionControlService(null);
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            IEditorContext editorContext =
                TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.That(
                VersionControlTabExtension.Instance.TryCreateContext(
                    editorContext,
                    out IToolContext? context),
                Is.True);
            using var viewModel = (VersionControlTabViewModel)context!;
            var view = new VersionControlTabView { DataContext = viewModel };
            window.Content = view;
            window.Show();
            await viewModel.Initialization;
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsTracked.Value, Is.False);
                Assert.That(
                    view.FindControl<Border>("UntrackedProjectPanel")!.IsVisible,
                    Is.True);
            });

            TestShell.Editor.PublishProjectVersionControlService(readyService);
            await WaitUntilAsync(
                () => viewModel.IsTracked.Value && viewModel.Commits.Count > 0);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(window.Content, Is.SameAs(view));
                Assert.That(viewModel.IsTracked.Value, Is.True);
                Assert.That(viewModel.Commits, Is.Not.Empty);
                Assert.That(
                    view.FindControl<Border>("UntrackedProjectPanel")!.IsVisible,
                    Is.False);
                Assert.That(
                    view.FindControl<Grid>("WideLayoutRoot")!.IsVisible,
                    Is.True);
            });
        }
        finally
        {
            window.Close();
            config.GitExecutablePath = previousGitPath;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Adaptive_layout_supports_onboarding_wide_and_narrow_drill_down()
    {
        await TestReset.ResetShellAsync();
        using var gitEnvironment = new IsolatedGitEnvironment();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? previousGitPath = config.GitExecutablePath;
        var window = new Window { Width = 900, Height = 700 };
        try
        {
            config.GitExecutablePath = ProbeGitOrIgnore();
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-tab-view");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "untracked",
                location))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            IEditorContext editorContext = TestShell.Editor.SelectedTabItem.Value!.Context.Value;

            Assert.That(
                VersionControlTabExtension.Instance.TryCreateContext(
                    editorContext,
                    out IToolContext? context),
                Is.True);
            using var viewModel = (VersionControlTabViewModel)context!;
            var view = new VersionControlTabView { DataContext = viewModel };
            var handler = new RecordingCommandHandler();
            window.DataContext = handler;
            window.Content = view;

            await viewModel.Initialization;
            window.Show();
            HeadlessTestHelpers.Render();

            Button enableButton = view.FindControl<Button>("EnableVersionControlButton")!;
            Assert.Multiple(() =>
            {
                Assert.That(
                    view.FindControl<Border>("UntrackedProjectPanel")!.IsVisible,
                    Is.True);
                Assert.That(enableButton.IsVisible, Is.True);
                Assert.That(
                    view.FindControl<HyperlinkButton>("DownloadGitButton")!.IsVisible,
                    Is.False);
            });

            enableButton.Command!.Execute(null);
            await Task.Yield();
            HeadlessTestHelpers.Settle();
            Assert.That(handler.LastExecution?.CommandName, Is.EqualTo("EnableVersionControl"));

            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                    "Beutl Headless Test",
                    "headless@example.invalid")));
            Assert.That(initialized, Is.True);
            await WaitUntilAsync(
                () => viewModel.IsTracked.Value && viewModel.Commits.Count > 0);
            await viewModel.Initialization;
            await WaitUntilAsync(
                () => !viewModel.IsLoading.Value && viewModel.Commits.Count > 0);
            IProjectVersionControlService trackedService =
                TestShell.VersionControl.CurrentService!;
            string requestedSha = (await trackedService.GetHistoryAsync(
                    0,
                    1,
                    CancellationToken.None))
                .Single()
                .Sha;

            SplitButton primaryAction =
                view.FindControl<SplitButton>("PrimaryActionSplitButton")!;
            TextBox commitMessageTextBox =
                view.FindControl<TextBox>("CommitMessageTextBox")!;
            Grid commitComposer =
                view.FindControl<Grid>("CommitComposer")!;
            var primaryActionFlyout = (MenuFlyout)primaryAction.Flyout!;
            Assert.Multiple(() =>
            {
                Assert.That(
                    primaryAction.Content,
                    Is.EqualTo(Strings.VersionControl_PublishBranch));
                Assert.That(primaryAction.IsEnabled, Is.True);
                Assert.That(commitMessageTextBox.AcceptsReturn, Is.True);
                Assert.That(commitMessageTextBox.MinLines, Is.EqualTo(3));
                Assert.That(commitMessageTextBox.MaxLines, Is.EqualTo(6));
                Assert.That(commitMessageTextBox.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
                Assert.That(commitComposer.Parent, Is.TypeOf<Grid>());
                Assert.That(primaryActionFlyout.Placement.ToString(), Is.EqualTo("Pointer"));
                Assert.That(
                    primaryActionFlyout.Items,
                    Has.Count.EqualTo(5));
            });

            Task<string?> remoteUrlTask =
                viewModel.RequestRemoteUrlAsync("https://example.invalid/old.git");
            HeadlessTestHelpers.Render();

            VersionControlPickerFlyout tabPrompt = view.PromptFlyout;
            Button acceptButton = GetPickerButton(tabPrompt, "AcceptButton");
            Button dismissButton = GetPickerButton(tabPrompt, "DismissButton");
            Assert.Multiple(() =>
            {
                Assert.That(tabPrompt.IsOpen, Is.True);
                Assert.That(tabPrompt.Target, Is.SameAs(primaryAction));
                Assert.That(
                    tabPrompt.TitleTextBlock.Text,
                    Is.EqualTo(Strings.VersionControl_SetRemoteTitle));
                Assert.That(
                    tabPrompt.PrimaryTextBox.Text,
                    Is.EqualTo("https://example.invalid/old.git"));
                Assert.That(tabPrompt.PrimaryTextBox.IsVisible, Is.True);
                Assert.That(tabPrompt.Presenter, Is.TypeOf<PickerFlyoutPresenter>());
                Assert.That(acceptButton.IsVisible, Is.True);
                Assert.That(dismissButton.IsVisible, Is.True);
            });

            tabPrompt.PrimaryTextBox.Text = "https://example.invalid/new.git";
            acceptButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Assert.That(
                await remoteUrlTask,
                Is.EqualTo("https://example.invalid/new.git"));
            await WaitUntilAsync(
                () => !viewModel.IsLoading.Value && viewModel.Commits.Count > 0);

            CommitInfo selectedCommitInfo = viewModel.Commits[0].Commit;
            Task<string?> branchNameTask =
                viewModel.RequestBranchNameAsync(selectedCommitInfo);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(tabPrompt.IsOpen, Is.True);
                Assert.That(
                    tabPrompt.TitleTextBlock.Text,
                    Is.EqualTo(Strings.VersionControl_CreateBranchTitle));
                Assert.That(
                    tabPrompt.PrimaryTextBox.Text,
                    Is.EqualTo($"restore-{selectedCommitInfo.ShortSha}"));
                Assert.That(tabPrompt.MessageTextBlock.IsVisible, Is.False);
            });

            dismissButton = GetPickerButton(tabPrompt, "DismissButton");
            dismissButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await branchNameTask, Is.Null);

            string longWarning = string.Concat(
                Enumerable.Repeat("LongWarningText", 20));
            Task<bool> confirmationTask = tabPrompt.ShowConfirmationAsync(
                primaryAction,
                Strings.VersionControl_Pull,
                longWarning);
            HeadlessTestHelpers.Render();
            PickerFlyoutPresenter presenter = tabPrompt.Presenter!;
            ScrollViewer presenterScrollViewer = presenter
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single(scrollViewer => scrollViewer.Name == "ScrollViewer");
            Assert.Multiple(() =>
            {
                Assert.That(tabPrompt.IsOpen, Is.True);
                Assert.That(tabPrompt.MessageTextBlock.IsVisible, Is.True);
                Assert.That(
                    tabPrompt.MessageTextBlock.Text,
                    Is.EqualTo(longWarning));
                Assert.That(
                    tabPrompt.TitleTextBlock.TextWrapping,
                    Is.EqualTo(TextWrapping.Wrap));
                Assert.That(
                    tabPrompt.MessageTextBlock.TextWrapping,
                    Is.EqualTo(TextWrapping.Wrap));
                Assert.That(
                    tabPrompt.MessageTextBlock.Bounds.Width,
                    Is.LessThanOrEqualTo(304));
                Assert.That(
                    tabPrompt.MessageTextBlock.Bounds.Height,
                    Is.GreaterThan(30));
                Assert.That(
                    ScrollViewer.GetHorizontalScrollBarVisibility(presenter),
                    Is.EqualTo(ScrollBarVisibility.Disabled));
                Assert.That(
                    presenterScrollViewer.Extent.Width,
                    Is.LessThanOrEqualTo(presenterScrollViewer.Viewport.Width));
                Assert.That(tabPrompt.PrimaryTextBox.IsVisible, Is.False);
                Assert.That(tabPrompt.SecondaryTextBox.IsVisible, Is.False);
            });
            GetPickerButton(tabPrompt, "AcceptButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await confirmationTask, Is.True);

            Task<VersionControlIdentityInput?> identityTask =
                tabPrompt.ShowIdentityAsync(
                    primaryAction,
                    Strings.VersionControl_IdentityTitle,
                    Strings.VersionControl_IdentityName,
                    Strings.VersionControl_IdentityEmail,
                    "Headless User",
                    "headless@example.invalid",
                    CancellationToken.None);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(
                    tabPrompt.PrimaryLabelTextBlock.Text,
                    Is.EqualTo(Strings.VersionControl_IdentityName));
                Assert.That(
                    tabPrompt.SecondaryLabelTextBlock.Text,
                    Is.EqualTo(Strings.VersionControl_IdentityEmail));
                Assert.That(tabPrompt.PrimaryTextBox.Text, Is.EqualTo("Headless User"));
                Assert.That(
                    tabPrompt.SecondaryTextBox.Text,
                    Is.EqualTo("headless@example.invalid"));
            });
            GetPickerButton(tabPrompt, "AcceptButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(
                await identityTask,
                Is.EqualTo(
                    new VersionControlIdentityInput(
                        "Headless User",
                        "headless@example.invalid")));

            INotificationServiceHandler previousNotificationHandler =
                NotificationService.Handler;
            var notificationHandler = new CaptureNotificationHandler();
            try
            {
                NotificationService.Handler = notificationHandler;
                await viewModel.ShowRemoteResultAsync(new RemoteOpResult.Offline());
            }
            finally
            {
                NotificationService.Handler = previousNotificationHandler;
            }

            Notification notification = notificationHandler.Notifications.Single();
            Assert.Multiple(() =>
            {
                Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
                Assert.That(
                    notification.Title,
                    Is.EqualTo(Strings.VersionControl_ErrorTitle));
                Assert.That(
                    notification.Message,
                    Is.EqualTo(Strings.VersionControl_Offline));
                Assert.That(tabPrompt.IsOpen, Is.False);
            });

            Grid wideLayout = view.FindControl<Grid>("WideLayoutRoot")!;
            Grid narrowLayout = view.FindControl<Grid>("NarrowLayoutRoot")!;
            UserControl wideHistory =
                view.FindControl<UserControl>("WideHistoryView")!;
            UserControl narrowHistory =
                view.FindControl<UserControl>("NarrowHistoryRoot")!;
            UserControl wideChanges =
                view.FindControl<UserControl>("WideChangesView")!;
            UserControl narrowChanges =
                view.FindControl<UserControl>("NarrowChangesView")!;
            UserControl detailHeader =
                view.FindControl<UserControl>("NarrowDetailHeader")!;

            Assert.Multiple(() =>
            {
                Assert.That(view.IsNarrowLayout, Is.False);
                Assert.That(wideLayout.IsVisible, Is.True);
                Assert.That(narrowLayout.IsVisible, Is.False);
                Assert.That(
                    wideHistory.FindControl<TextBlock>("HistoryEmptyHint")!.Text,
                    Is.EqualTo(
                        narrowHistory.FindControl<TextBlock>("HistoryEmptyHint")!.Text));
                Assert.That(
                    wideChanges.FindControl<TextBlock>("ChangedFilesEmptyHint")!.Text,
                    Is.EqualTo(
                        narrowChanges.FindControl<TextBlock>("ChangedFilesEmptyHint")!.Text));
                Assert.That(
                    wideChanges.FindControl<TextBlock>("DiffEmptyHint")!.Text,
                    Is.EqualTo(narrowChanges.FindControl<TextBlock>("DiffEmptyHint")!.Text));
            });

            window.Width = 500;
            HeadlessTestHelpers.Render();

            Grid narrowDetail = view.FindControl<Grid>("NarrowDetailRoot")!;
            Assert.Multiple(() =>
            {
                Assert.That(view.IsNarrowLayout, Is.True);
                Assert.That(wideLayout.IsVisible, Is.False);
                Assert.That(narrowLayout.IsVisible, Is.True);
                Assert.That(narrowHistory.IsVisible, Is.True);
                Assert.That(narrowDetail.IsVisible, Is.False);
            });

            ListBox commitList = narrowHistory.FindControl<ListBox>("CommitList")!;
            ListBox wideCommitList = wideHistory.FindControl<ListBox>("CommitList")!;

            ListBox changedFileList =
                narrowChanges.FindControl<ListBox>("ChangedFileList")!;
            ScrollViewer diffScrollViewer =
                narrowChanges.FindControl<ScrollViewer>("DiffScrollViewer")!;
            VersionControlFileChangeViewModel? changedFile = null;
            ListBoxItem? changedFileItem = null;
            await WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render();
                VersionControlCommitViewModel? currentCommit = viewModel.Commits
                    .FirstOrDefault(commit => string.Equals(
                        commit.Commit.Sha,
                        requestedSha,
                        StringComparison.Ordinal));
                if (currentCommit is null)
                {
                    return false;
                }

                if (!ReferenceEquals(commitList.SelectedItem, currentCommit))
                {
                    commitList.SelectedItem = currentCommit;
                    return false;
                }

                if (!viewModel.ShowingDetail.Value
                    || !ReferenceEquals(viewModel.SelectedCommit.Value, currentCommit)
                    || viewModel.ChangedFiles.FirstOrDefault() is not { } currentFile
                    || changedFileList.ContainerFromIndex(0) is not ListBoxItem currentItem
                    || !ReferenceEquals(currentItem.DataContext, currentFile)
                    || !ReferenceEquals(commitList.SelectedItem, currentCommit)
                    || !ReferenceEquals(wideCommitList.SelectedItem, currentCommit))
                {
                    return false;
                }

                changedFile = currentFile;
                changedFileItem = currentItem;
                return true;
            });
            TextBlock changeStatusText = changedFileItem!
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == changedFile!.StatusText);
            TextBlock changePathText = changedFileItem!
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == changedFile!.PathText);
            Button restoreButton =
                narrowChanges.FindControl<Button>("RestoreButton")!;
            Button restoreToNewBranchButton =
                narrowChanges.FindControl<Button>("RestoreToNewBranchButton")!;
            Assert.Multiple(() =>
            {
                Assert.That(narrowHistory.IsVisible, Is.False);
                Assert.That(narrowDetail.IsVisible, Is.True);
                Assert.That(
                    viewModel.SelectedCommit.Value?.Commit.Sha,
                    Is.EqualTo(requestedSha));
                Assert.That(
                    wideCommitList.SelectedItem,
                    Is.SameAs(viewModel.SelectedCommit.Value));
                Assert.That(wideHistory.Margin, Is.EqualTo(default(Thickness)));
                Assert.That(wideChanges.Margin, Is.EqualTo(default(Thickness)));
                Assert.That(narrowHistory.Margin, Is.EqualTo(default(Thickness)));
                Assert.That(narrowChanges.Margin, Is.EqualTo(default(Thickness)));
                Assert.That(commitList.Padding, Is.EqualTo(new Thickness(8)));
                Assert.That(changedFileList.Padding, Is.EqualTo(new Thickness(8)));
                Assert.That(diffScrollViewer.Padding, Is.EqualTo(new Thickness(8)));
                Assert.That(changeStatusText.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
                Assert.That(changePathText.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
                Assert.That(
                    narrowChanges.FindControl<WrapPanel>("SelectedCommitActionBar")!.IsVisible,
                    Is.True);
                Assert.That(restoreButton.Command, Is.Null);
                Assert.That(restoreButton.IsEffectivelyEnabled, Is.True);
                Assert.That(restoreToNewBranchButton.Command, Is.Null);
                Assert.That(restoreToNewBranchButton.IsEffectivelyEnabled, Is.True);
                Assert.That(
                    detailHeader.FindControl<Button>("BackButton")!.IsVisible,
                    Is.True);
            });

            detailHeader.FindControl<Button>("BackButton")!.Command!.Execute(null);
            HeadlessTestHelpers.Render();
            await WaitUntilAsync(() =>
                viewModel.SelectedCommit.Value is { } currentCommit
                && string.Equals(
                    currentCommit.Commit.Sha,
                    requestedSha,
                    StringComparison.Ordinal)
                && ReferenceEquals(commitList.SelectedItem, currentCommit));

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ShowingDetail.Value, Is.False);
                Assert.That(narrowHistory.IsVisible, Is.True);
                Assert.That(narrowDetail.IsVisible, Is.False);
                Assert.That(
                    viewModel.SelectedCommit.Value?.Commit.Sha,
                    Is.EqualTo(requestedSha));
                Assert.That(
                    commitList.SelectedItem,
                    Is.SameAs(viewModel.SelectedCommit.Value));
            });

            var selectedItem = (ListBoxItem)commitList.ContainerFromIndex(0)!;
            Point selectedItemCenter = selectedItem.TranslatePoint(
                new Point(
                    selectedItem.Bounds.Width / 2,
                    selectedItem.Bounds.Height / 2),
                window)!.Value;
            window.MouseDown(selectedItemCenter, MouseButton.Left);
            window.MouseUp(selectedItemCenter, MouseButton.Left);
            await WaitUntilAsync(() => viewModel.ShowingDetail.Value);
            HeadlessTestHelpers.Render();

            Assert.That(narrowDetail.IsVisible, Is.True);

            detailHeader.FindControl<Button>("BackButton")!.Command!.Execute(null);
            HeadlessTestHelpers.Render();
            window.Width = 900;
            HeadlessTestHelpers.Render();
            await WaitUntilAsync(() =>
                viewModel.SelectedCommit.Value is { } currentCommit
                && string.Equals(
                    currentCommit.Commit.Sha,
                    requestedSha,
                    StringComparison.Ordinal)
                && ReferenceEquals(wideCommitList.SelectedItem, currentCommit));

            Assert.Multiple(() =>
            {
                Assert.That(view.IsNarrowLayout, Is.False);
                Assert.That(wideLayout.IsVisible, Is.True);
                Assert.That(narrowLayout.IsVisible, Is.False);
                Assert.That(
                    wideCommitList.SelectedItem,
                    Is.SameAs(viewModel.SelectedCommit.Value));
                Assert.That(
                    wideChanges.FindControl<WrapPanel>("SelectedCommitActionBar")!.IsVisible,
                    Is.True);
            });

            commitMessageTextBox.Focus();
            viewModel.CommitMessage.Value = "first line";
            HeadlessTestHelpers.Settle();
            commitMessageTextBox.CaretIndex = commitMessageTextBox.Text!.Length;
            await File.AppendAllTextAsync(project.Uri!.LocalPath, "\n");
            WorkspaceStatus statusBeforeEnter = await trackedService.GetStatusAsync(
                CancellationToken.None);
            string[] revisionsBeforeEnter = (await trackedService.GetHistoryAsync(
                    0,
                    VersionControlTabViewModel.HistoryPageSize,
                    CancellationToken.None))
                .Select(static commit => commit.Sha)
                .ToArray();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestHelpers.Settle();
            string[] revisionsAfterEnter = (await trackedService.GetHistoryAsync(
                    0,
                    VersionControlTabViewModel.HistoryPageSize,
                    CancellationToken.None))
                .Select(static commit => commit.Sha)
                .ToArray();
            WorkspaceStatus statusAfterEnter = await trackedService.GetStatusAsync(
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(statusBeforeEnter.IsClean, Is.False);
                Assert.That(viewModel.CommitMessage.Value, Does.Contain('\n'));
                Assert.That(revisionsAfterEnter, Is.EqualTo(revisionsBeforeEnter));
                Assert.That(statusAfterEnter.IsClean, Is.False);
            });
        }
        finally
        {
            window.Close();
            config.GitExecutablePath = previousGitPath;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Identity_prompt_cancellation_closes_flyout_and_cancels_request()
    {
        await TestReset.ResetShellAsync();
        var window = new Window { Width = 420, Height = 240 };
        var anchor = new Button { Content = "Identity" };
        var flyout = new VersionControlPickerFlyout();
        window.Content = anchor;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            using var cancellation = new CancellationTokenSource();
            Task<VersionControlIdentityInput?> request = flyout.ShowIdentityAsync(
                anchor,
                Strings.VersionControl_IdentityTitle,
                Strings.VersionControl_IdentityName,
                Strings.VersionControl_IdentityEmail,
                "Headless User",
                "headless@example.invalid",
                cancellation.Token);
            HeadlessTestHelpers.Render();

            Assert.That(flyout.IsOpen, Is.True);

            await Dispatcher.UIThread.InvokeAsync(cancellation.Cancel);
            Assert.That(flyout.IsOpen, Is.False);
            Assert.That(
                async () => await request,
                Throws.InstanceOf<OperationCanceledException>());
        }
        finally
        {
            if (flyout.IsOpen)
            {
                flyout.Hide();
            }

            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(25);
        }

        Assert.That(condition(), Is.True, "The expected UI state was not reached.");
    }

    private static string ProbeGitOrIgnore()
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--version");
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Assert.Ignore("git is not available on this machine.");
                return "git";
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Assert.Ignore("git is not available on this machine.");
            }

            return FindGitOnPath();
        }
        catch (Win32Exception)
        {
            Assert.Ignore("git is not available on this machine.");
            return "git";
        }
    }

    private static string FindGitOnPath()
    {
        string executable = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("git");
        using var process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0
            || output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() is not { } path)
        {
            Assert.Ignore("git is not available on this machine.");
            return "git";
        }

        return path;
    }

    private static void RunGit(
        string executable,
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(
            process.ExitCode,
            Is.Zero,
            $"git {string.Join(' ', arguments)} failed: {stderr}");
    }

    private sealed class RecordingCommandHandler : IContextCommandHandler
    {
        public ContextCommandExecution? LastExecution { get; private set; }

        public void Execute(ContextCommandExecution execution)
        {
            LastExecution = execution;
        }
    }

    private static Button GetPickerButton(
        VersionControlPickerFlyout flyout,
        string name)
    {
        HeadlessTestHelpers.Render();
        return flyout.Presenter!
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Name == name);
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification)
        {
            Notifications.Add(notification);
        }
    }

    private sealed class IsolatedGitEnvironment : IDisposable
    {
        private readonly string? _previousGlobal
            = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        private readonly string? _previousNoSystem
            = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");

        public IsolatedGitEnvironment()
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _previousGlobal);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", _previousNoSystem);
        }
    }
}
