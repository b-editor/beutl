using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlSaveTests
{
    [AvaloniaTest]
    public async Task Save_all_does_not_snapshot_partially_saved_files()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;

        try
        {
            config.GitExecutablePath = gitPath;
            config.AutoCommitOnSave = true;
            config.AutoCommitOnClose = false;
            config.UseLfsWhenAvailable = false;

            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-partial-save");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "partial-save",
                location))!;
            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                    "Beutl Headless Test",
                    "headless@example.invalid")));
            Assert.That(initialized, Is.True);

            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            int commitsBeforeSave = await CountCommitsAsync(gitPath, projectRoot);
            var failedCommands = new FailedSaveCommands();
            var failedItem = new Scene
            {
                Uri = new Uri(Path.Combine(projectRoot, "failed.scene")),
            };
            TestShell.Editor.TabItems.Add(new EditorTabItem(
                new FailedSaveEditorContext(failedItem, failedCommands)));
            project.Variables["partially-saved"] = "true";

            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

            int commitsAfterSave = await CountCommitsAsync(gitPath, projectRoot);
            int saveSnapshots = await CountSaveSnapshotsAsync(gitPath, projectRoot);
            WorkspaceStatus status = await TestShell.VersionControl.CurrentService!
                .GetStatusAsync(CancellationToken.None);
            Project persisted = CoreSerializer.RestoreFromUri<Project>(project.Uri);
            Assert.Multiple(() =>
            {
                Assert.That(failedCommands.SaveCalls, Is.EqualTo(1));
                Assert.That(persisted.Variables["partially-saved"], Is.EqualTo("true"));
                Assert.That(commitsAfterSave, Is.EqualTo(commitsBeforeSave));
                Assert.That(saveSnapshots, Is.Zero);
                Assert.That(status.IsClean, Is.False);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Explicit_save_creates_one_snapshot_and_a_second_clean_save_creates_none()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnSave = config.AutoCommitOnSave;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;

        try
        {
            config.GitExecutablePath = gitPath;
            config.AutoCommitOnSave = true;
            config.AutoCommitOnClose = true;
            config.UseLfsWhenAvailable = false;

            string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "version-control-save");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "tracked",
                location))!;
            HeadlessTestHelpers.Settle();

            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Headless Test", "headless@example.invalid")));
            Assert.That(initialized, Is.True);

            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            Assert.That(await CountCommitsAsync(gitPath, projectRoot), Is.EqualTo(1));

            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.That(
                editor.GetService(typeof(IProjectVersionControlService)),
                Is.SameAs(TestShell.VersionControl.CurrentService));

            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            adder.AddElement(new ElementDescription(
                Start: TimeSpan.Zero,
                Length: TimeSpan.FromSeconds(1),
                Layer: 0,
                EngineObjectFactory: () => new RectShape()));
            HeadlessTestHelpers.Settle();

            await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            int afterFirstSave = await CountCommitsAsync(gitPath, projectRoot);
            int saveSnapshotsAfterFirstSave = await CountSaveSnapshotsAsync(gitPath, projectRoot);

            await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            int afterSecondSave = await CountCommitsAsync(gitPath, projectRoot);
            int saveSnapshotsAfterSecondSave = await CountSaveSnapshotsAsync(gitPath, projectRoot);

            Assert.Multiple(() =>
            {
                Assert.That(afterFirstSave, Is.EqualTo(2));
                Assert.That(saveSnapshotsAfterFirstSave, Is.EqualTo(1));
                Assert.That(afterSecondSave, Is.EqualTo(2));
                Assert.That(saveSnapshotsAfterSecondSave, Is.EqualTo(1));
            });

            await File.WriteAllTextAsync(Path.Combine(projectRoot, "close-marker.txt"), "close\n");
            await TestShell.MainViewModel.MenuBar.CloseProject.ExecuteAsync();
            int afterClose = await CountCommitsAsync(gitPath, projectRoot);
            int closeSnapshots = await CountCloseSnapshotsAsync(gitPath, projectRoot);

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                Assert.That(afterClose, Is.EqualTo(3));
                Assert.That(closeSnapshots, Is.EqualTo(1));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnSave = oldAutoCommitOnSave;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Initialization_publishes_tracked_state_and_disables_the_enable_command()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldUseLfs = config.UseLfsWhenAvailable;

        try
        {
            config.GitExecutablePath = gitPath;
            config.UseLfsWhenAvailable = false;
            GitAvailability availability =
                await TestShell.VersionControl.GetAvailabilityAsync();
            Assert.That(
                availability.State,
                Is.EqualTo(GitAvailabilityState.Installed));

            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-tracked-state");
            Directory.CreateDirectory(location);
            await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "tracked-state",
                location);
            HeadlessTestHelpers.Settle();

            var enableCommand =
                (System.Windows.Input.ICommand)TestShell.MainViewModel.MenuBar.EnableVersionControl;
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.VersionControl.IsTracked.Value, Is.False);
                Assert.That(enableCommand.CanExecute(null), Is.True);
            });

            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                    "Beutl Headless Test",
                    "headless@example.invalid")));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(initialized, Is.True);
                Assert.That(TestShell.VersionControl.IsTracked.Value, Is.True);
                Assert.That(enableCommand.CanExecute(null), Is.False);
                Assert.That(
                    ((System.Windows.Input.ICommand)TestShell.MainViewModel.MenuBar.CommitVersion)
                    .CanExecute(null),
                    Is.True);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Discovered_repository_hygiene_finishes_before_tracked_service_publication()
    {
        await TestReset.ResetShellAsync();
        using var environment = new IsolatedGitEnvironment();
        string gitPath = ProbeGitOrIgnore();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGitPath = config.GitExecutablePath;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldUseLfs = config.UseLfsWhenAvailable;

        try
        {
            config.GitExecutablePath = gitPath;
            config.AutoCommitOnClose = false;
            config.UseLfsWhenAvailable = false;
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-auto-hygiene");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "auto-hygiene",
                location))!;
            bool initialized = await TestShell.VersionControl.InitializeCurrentProjectAsync(
                TestShell.Project.CurrentProject.Value!,
                _ => Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Headless Test", "headless@example.invalid")));
            Assert.That(initialized, Is.True);

            string projectFile = project.Uri!.LocalPath;
            string projectRoot = Path.GetDirectoryName(projectFile)!;
            int initialCommitCount = await CountCommitsAsync(gitPath, projectRoot);
            await TestShell.MainViewModel.MenuBar.CloseProject.ExecuteAsync();
            File.Delete(Path.Combine(projectRoot, ".gitignore"));
            File.Delete(Path.Combine(projectRoot, ".gitattributes"));

            await TestShell.Project.OpenProject(projectFile);
            await WaitUntilAsync(
                () => TestShell.VersionControl.CurrentService?.Repository is not null);
            string staged = await RunGitAsync(
                gitPath,
                projectRoot,
                "diff",
                "--cached",
                "--name-only");
            int finalCommitCount = await CountCommitsAsync(gitPath, projectRoot);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                    Is.EqualTo("**/.beutl/\n*.tmp\n"));
                Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                    Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
                Assert.That(finalCommitCount, Is.EqualTo(initialCommitCount));
                Assert.That(staged, Is.Empty);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
            config.GitExecutablePath = oldGitPath;
            config.AutoCommitOnClose = oldAutoCommitOnClose;
            config.UseLfsWhenAvailable = oldUseLfs;
        }
    }

    [AvaloniaTest]
    public async Task Identity_view_model_prefills_the_os_user_and_writes_the_repository_identity()
    {
        var viewModel = new GitIdentityDialogViewModel();

        Assert.That(viewModel.Name.Value, Is.EqualTo(Environment.UserName));
        viewModel.Email.Value = "local@example.invalid";
        GitIdentity identity = viewModel.CreateIdentity();

        Assert.That(
            identity,
            Is.EqualTo(new GitIdentity(Environment.UserName, "local@example.invalid")));
    }

    [AvaloniaTest]
    public async Task Save_all_reserves_the_workspace_while_an_extension_writes_files()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (Project project, BlockingSaveCommands blocking) =
                await CreateProjectWithBlockingEditorAsync("save-all-workspace-lease");

            Task saveAll = TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
            await WaitUntilSaveEnteredAsync(blocking);

            IDisposable? blockedMutation = TestShell.Editor.TryBeginWorktreeMutation();
            try
            {
                Assert.That(
                    blockedMutation,
                    Is.Null,
                    "A worktree mutation must not start while an extension is still writing files.");
            }
            finally
            {
                blockedMutation?.Dispose();
            }

            blocking.Release();
            await CompleteAsync(saveAll);

            using IDisposable? mutationAfterSave = TestShell.Editor.TryBeginWorktreeMutation();
            Assert.That(
                mutationAfterSave,
                Is.Not.Null,
                "The write lease must be released once every save completed.");
            Assert.That(project.Uri, Is.Not.Null);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Explicit_save_reserves_the_workspace_while_an_extension_writes_files()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (_, BlockingSaveCommands blocking) =
                await CreateProjectWithBlockingEditorAsync("save-workspace-lease");

            Task save = TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            await WaitUntilSaveEnteredAsync(blocking);

            IDisposable? blockedMutation = TestShell.Editor.TryBeginWorktreeMutation();
            try
            {
                Assert.That(
                    blockedMutation,
                    Is.Null,
                    "A worktree mutation must not start while an extension is still writing files.");
            }
            finally
            {
                blockedMutation?.Dispose();
            }

            blocking.Release();
            await CompleteAsync(save);

            using IDisposable? mutationAfterSave = TestShell.Editor.TryBeginWorktreeMutation();
            Assert.That(mutationAfterSave, Is.Not.Null);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static async Task<(Project Project, BlockingSaveCommands Commands)>
        CreateProjectWithBlockingEditorAsync(string directoryName)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, directoryName);
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(
            640,
            480,
            30,
            44100,
            directoryName,
            location))!;
        HeadlessTestHelpers.Settle();

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        var blocking = new BlockingSaveCommands();
        var blockingItem = new Scene
        {
            Uri = new Uri(Path.Combine(projectRoot, "blocking.scene")),
        };
        var tabItem = new EditorTabItem(new FailedSaveEditorContext(blockingItem, blocking));
        TestShell.Editor.TabItems.Add(tabItem);
        TestShell.Editor.SelectedTabItem.Value = tabItem;
        HeadlessTestHelpers.Settle();
        return (project, blocking);
    }

    private static async Task WaitUntilSaveEnteredAsync(BlockingSaveCommands blocking)
    {
        for (int attempt = 0; attempt < 200 && !blocking.SaveEntered; attempt++)
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10);
        }

        Assert.That(blocking.SaveEntered, Is.True, "The extension save never started.");
    }

    private static async Task CompleteAsync(Task pending)
    {
        for (int attempt = 0; attempt < 200 && !pending.IsCompleted; attempt++)
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10);
        }

        await pending;
    }

    private static int CountSaveSnapshots(string log)
    {
        const string trailer = "Beutl-Snapshot: save";
        int count = 0;
        int index = 0;
        while ((index = log.IndexOf(trailer, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += trailer.Length;
        }

        return count;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            HeadlessTestHelpers.Settle();
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Timed out waiting for version control activation.");
    }

    private static async Task<int> CountCommitsAsync(string gitPath, string repositoryRoot)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "rev-list", "--count", "HEAD");
        return int.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountSaveSnapshotsAsync(string gitPath, string repositoryRoot)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "log", "--format=%B");
        return CountSaveSnapshots(output);
    }

    private static async Task<int> CountCloseSnapshotsAsync(string gitPath, string repositoryRoot)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "log", "--format=%B");
        return CountOccurrences(output, "Beutl-Snapshot: close");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static async Task<string> RunGitAsync(
        string gitPath,
        string repositoryRoot,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(gitPath)
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var process = Process.Start(startInfo)!;
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, stderr);
        return stdout;
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

            return process.StartInfo.FileName == "git"
                ? FindGitOnPath()
                : process.StartInfo.FileName;
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
            || output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is not { } path)
        {
            Assert.Ignore("git is not available on this machine.");
            return "git";
        }

        return path;
    }

    private sealed class FailedSaveEditorContext(
        CoreObject obj,
        IKnownEditorCommands commands) : IEditorContext
    {
        public CoreObject Object { get; } = obj;

        public EditorExtension Extension => SceneEditorExtension.Instance;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands { get; } = commands;

        public object? GetService(Type serviceType) => null;

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
        {
            return default;
        }

        public T? FindToolTab<T>()
            where T : IToolContext
        {
            return default;
        }

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }
    }

    private sealed class FailedSaveCommands : IKnownEditorCommands
    {
        public int SaveCalls { get; private set; }

        public ValueTask<bool> OnSave()
        {
            SaveCalls++;
            return ValueTask.FromResult(false);
        }
    }

    private sealed class BlockingSaveCommands : IKnownEditorCommands
    {
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SaveEntered { get; private set; }

        public async ValueTask<bool> OnSave()
        {
            SaveEntered = true;
            return await _release.Task;
        }

        public void Release() => _release.TrySetResult(true);
    }

    private sealed class IsolatedGitEnvironment : IDisposable
    {
        private readonly string? _oldGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        private readonly string? _oldNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");

        public IsolatedGitEnvironment()
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _oldGlobal);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", _oldNoSystem);
        }
    }

}
