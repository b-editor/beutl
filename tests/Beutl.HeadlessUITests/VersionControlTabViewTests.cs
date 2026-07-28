using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.Components.VersionControlTab.Views;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlTabViewTests
{
    [AvaloniaTest]
    public async Task Untracked_onboarding_and_explicit_branch_controls_are_wired()
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

            var main = new BranchInfo("main", true, null);
            var alternate = new BranchInfo("alternate", false, null);
            viewModel.IsTracked.Value = true;
            viewModel.Branches.Add(main);
            viewModel.Branches.Add(alternate);
            viewModel.CurrentBranch.Value = main;
            viewModel.SelectedBranch.Value = alternate;
            viewModel.HasAhead.Value = true;
            viewModel.AheadBadgeText.Value = "↑2";
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(view.FindControl<ComboBox>("BranchComboBox")!.SelectedItem,
                    Is.EqualTo(alternate));
                Assert.That(view.FindControl<Button>("SwitchBranchButton")!.IsEnabled,
                    Is.True);
                Assert.That(view.FindControl<Border>("AheadBadge")!.IsVisible, Is.True);
                Assert.That(view.FindControl<Border>("BehindBadge")!.IsVisible, Is.False);
                Assert.That(view.FindControl<Expander>("RemoteExpander")!.IsExpanded, Is.False);
                Assert.That(view.FindControl<TextBlock>("HistoryEmptyHint")!.IsVisible, Is.True);
                Assert.That(view.FindControl<WrapPanel>("SelectedCommitActionBar")!.IsVisible,
                    Is.False);
                Assert.That(view.FindControl<TextBlock>("ChangedFilesEmptyHint")!.IsVisible,
                    Is.True);
                Assert.That(view.FindControl<TextBlock>("DiffEmptyHint")!.IsVisible, Is.True);
            });
        }
        finally
        {
            window.Close();
            config.GitExecutablePath = previousGitPath;
            await TestReset.ResetShellAsync();
        }
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
