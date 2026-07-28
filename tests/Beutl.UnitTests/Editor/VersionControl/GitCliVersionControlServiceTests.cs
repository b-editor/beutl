using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class GitCliVersionControlServiceTests : RealGitTestRepository
{
    [Test]
    public async Task InitializeAsync_creates_repository_files_and_initial_snapshot()
    {
        string projectRoot = Path.Combine(Root, "new-project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        Assert.ThrowsAsync<GitIdentityRequiredException>(
            async () => await service.InitializeAsync(
                new InitOptions(projectRoot, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.That(service.Repository, Is.EqualTo(new RepositoryInfo(projectRoot, projectRoot)));
        await service.SetLocalIdentityAsync(
            new GitIdentity("Beutl Test", "beutl-test@example.invalid"),
            CancellationToken.None);
        await service.InitializeAsync(
            new InitOptions(projectRoot, UseLfsWhenAvailable: false),
            CancellationToken.None);

        var projectRepository = new RepositoryInfo(projectRoot, projectRoot);
        GitCommandResult branch = await Runner.RunAsync(
            projectRepository,
            ["branch", "--show-current"],
            networkOperation: false,
            CancellationToken.None);
        GitCommandResult log = await Runner.RunAsync(
            projectRepository,
            ["log", "-1", "--format=%s%n%b"],
            networkOperation: false,
            CancellationToken.None);
        GitCommandResult count = await Runner.RunAsync(
            projectRepository,
            ["rev-list", "--count", "HEAD"],
            networkOperation: false,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(branch.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(count.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(log.Stdout, Does.StartWith("beutl: initialize version control\n"));
            Assert.That(log.Stdout, Does.Contain("Beutl-Snapshot: init"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                Is.EqualTo("**/.beutl/\n*.tmp\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain("*.bep text eol=lf\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain(".gitattributes text eol=lf\n"));
        });
    }

    [Test]
    public async Task InitializeAsync_installs_local_lfs_and_writes_media_patterns_when_active()
    {
        string projectRoot = Path.Combine(Root, "lfs-project");
        Directory.CreateDirectory(projectRoot);
        var runner = new RecordingInitializationRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(projectRoot, UseLfsWhenAvailable: true),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                runner.Commands,
                Does.Contain("lfs install --local"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain("resources/**/*.mp4 filter=lfs diff=lfs merge=lfs -text\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain("resources/**/*.png filter=lfs diff=lfs merge=lfs -text\n"));
        });
    }

    [Test]
    public async Task CommitAllAsync_creates_one_snapshot_and_skips_a_clean_tree()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        using var service = CreateService();

        CommitResult first = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        CommitResult second = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        GitCommandResult log = await RunGitAsync("log", "-1", "--format=%s%n%b");
        GitCommandResult count = await RunGitAsync("rev-list", "--count", "HEAD");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<CommitResult.Committed>());
            Assert.That(second, Is.TypeOf<CommitResult.NoChanges>());
            Assert.That(count.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(log.Stdout, Does.StartWith("beutl: snapshot on save\n"));
            Assert.That(log.Stdout, Does.Contain("Beutl-Snapshot: save"));
        });
    }

    [Test]
    public async Task CommitAllAsync_skips_unattended_snapshot_without_staging_when_identity_is_missing()
    {
        await RunGitAsync("config", "--unset", "user.name");
        await RunGitAsync("config", "--unset", "user.email");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        using var service = CreateService();

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on close",
            SnapshotKind.Close,
            CancellationToken.None);
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.SkippedNoIdentity>());
            Assert.That(staged.Stdout, Is.Empty);
        });
    }

    [Test]
    public async Task Identity_is_read_and_written_in_repository_local_config()
    {
        await RunGitAsync("config", "--unset", "user.name");
        await RunGitAsync("config", "--unset", "user.email");
        using var service = CreateService();

        Assert.That(await service.GetIdentityAsync(CancellationToken.None), Is.Null);

        var expected = new GitIdentity("Local User", "local@example.invalid");
        await service.SetLocalIdentityAsync(expected, CancellationToken.None);
        GitIdentity? actual = await service.GetIdentityAsync(CancellationToken.None);
        GitCommandResult localName = await RunGitAsync("config", "--local", "--get", "user.name");
        GitCommandResult localEmail = await RunGitAsync("config", "--local", "--get", "user.email");

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(localName.Stdout.Trim(), Is.EqualTo(expected.Name));
            Assert.That(localEmail.Stdout.Trim(), Is.EqualTo(expected.Email));
        });
    }

    [Test]
    public async Task CommitAllAsync_scopes_staging_to_the_project_pathspec()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "foreign.txt"), "foreign\n");
        var repository = new RepositoryInfo(Root, projectRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository,
            watcher: null,
            _ => CreateRunner());

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        GitCommandResult committed = await RunGitAsync("show", "--format=", "--name-only", "HEAD");
        GitCommandResult status = await RunGitAsync("status", "--porcelain", "--", "foreign.txt");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(committed.Stdout, Does.Contain("nested-project/project.bep"));
            Assert.That(committed.Stdout, Does.Not.Contain("foreign.txt"));
            Assert.That(status.Stdout, Does.StartWith("?? foreign.txt"));
        });
    }

    [Test]
    public async Task GetHistoryAsync_pages_commits_and_parses_snapshot_trailers()
    {
        await CommitFileAsync("project.bep", "one\n", "manual baseline");
        using var service = CreateService();

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "two\n");
        await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "three\n");
        await service.CommitAllAsync(
            "beutl: snapshot on close",
            SnapshotKind.Close,
            CancellationToken.None);

        IReadOnlyList<CommitInfo> firstPage = await service.GetHistoryAsync(
            0,
            2,
            CancellationToken.None);
        IReadOnlyList<CommitInfo> secondPage = await service.GetHistoryAsync(
            2,
            2,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstPage, Has.Count.EqualTo(2));
            Assert.That(firstPage[0].Kind, Is.EqualTo(SnapshotKind.Close));
            Assert.That(firstPage[0].Subject, Is.EqualTo("beutl: snapshot on close"));
            Assert.That(firstPage[0].AuthorName, Is.EqualTo("Beutl Test"));
            Assert.That(firstPage[0].Sha, Has.Length.EqualTo(40));
            Assert.That(firstPage[1].Kind, Is.EqualTo(SnapshotKind.Save));
            Assert.That(secondPage, Has.Count.EqualTo(1));
            Assert.That(secondPage[0].Kind, Is.EqualTo(SnapshotKind.Manual));
            Assert.That(secondPage[0].Subject, Is.EqualTo("manual baseline"));
        });
    }

    [Test]
    public async Task GetCommitFilesAsync_reports_added_modified_deleted_and_renamed_paths()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "one\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "delete.belm"), "delete\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "old.scene"), "rename\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "baseline");

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "two\n");
        File.Delete(Path.Combine(Root, "delete.belm"));
        File.Move(Path.Combine(Root, "old.scene"), Path.Combine(Root, "new.scene"));
        await File.WriteAllTextAsync(Path.Combine(Root, "added.belm"), "added\n");
        using var service = CreateService();
        var commit = (CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        IReadOnlyList<FileChange> files = await service.GetCommitFilesAsync(
            commit.Sha,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(files, Does.Contain(new FileChange("project.bep", FileChangeStatus.Modified)));
            Assert.That(files, Does.Contain(new FileChange("delete.belm", FileChangeStatus.Deleted)));
            Assert.That(files, Does.Contain(new FileChange("added.belm", FileChangeStatus.Added)));
            Assert.That(
                files,
                Does.Contain(new FileChange("new.scene", FileChangeStatus.Renamed, "old.scene")));
        });
    }

    [Test]
    public async Task GetDiffAsync_returns_unified_text_for_one_file()
    {
        await CommitFileAsync("project.bep", "old value\n", "baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "new value\n");
        using var service = CreateService();
        var commit = (CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        string diff = await service.GetDiffAsync(
            commit.Sha,
            "project.bep",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diff, Does.Contain("--- a/project.bep"));
            Assert.That(diff, Does.Contain("+++ b/project.bep"));
            Assert.That(diff, Does.Contain("-old value"));
            Assert.That(diff, Does.Contain("+new value"));
            Assert.That(diff, Does.Not.Contain(GitCliVersionControlService.DiffTruncationMarker));
        });
    }

    [Test]
    public async Task GetDiffAsync_caps_output_at_one_megabyte_and_appends_marker()
    {
        await CommitFileAsync("large.belm", "old\n", "baseline");
        string largeContents = string.Concat(
            Enumerable.Repeat("a changed line that remains text\n", 40000));
        await File.WriteAllTextAsync(Path.Combine(Root, "large.belm"), largeContents);
        using var service = CreateService();
        var commit = (CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        string diff = await service.GetDiffAsync(
            commit.Sha,
            "large.belm",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diff, Does.EndWith(GitCliVersionControlService.DiffTruncationMarker));
            Assert.That(
                System.Text.Encoding.UTF8.GetByteCount(
                    diff[..^GitCliVersionControlService.DiffTruncationMarker.Length]),
                Is.LessThanOrEqualTo(GitCliVersionControlService.MaxDiffBytes));
        });
    }

    [Test]
    public async Task RestoreWorktreeFromAsync_matches_target_and_preserves_ignored_files()
    {
        byte[] targetProject = [0x7b, 0x0a, 0x7d, 0x0a];
        byte[] targetElement = [0x31, 0x32, 0x33, 0x0a];
        await File.WriteAllTextAsync(
            Path.Combine(Root, ".gitignore"),
            "**/.beutl/\n*.tmp\n");
        await File.WriteAllBytesAsync(Path.Combine(Root, "project.bep"), targetProject);
        Directory.CreateDirectory(Path.Combine(Root, "elements"));
        await File.WriteAllBytesAsync(Path.Combine(Root, "elements", "base.belm"), targetElement);
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "target");
        string targetSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "later\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "elements", "base.belm"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "elements", "later.belm"), "later\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "later");
        string laterSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string stateDirectory = Path.Combine(Root, ".beutl");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(Path.Combine(stateDirectory, "view.json"), "keep\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "atomic.tmp"), "keep\n");

        using var service = CreateService();
        await service.RestoreWorktreeFromAsync(targetSha, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(Path.Combine(Root, "project.bep")), Is.EqualTo(targetProject));
            Assert.That(
                File.ReadAllBytes(Path.Combine(Root, "elements", "base.belm")),
                Is.EqualTo(targetElement));
            Assert.That(File.Exists(Path.Combine(Root, "elements", "later.belm")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(stateDirectory, "view.json")), Is.EqualTo("keep\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "atomic.tmp")), Is.EqualTo("keep\n"));
        });

        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "record restore");
        await service.RestoreWorktreeFromAsync(laterSha, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("later\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "elements", "base.belm")),
                Is.EqualTo("changed\n"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "elements", "later.belm")),
                Is.EqualTo("later\n"));
            Assert.That(File.ReadAllText(Path.Combine(stateDirectory, "view.json")), Is.EqualTo("keep\n"));
            Assert.That(File.ReadAllText(Path.Combine(Root, "atomic.tmp")), Is.EqualTo("keep\n"));
        });
    }

    [Test]
    public async Task CreateBranchFromAsync_switches_to_a_new_branch_at_the_selected_commit()
    {
        await CommitFileAsync("project.bep", "target\n", "target");
        string targetSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "current\n", "current");
        string mainSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        await service.CreateBranchFromAsync(
            "restored-state",
            targetSha,
            CancellationToken.None);

        GitCommandResult branch = await RunGitAsync("branch", "--show-current");
        GitCommandResult newBranchSha = await RunGitAsync("rev-parse", "HEAD");
        GitCommandResult originalBranchSha = await RunGitAsync("rev-parse", "main");
        Assert.Multiple(() =>
        {
            Assert.That(branch.Stdout.Trim(), Is.EqualTo("restored-state"));
            Assert.That(newBranchSha.Stdout.Trim(), Is.EqualTo(targetSha));
            Assert.That(originalBranchSha.Stdout.Trim(), Is.EqualTo(mainSha));
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("target\n"));
        });

        await service.SwitchBranchAsync("main", CancellationToken.None);
        GitCommandResult switchedBack = await RunGitAsync("branch", "--show-current");

        Assert.Multiple(() =>
        {
            Assert.That(switchedBack.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("current\n"));
        });
    }

    [Test]
    public async Task Worktree_mutations_are_rejected_until_the_project_is_closed()
    {
        await CommitFileAsync("project.bep", "current\n", "current");
        string head = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            isWorktreeMutationAllowed: static () => false);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.RestoreWorktreeFromAsync(
                    head,
                    CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.CreateBranchFromAsync(
                    "blocked-branch",
                    head,
                    CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.SwitchBranchAsync(
                    "main",
                    CancellationToken.None));
        });

        GitCommandResult branch = await RunGitAsync("branch", "--show-current");
        Assert.Multiple(() =>
        {
            Assert.That(branch.Stdout.Trim(), Is.EqualTo("main"));
            Assert.That(
                File.ReadAllText(Path.Combine(Root, "project.bep")),
                Is.EqualTo("current\n"));
        });
    }

    [Test]
    public void Porcelain_v2_parser_reads_branch_counts_renames_and_conflicts()
    {
        string output = string.Join('\0',
        [
            "# branch.oid abcdef",
            "# branch.head feature/test",
            "# branch.ab +2 -3",
            "1 .M N... 100644 100644 100644 aaaaaaa bbbbbbb project file.bep",
            "2 R. N... 100644 100644 100644 aaaaaaa bbbbbbb R100 renamed.scene",
            "old.scene",
            "u UU N... 100644 100644 100644 100644 aaaaaaa bbbbbbb ccccccc conflict.belm",
            "? added.belm",
            "",
        ]);

        WorkspaceStatus status = GitCliVersionControlService.ParseStatus(output);

        Assert.Multiple(() =>
        {
            Assert.That(status.Branch, Is.EqualTo("feature/test"));
            Assert.That(status.Ahead, Is.EqualTo(2));
            Assert.That(status.Behind, Is.EqualTo(3));
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(status.Changes, Has.Count.EqualTo(4));
            Assert.That(status.Changes[0],
                Is.EqualTo(new FileChange("project file.bep", FileChangeStatus.Modified)));
            Assert.That(status.Changes[1],
                Is.EqualTo(new FileChange("renamed.scene", FileChangeStatus.Renamed, "old.scene")));
            Assert.That(status.Changes[3],
                Is.EqualTo(new FileChange("added.belm", FileChangeStatus.Added)));
        });
    }

    [Test]
    public async Task GetStatusAsync_reports_real_untracked_file()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}");
        using var service = CreateService();

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.Branch, Is.EqualTo("main"));
            Assert.That(status.IsClean, Is.False);
            Assert.That(status.HasConflicts, Is.False);
            Assert.That(status.Changes,
                Does.Contain(new FileChange("project.bep", FileChangeStatus.Added)));
        });
    }

    [Test]
    public async Task GetStatusAsync_detects_real_unmerged_paths()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main\n", "main");

        Assert.ThrowsAsync<GitOperationException>(
            async () => await RunGitAsync("merge", "alternate"));
        using var service = CreateService();

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(status.Changes,
                Does.Contain(new FileChange("project.bep", FileChangeStatus.Modified)));
        });
    }

    [Test]
    public async Task Concurrent_status_calls_are_serialized()
    {
        var runner = new ConcurrencyTrackingRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> first = service.GetStatusAsync(CancellationToken.None);
        Task<WorkspaceStatus> second = service.GetStatusAsync(CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.That(runner.MaxConcurrency, Is.EqualTo(1));
    }

    [Test]
    public async Task Git_runtime_is_reused_until_the_executable_config_changes()
    {
        string firstPath = Path.GetFullPath(Path.Combine(Root, "git-one"));
        string secondPath = Path.GetFullPath(Path.Combine(Root, "git-two"));
        var config = new VersionControlConfig { GitExecutablePath = firstPath };
        var probe = new RuntimeProbe();
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
        int runnerCreations = 0;
        using var service = new GitCliVersionControlService(
            locator,
            Repository,
            watcher: null,
            _ =>
            {
                runnerCreations++;
                return new StaticStatusRunner();
            });

        await service.GetStatusAsync(CancellationToken.None);
        await service.GetStatusAsync(CancellationToken.None);
        config.GitExecutablePath = secondPath;
        await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(runnerCreations, Is.EqualTo(2));
            Assert.That(probe.VersionProbeCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Watcher_refresh_raises_StatusChanged_on_background_thread()
    {
        var timeProvider = new FakeTimeProvider();
        var watcher = new RepositoryWatcher(Root, timeProvider, startWatching: false);
        using var service = CreateService(watcher);
        int callerThread = Environment.CurrentManagedThreadId;
        var completion = new TaskCompletionSource<(WorkspaceStatus Status, int ThreadId)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += (_, status) =>
            completion.TrySetResult((status, Environment.CurrentManagedThreadId));
        await File.WriteAllTextAsync(Path.Combine(Root, "changed.bep"), "{}");

        watcher.NotifyPathChanged(Path.Combine(Root, "changed.bep"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        (WorkspaceStatus status, int eventThread) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(eventThread, Is.Not.EqualTo(callerThread));
            Assert.That(status.Changes,
                Does.Contain(new FileChange("changed.bep", FileChangeStatus.Added)));
        });
    }

    private GitCliVersionControlService CreateService(RepositoryWatcher? watcher = null)
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => CreateRunner());
    }

    private sealed class ConcurrencyTrackingRunner : IGitCliRunner
    {
        private int _concurrency;

        public int MaxConcurrency { get; private set; }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            bool networkOperation,
            CancellationToken cancellationToken)
        {
            int concurrency = Interlocked.Increment(ref _concurrency);
            MaxConcurrency = Math.Max(MaxConcurrency, concurrency);
            try
            {
                await Task.Delay(100, cancellationToken);
                return new GitCommandResult(0, "# branch.head main\0", "");
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }
    }

    private sealed class StaticStatusRunner : IGitCliRunner
    {
        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            bool networkOperation,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new GitCommandResult(0, "# branch.head main\0", ""));
        }
    }

    private sealed class RecordingInitializationRunner : IGitCliRunner
    {
        public List<string> Commands { get; } = [];

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            bool networkOperation,
            CancellationToken cancellationToken)
        {
            Commands.Add(string.Join(' ', arguments));
            if (arguments.SequenceEqual(["config", "--get", "user.name"]))
            {
                return Task.FromResult(new GitCommandResult(0, "Beutl Test\n", ""));
            }

            if (arguments.SequenceEqual(["config", "--get", "user.email"]))
            {
                return Task.FromResult(new GitCommandResult(0, "beutl-test@example.invalid\n", ""));
            }

            if (arguments.FirstOrDefault() == "status")
            {
                return Task.FromResult(new GitCommandResult(
                    0,
                    "# branch.head main\0? .gitignore\0? .gitattributes\0",
                    ""));
            }

            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }

    private sealed class RuntimeProbe : IGitInstallationProbe
    {
        public int VersionProbeCount { get; private set; }

        public Task<IReadOnlyList<string>> FindOnPathAsync(
            string executableName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<GitProbeResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            if (arguments.SequenceEqual(["--version"]))
            {
                VersionProbeCount++;
                return Task.FromResult(new GitProbeResult(0, "git version 2.50.0", ""));
            }

            return Task.FromResult(new GitProbeResult(1, "", ""));
        }

        public bool FileExists(string path) => false;

        public string? GetEnvironmentVariable(string name) => null;
    }
}
