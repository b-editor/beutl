using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.Components.VersionControlTab.Views;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.Views;
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
                async service =>
                {
                    await service.SetLocalIdentityAsync(
                        new GitIdentity(
                            "Beutl Headless Test",
                            "headless@example.invalid"),
                        CancellationToken.None);
                    return true;
                });
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
            Assert.That(
                viewModel.Branches.Select(branch => branch.Name),
                Does.Not.Contain("flyout-refresh"));

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
                async service =>
                {
                    await service.SetLocalIdentityAsync(
                        new GitIdentity(
                            "Beutl Headless Test",
                            "headless@example.invalid"),
                        CancellationToken.None);
                    return true;
                });
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
                async service =>
                {
                    await service.SetLocalIdentityAsync(
                        new GitIdentity(
                            "Beutl Headless Test",
                            "headless@example.invalid"),
                        CancellationToken.None);
                    return true;
                });
            Assert.That(initialized, Is.True);
            await WaitUntilAsync(
                () => viewModel.IsTracked.Value && viewModel.Commits.Count > 0);

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

            commitMessageTextBox.Focus();
            viewModel.CommitMessage.Value = "first line";
            commitMessageTextBox.CaretIndex = commitMessageTextBox.Text!.Length;
            viewModel.StatusMessage.Value = "unchanged";
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.CommitMessage.Value, Does.Contain('\n'));
                Assert.That(viewModel.StatusMessage.Value, Is.EqualTo("unchanged"));
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
            VersionControlCommitViewModel selectedCommit = viewModel.Commits[0];
            commitList.SelectedItem = selectedCommit;
            await WaitUntilAsync(() => viewModel.ShowingDetail.Value);
            await WaitUntilAsync(() => viewModel.ChangedFiles.Count > 0);
            HeadlessTestHelpers.Render();

            ListBox changedFileList =
                narrowChanges.FindControl<ListBox>("ChangedFileList")!;
            var changedFileItem = (ListBoxItem)changedFileList.ContainerFromIndex(0)!;
            VersionControlFileChangeViewModel changedFile = viewModel.ChangedFiles[0];
            TextBlock changeStatusText = changedFileItem
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == changedFile.StatusText);
            TextBlock changePathText = changedFileItem
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == changedFile.PathText);
            Assert.Multiple(() =>
            {
                Assert.That(narrowHistory.IsVisible, Is.False);
                Assert.That(narrowDetail.IsVisible, Is.True);
                Assert.That(viewModel.SelectedCommit.Value, Is.SameAs(selectedCommit));
                Assert.That(wideCommitList.SelectedItem, Is.SameAs(selectedCommit));
                Assert.That(changeStatusText.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
                Assert.That(changePathText.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
                Assert.That(
                    narrowChanges.FindControl<WrapPanel>("SelectedCommitActionBar")!.IsVisible,
                    Is.True);
                Assert.That(
                    detailHeader.FindControl<Button>("BackButton")!.IsVisible,
                    Is.True);
            });

            detailHeader.FindControl<Button>("BackButton")!.Command!.Execute(null);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ShowingDetail.Value, Is.False);
                Assert.That(narrowHistory.IsVisible, Is.True);
                Assert.That(narrowDetail.IsVisible, Is.False);
                Assert.That(viewModel.SelectedCommit.Value, Is.SameAs(selectedCommit));
                Assert.That(commitList.SelectedItem, Is.SameAs(selectedCommit));
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

            Assert.Multiple(() =>
            {
                Assert.That(view.IsNarrowLayout, Is.False);
                Assert.That(wideLayout.IsVisible, Is.True);
                Assert.That(narrowLayout.IsVisible, Is.False);
                Assert.That(wideCommitList.SelectedItem, Is.SameAs(selectedCommit));
                Assert.That(
                    wideChanges.FindControl<WrapPanel>("SelectedCommitActionBar")!.IsVisible,
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
