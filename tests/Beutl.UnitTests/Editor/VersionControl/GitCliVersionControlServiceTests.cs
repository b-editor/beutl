using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class GitCliVersionControlServiceTests : RealGitTestRepository
{
    [Test]
    public async Task InitializeAsync_creates_repository_files_and_initial_snapshot()
    {
        string projectRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => CreateRunner());

        Assert.ThrowsAsync<GitIdentityRequiredException>(
            async () => await service.InitializeAsync(
                new InitOptions(
                    new RepositoryInfo(projectRoot, projectRoot),
                    UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.That(service.Repository, Is.EqualTo(new RepositoryInfo(projectRoot, projectRoot)));
        await service.SetLocalIdentityAsync(
            new GitIdentity("Beutl Test", "beutl-test@example.invalid"),
            CancellationToken.None);
        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: false),
            CancellationToken.None);

        var projectRepository = new RepositoryInfo(projectRoot, projectRoot);
        GitCommandResult branch = await Runner.RunAsync(
            projectRepository,
            ["branch", "--show-current"],
            GitCommandOptions.Local,
            CancellationToken.None);
        GitCommandResult log = await Runner.RunAsync(
            projectRepository,
            ["log", "-1", "--format=%s%n%b"],
            GitCommandOptions.Local,
            CancellationToken.None);
        GitCommandResult count = await Runner.RunAsync(
            projectRepository,
            ["rev-list", "--count", "HEAD"],
            GitCommandOptions.Local,
            CancellationToken.None);
        GitCommandResult topLevel = await Runner.RunAsync(
            projectRepository,
            ["rev-parse", "--show-toplevel"],
            GitCommandOptions.Local,
            CancellationToken.None);
        string expectedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(topLevel.Stdout.Trim()));

        Assert.Multiple(() =>
        {
            Assert.That(service.Repository!.RepoRoot, Is.EqualTo(expectedRoot));
            Assert.That(service.Repository.ProjectRoot, Is.EqualTo(expectedRoot));
            Assert.That(service.Repository.Pathspec, Is.EqualTo("."));
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
    public async Task InitializeAsync_does_not_reassociate_a_service_with_another_repository()
    {
        string otherRoot = CreateTemporaryDirectory();
        var otherRepository = new RepositoryInfo(otherRoot, otherRoot);
        await Runner.RunAsync(
            otherRepository,
            ["init"],
            GitCommandOptions.Local,
            CancellationToken.None);
        using GitCliVersionControlService service = CreateService();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(otherRepository, UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.That(service.Repository, Is.SameAs(Repository));
    }

    [Test]
    public void InitializeAsync_rejects_discovery_from_a_different_repository()
    {
        string projectRoot = CreateTemporaryDirectory();
        string discoveredRoot = CreateTemporaryDirectory();
        var runner = new MismatchedDiscoveryRunner(discoveredRoot);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.InitializeAsync(
                new InitOptions(
                    new RepositoryInfo(projectRoot, projectRoot),
                    UseLfsWhenAvailable: false),
                CancellationToken.None));

        Assert.That(runner.Commands, Does.Not.Contain("init"));
    }

    [Test]
    public async Task InitializeAsync_installs_local_lfs_and_writes_media_patterns_when_active()
    {
        string projectRoot = CreateTemporaryDirectory();
        var runner = new RecordingInitializationRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(lfsInstalled: true),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: true),
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
    public async Task EnsureRepositoryHygieneAsync_is_idempotent_and_does_not_stage_or_commit()
    {
        await CommitFileAsync("project.bep", "{}\n", "existing repository");
        string initialTip = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);
        await service.EnsureRepositoryHygieneAsync(CancellationToken.None);

        GitCommandResult currentTip = await RunGitAsync("rev-parse", "HEAD");
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");
        Assert.Multiple(() =>
        {
            Assert.That(currentTip.Stdout.Trim(), Is.EqualTo(initialTip));
            Assert.That(staged.Stdout, Is.Empty);
            Assert.That(
                File.ReadAllLines(Path.Combine(Root, ".gitignore")),
                Is.EqualTo(new[] { "**/.beutl/", "*.tmp" }));
            Assert.That(
                File.ReadAllLines(Path.Combine(Root, ".gitattributes"))
                    .Count(static line => line == "*.bep text eol=lf"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task InitializeAsync_sets_main_with_git_2_23_compatible_commands()
    {
        string projectRoot = CreateTemporaryDirectory();
        var runner = new RecordingInitializationRunner();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(
                new RepositoryInfo(projectRoot, projectRoot),
                UseLfsWhenAvailable: false),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(runner.Commands, Does.Contain("init"));
            Assert.That(
                runner.Commands,
                Does.Contain("symbolic-ref HEAD refs/heads/main"));
            Assert.That(runner.Commands, Does.Not.Contain("init -b main"));
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
    public async Task CommitAllAsync_scopes_commit_and_preserves_foreign_staged_changes()
    {
        await CommitFileAsync("baseline.txt", "baseline\n", "baseline");
        string projectRoot = Path.Combine(Root, "nested-project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "foreign.txt"), "foreign\n");
        await RunGitAsync("add", "--", "foreign.txt");
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
        GitCommandResult staged = await RunGitAsync("diff", "--cached", "--name-only");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(committed.Stdout, Does.Contain("nested-project/project.bep"));
            Assert.That(committed.Stdout, Does.Not.Contain("foreign.txt"));
            Assert.That(staged.Stdout.Trim(), Is.EqualTo("foreign.txt"));
        });
    }

    [Test]
    public async Task Stale_lock_failure_is_exposed_and_removed_only_on_request()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "{}\n");
        string lockPath = Path.Combine(Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "");
        File.SetLastWriteTimeUtc(
            lockPath,
            DateTime.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        using GitCliVersionControlService service = CreateService();
        var completion = new TaskCompletionSource<RepositoryLockInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, lockInfo) =>
            completion.TrySetResult(lockInfo);

        Assert.ThrowsAsync<GitOperationException>(
            async () => await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None));
        RepositoryLockInfo lockInfo = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(service.RecoverableLock, Is.EqualTo(lockInfo));
            Assert.That(File.Exists(lockPath), Is.True);
        });

        Assert.That(
            await service.RemoveRecoverableLockAsync(CancellationToken.None),
            Is.True);
        Assert.That(File.Exists(lockPath), Is.False);
        Assert.That(
            await service.CommitAllAsync(
                "beutl: snapshot on save",
                SnapshotKind.Save,
                CancellationToken.None),
            Is.TypeOf<CommitResult.Committed>());
    }

    [Test]
    public async Task Mapped_remote_failure_still_exposes_recoverable_lock()
    {
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new RemoteLockFailureRunner(expectedLock);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);
        var completion = new TaskCompletionSource<RepositoryLockInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, lockInfo) =>
            completion.TrySetResult(lockInfo);

        RemoteOpResult result = await service.PushAsync(
            progress: null,
            CancellationToken.None);
        RepositoryLockInfo actual = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(actual, Is.EqualTo(expectedLock));
            Assert.That(service.RecoverableLock, Is.EqualTo(expectedLock));
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
    public async Task GetHistoryAsync_parses_recovery_snapshot_trailer()
    {
        await CommitFileAsync("project.bep", "before\n", "baseline");
        using var service = CreateService();
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "recovered\n");

        await service.CommitAllAsync(
            "beutl: recover project state after failed restore",
            SnapshotKind.Recovery,
            CancellationToken.None);
        CommitInfo recovery = (await service.GetHistoryAsync(
            0,
            1,
            CancellationToken.None)).Single();

        Assert.That(recovery.Kind, Is.EqualTo(SnapshotKind.Recovery));
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
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

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
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

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
    public async Task GetDiffAsync_treats_pathspec_magic_like_top_as_a_literal_file_name()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows file names cannot contain a colon.");
        }

        await CommitFileAsync(":(top)", "old literal\n", "literal baseline");
        await CommitFileAsync("other.bep", "old other\n", "other baseline");
        await File.WriteAllTextAsync(Path.Combine(Root, ":(top)"), "new literal\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "other.bep"), "new other\n");
        using var service = CreateService();
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "literal path update",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

        string diff = await service.GetDiffAsync(
            commit.Sha,
            ":(top)",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diff, Does.Contain("+new literal"));
            Assert.That(diff, Does.Not.Contain("+new other"));
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
        var commit = (CommitRevision.Known)((CommitResult.Committed)await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None)).Revision;

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
    public async Task CommitProjectTreeAsync_matches_target_and_preserves_ignored_files()
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
        CheckedOutBranchTip laterTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);
        CommitResult targetRestore = await service.CommitProjectTreeAsync(
            laterTip,
            targetSha,
            "beutl: restore target",
            SnapshotKind.Restore,
            CancellationToken.None);

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

        var targetRestoreCommit = (CommitRevision.Known)
            ((CommitResult.Committed)targetRestore).Revision;
        await service.CommitProjectTreeAsync(
            new CheckedOutBranchTip(laterTip.RefName, targetRestoreCommit.Sha),
            laterSha,
            "beutl: restore later",
            SnapshotKind.Restore,
            CancellationToken.None);

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
    public async Task CreateBranchAsync_switches_to_a_new_branch_at_the_selected_commit()
    {
        await CommitFileAsync("project.bep", "target\n", "target");
        string targetSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        await CommitFileAsync("project.bep", "current\n", "current");
        string mainSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        await service.CreateBranchAsync(
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
    public async Task Branches_can_be_listed_created_and_switched_after_diverging()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        string baseSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        using var service = CreateService();

        await service.CreateBranchAsync("alternate", "HEAD", CancellationToken.None);
        IReadOnlyList<BranchInfo> afterCreate = await service.GetBranchesAsync(
            CancellationToken.None);
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        string alternateSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();

        await service.SwitchBranchAsync("main", CancellationToken.None);
        await CommitFileAsync("project.bep", "main\n", "main");
        string mainSha = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        IReadOnlyList<BranchInfo> onMain = await service.GetBranchesAsync(
            CancellationToken.None);

        await service.SwitchBranchAsync("alternate", CancellationToken.None);
        string mergeBase = (await RunGitAsync(
            "merge-base",
            "main",
            "alternate")).Stdout.Trim();

        Assert.Multiple(() =>
        {
            Assert.That(afterCreate.Single(branch => branch.Name == "alternate").IsCurrent, Is.True);
            Assert.That(mergeBase, Is.EqualTo(baseSha));
            Assert.That(mainSha, Is.Not.EqualTo(alternateSha));
            Assert.That(onMain.Single(branch => branch.Name == "main").IsCurrent, Is.True);
            Assert.That(onMain.Single(branch => branch.Name == "alternate").IsCurrent, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Root, "project.bep")), Is.EqualTo("alternate\n"));
        });
    }

    [Test]
    public void Branch_parser_reads_current_and_upstream_fields()
    {
        IReadOnlyList<BranchInfo> branches = GitCliVersionControlService.ParseBranches(
            "alternate\0 \0\0\nmain\0*\0origin/main\n");

        Assert.That(
            branches,
            Is.EqualTo(new[]
            {
                new BranchInfo("alternate", false, null),
                new BranchInfo("main", true, "origin/main"),
            }));
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
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.CommitProjectTreeAsync(
                    new CheckedOutBranchTip("refs/heads/main", head),
                    head,
                    "blocked restore",
                    SnapshotKind.Restore,
                    CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.CreateBranchAsync(
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
    public async Task GetStatusAsync_reports_files_inside_untracked_directories()
    {
        string mediaPath = Path.Combine(Root, "resources", "nested", "large.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllTextAsync(mediaPath, "media");
        using var service = CreateService();

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                status.Changes,
                Does.Contain(new FileChange(
                    "resources/nested/large.mp4",
                    FileChangeStatus.Added)));
            Assert.That(
                status.Changes.Select(change => change.Path),
                Does.Not.Contain("resources/"));
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
    public async Task Conflicted_repository_keeps_reads_available_and_blocks_every_mutation()
    {
        await CommitFileAsync("project.bep", "base\n", "base");
        await RunGitAsync("switch", "-c", "alternate");
        await CommitFileAsync("project.bep", "alternate\n", "alternate");
        await RunGitAsync("switch", "main");
        await CommitFileAsync("project.bep", "main\n", "main");
        Assert.ThrowsAsync<GitOperationException>(
            async () => await RunGitAsync("merge", "alternate"));
        using var service = CreateService();

        GitAvailability availability = await service.GetAvailabilityAsync(CancellationToken.None);
        RepositoryInfo? discovered = await service.DiscoverRepositoryAsync(
            Root,
            CancellationToken.None);
        GitCommandResult topLevel = await RunGitAsync("rev-parse", "--show-toplevel");
        string expectedRepoRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(topLevel.Stdout.Trim()));
        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);
        IReadOnlyList<CommitInfo> history = await service.GetHistoryAsync(
            0,
            10,
            CancellationToken.None);
        IReadOnlyList<FileChange> files = await service.GetCommitFilesAsync(
            history[0].Sha,
            CancellationToken.None);
        string diff = await service.GetDiffAsync(
            history[0].Sha,
            path: null,
            CancellationToken.None);
        GitIdentity? identity = await service.GetIdentityAsync(CancellationToken.None);
        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(
            CancellationToken.None);
        IReadOnlyList<RemoteInfo> remotes = await service.GetRemotesAsync(
            CancellationToken.None);
        CheckedOutBranchTip expectedTip = await service.GetCheckedOutBranchTipAsync(
            CancellationToken.None);

        VersionControlConflictedException[] exceptions =
        [
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitAllAsync(
                    "blocked",
                    SnapshotKind.Manual,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CommitProjectTreeAsync(
                    expectedTip,
                    history[0].Sha,
                    "blocked restore",
                    SnapshotKind.Restore,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.CreateBranchAsync(
                    "blocked-branch",
                    history[0].Sha,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.SwitchBranchAsync(
                    "alternate",
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.InitializeAsync(
                    new InitOptions(Repository, UseLfsWhenAvailable: false),
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.SetLocalIdentityAsync(
                    new GitIdentity("Blocked", "blocked@example.invalid"),
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.SetRemoteAsync(
                    "https://example.invalid/repository.git",
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.PushAsync(
                    progress: null,
                    CancellationToken.None))!,
            Assert.ThrowsAsync<VersionControlConflictedException>(
                async () => await service.PullFastForwardAsync(
                    expectedTip,
                    checkpoint: null,
                    CancellationToken.None))!,
        ];

        Assert.Multiple(() =>
        {
            Assert.That(availability.State, Is.EqualTo(GitAvailabilityState.Installed));
            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.RepoRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(discovered.ProjectRoot, Is.EqualTo(expectedRepoRoot));
            Assert.That(discovered.Pathspec, Is.EqualTo("."));
            Assert.That(status.HasConflicts, Is.True);
            Assert.That(history, Is.Not.Empty);
            Assert.That(files, Is.Not.Null);
            Assert.That(diff, Is.Not.Null);
            Assert.That(branches, Is.Not.Empty);
            Assert.That(remotes, Is.Empty);
            Assert.That(
                identity,
                Is.EqualTo(new GitIdentity(
                    "Beutl Test",
                    "beutl-test@example.invalid")));
            Assert.That(
                exceptions.Select(exception => exception.Guidance),
                Is.All.EqualTo(Strings.VersionControl_ConflictGuidance));
        });

        GitCommandResult unmerged = await RunGitAsync("ls-files", "-u");
        Assert.That(unmerged.Stdout, Does.Contain("project.bep"));
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
    public async Task Dispose_allows_an_in_flight_operation_to_release_the_gate()
    {
        var runner = new BlockingStatusRunner();
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> operation = service.GetStatusAsync(CancellationToken.None);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispose();
        runner.Complete();

        Assert.DoesNotThrowAsync(async () => await operation);
        Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await service.GetStatusAsync(CancellationToken.None));
    }

    [Test]
    public async Task Retirement_waits_for_started_operation_and_rejects_queued_and_new_calls()
    {
        var runner = new BlockingStatusRunner();
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> started = service.GetStatusAsync(CancellationToken.None);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
        Task<WorkspaceStatus> queued = service.GetStatusAsync(CancellationToken.None);
        Task retirement = ((IProjectVersionControlBackend)service).RetireAsync(
            finalSnapshot: null);

        Assert.Multiple(() =>
        {
            Assert.That(started.IsCompleted, Is.False);
            Assert.That(retirement.IsCompleted, Is.False);
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await service.GetStatusAsync(CancellationToken.None));
        });

        runner.Complete();
        Assert.DoesNotThrowAsync(async () => await started);
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await queued);
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Retirement_creates_final_snapshot_after_started_raw_operation()
    {
        var runner = new BlockingFirstStatusRunner(CreateRunner());
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner);

        Task<WorkspaceStatus> operation = service.GetStatusAsync(CancellationToken.None);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "final state\n");
        Task retirement = ((IProjectVersionControlBackend)service).RetireAsync(
            new ProjectVersionControlFinalSnapshot(
                "beutl: snapshot on close",
                SnapshotKind.Close));

        runner.Complete();
        await operation;
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        GitCommandResult log = await RunGitAsync(
            "log",
            "-1",
            "--pretty=%s%n%(trailers:key=Beutl-Snapshot,valueonly)");

        Assert.Multiple(() =>
        {
            Assert.That(log.Stdout, Does.Contain("beutl: snapshot on close"));
            Assert.That(log.Stdout, Does.Contain("close"));
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await service.GetStatusAsync(CancellationToken.None));
        });
    }

    [Test]
    public async Task Retirement_requested_during_initialization_commits_the_final_close_state()
    {
        string projectRoot = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "initial state\n");
        var repository = new RepositoryInfo(projectRoot, projectRoot);
        GitCliRunner commandRunner = CreateRunner();
        var runner = new BlockingInitializationRunner(commandRunner);
        var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        try
        {
            Task initialization = service.InitializeAsync(
                new InitOptions(repository, UseLfsWhenAvailable: false),
                CancellationToken.None);
            await runner.RepositoryInitialized.WaitAsync(TimeSpan.FromSeconds(5));
            await commandRunner.RunAsync(
                repository,
                ["config", "user.name", "Initialization Test"],
                GitCommandOptions.Local,
                CancellationToken.None);
            await commandRunner.RunAsync(
                repository,
                ["config", "user.email", "initialization@example.invalid"],
                GitCommandOptions.Local,
                CancellationToken.None);

            Task retirement = ((IProjectVersionControlBackend)service).RetireAsync(
                new ProjectVersionControlFinalSnapshot(
                    "beutl: snapshot on close",
                    SnapshotKind.Close));
            Assert.Multiple(() =>
            {
                Assert.That(service.Repository, Is.Null);
                Assert.That(retirement.IsCompleted, Is.False);
            });

            runner.ContinueAfterRepositoryInitialization();
            await runner.InitialCommitCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "project.bep"),
                "final close state\n");
            runner.ContinueAfterInitialCommit();
            await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            await retirement.WaitAsync(TimeSpan.FromSeconds(5));

            GitCommandResult log = await commandRunner.RunAsync(
                repository,
                ["log", "-2", "--pretty=%s%n%(trailers:key=Beutl-Snapshot,valueonly)"],
                GitCommandOptions.Local,
                CancellationToken.None);
            GitCommandResult contents = await commandRunner.RunAsync(
                repository,
                ["show", "HEAD:project.bep"],
                GitCommandOptions.Local,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(log.Stdout, Does.Contain("beutl: snapshot on close"));
                Assert.That(log.Stdout, Does.Contain("beutl: initialize version control"));
                Assert.That(log.Stdout, Does.Contain("close"));
                Assert.That(log.Stdout, Does.Contain("init"));
                Assert.That(contents.Stdout, Is.EqualTo("final close state\n"));
            });
        }
        finally
        {
            runner.ContinueAfterRepositoryInitialization();
            runner.ContinueAfterInitialCommit();
            service.Dispose();
        }
    }

    [Test]
    public void Dispose_is_safe_when_called_concurrently()
    {
        GitCliVersionControlService service = CreateService();

        Assert.DoesNotThrow(() => Parallel.For(0, 64, _ => service.Dispose()));
        Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await service.GetStatusAsync(CancellationToken.None));
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

    [Test]
    public async Task Watcher_refresh_logs_unexpected_failures()
    {
        var timeProvider = new FakeTimeProvider();
        var watcher = new RepositoryWatcher(Root, timeProvider, startWatching: false);
        var logger = new RecordingLogger();
        var expected = new IOException("status failed");
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => new ThrowingStatusRunner(expected),
            logger);

        watcher.NotifyPathChanged(Path.Combine(Root, "changed.bep"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(expected));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to refresh version-control status after a repository change."));
        });
    }

    [Test]
    public async Task Durable_mutations_succeed_when_status_refresh_fails()
    {
        string remoteRoot = CreateTemporaryDirectory();
        var remoteRepository = new RepositoryInfo(remoteRoot, remoteRoot);
        await CreateRunner().RunAsync(
            remoteRepository,
            ["init", "--bare", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        var runner = new FailingPostMutationStatusRunner(CreateRunner());
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);

        CommitResult commit = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        await service.CreateBranchAsync("alternate", "HEAD", CancellationToken.None);
        await service.SwitchBranchAsync("main", CancellationToken.None);
        await service.SetRemoteAsync(remoteRoot, CancellationToken.None);
        RemoteOpResult push = await service.PushAsync(progress: null, CancellationToken.None);

        string localHead = (await RunGitAsync("rev-parse", "HEAD")).Stdout.Trim();
        string remoteHead = (await CreateRunner().RunAsync(
            remoteRepository,
            ["rev-parse", "refs/heads/main"],
            GitCommandOptions.Local,
            CancellationToken.None)).Stdout.Trim();
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(commit, Is.TypeOf<CommitResult.Committed>());
            Assert.That(push, Is.TypeOf<RemoteOpResult.Success>());
            Assert.That(remoteHead, Is.EqualTo(localHead));
            Assert.That(runner.StatusFailureCount, Is.EqualTo(5));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(runner.StatusFailure));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to publish version-control status after a durable Git operation."));
        });
    }

    [Test]
    public async Task CommitAllAsync_reports_committed_when_post_commit_revision_lookup_fails()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        var runner = new FailingPostCommitRevisionRunner(CreateRunner());
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        GitCommandResult commitCount = await RunGitAsync("rev-list", "--count", "HEAD");
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                ((CommitResult.Committed)result).Revision,
                Is.TypeOf<CommitRevision.Unavailable>());
            Assert.That(commitCount.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(runner.RevisionFailureCount, Is.EqualTo(1));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(runner.RevisionFailure));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to resolve the revision created by a successful Git commit."));
        });
    }

    [Test]
    public async Task InitializeAsync_succeeds_when_status_refresh_fails_after_initial_commit()
    {
        string projectRoot = CreateTemporaryDirectory();
        var projectRepository = new RepositoryInfo(projectRoot, projectRoot);
        GitCliRunner setupRunner = CreateRunner();
        await setupRunner.RunAsync(
            projectRepository,
            ["init", "-b", "main"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await setupRunner.RunAsync(
            projectRepository,
            ["config", "user.name", "Beutl Test"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await setupRunner.RunAsync(
            projectRepository,
            ["config", "user.email", "beutl-test@example.invalid"],
            GitCommandOptions.Local,
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "project.bep"), "initial\n");
        var runner = new FailingPostMutationStatusRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            repository: null,
            watcher: null,
            _ => runner);

        await service.InitializeAsync(
            new InitOptions(projectRepository, UseLfsWhenAvailable: false),
            CancellationToken.None);

        GitCommandResult commitCount = await setupRunner.RunAsync(
            projectRepository,
            ["rev-list", "--count", "HEAD"],
            GitCommandOptions.Local,
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(service.Repository, Is.Not.Null);
            Assert.That(
                RepositoryPathComparer.AreEquivalent(
                    service.Repository!.ProjectRoot,
                    projectRepository.ProjectRoot),
                Is.True);
            Assert.That(service.Repository.Pathspec, Is.EqualTo(projectRepository.Pathspec));
            Assert.That(commitCount.Stdout.Trim(), Is.EqualTo("1"));
            Assert.That(runner.StatusFailureCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StatusChanged_subscriber_failure_is_isolated_and_logged()
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), "initial\n");
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(),
            logger);
        var expected = new InvalidOperationException("subscriber failed");
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += (_, _) => throw expected;
        service.StatusChanged += (_, _) => notified.TrySetResult();

        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);

        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(expected));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to notify a version-control status subscriber."));
        });
    }

    [Test]
    public async Task SetRemoteAsync_succeeds_when_post_mutation_policy_check_fails()
    {
        const string remoteUrl = "https://example.invalid/repository.git";
        var runner = new FailingPostRemoteAuxiliaryRunner(CreateRunner());
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);

        await service.SetRemoteAsync(remoteUrl, CancellationToken.None);

        string configuredRemote = (await RunGitAsync("remote", "get-url", "origin")).Stdout.Trim();
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(configuredRemote, Is.EqualTo(remoteUrl));
            Assert.That(runner.AuxiliaryFailureCount, Is.EqualTo(1));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(runner.AuxiliaryFailure));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to publish the Git LFS quota notice after configuring the remote."));
        });
    }

    [Test]
    public async Task RecoverableLockAvailable_subscriber_failure_is_isolated_and_logged()
    {
        var expectedLock = new RepositoryLockInfo(
            Path.Combine(Root, ".git", "index.lock"),
            DateTimeOffset.UtcNow - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1));
        var runner = new RemoteLockFailureRunner(expectedLock);
        var logger = new RecordingLogger();
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => runner,
            logger);
        var expected = new InvalidOperationException("subscriber failed");
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RecoverableLockAvailable += (_, _) => throw expected;
        service.RecoverableLockAvailable += (_, _) => notified.TrySetResult();

        RemoteOpResult result = await service.PushAsync(
            progress: null,
            CancellationToken.None);

        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        LogEntry entry = await logger.Entry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<RemoteOpResult.Failed>());
            Assert.That(service.RecoverableLock, Is.EqualTo(expectedLock));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.SameAs(expected));
            Assert.That(
                entry.Message,
                Is.EqualTo("Failed to notify a recoverable repository-lock subscriber."));
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

        public bool HasActiveProcess => _concurrency > 0;

        public int MaxConcurrency { get; private set; }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
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

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class BlockingStatusRunner : IGitCliRunner
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasActiveProcess => !_completion.Task.IsCompleted;

        public Task Started => _started.Task;

        public void Complete()
        {
            _completion.TrySetResult(true);
        }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            _started.TrySetResult(true);
            await _completion.Task.WaitAsync(cancellationToken);
            return new GitCommandResult(0, "# branch.head main\0", "");
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class BlockingInitializationRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private readonly TaskCompletionSource _initialCommitCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _repositoryInitialized = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseInitialCommit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRepositoryInitialization = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task InitialCommitCompleted => _initialCommitCompleted.Task;

        public Task RepositoryInitialized => _repositoryInitialized.Task;

        public void ContinueAfterInitialCommit()
        {
            _releaseInitialCommit.TrySetResult();
        }

        public void ContinueAfterRepositoryInitialization()
        {
            _releaseRepositoryInitialization.TrySetResult();
        }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (arguments.SequenceEqual(["symbolic-ref", "HEAD", "refs/heads/main"]))
            {
                _repositoryInitialized.TrySetResult();
                await _releaseRepositoryInitialization.Task.WaitAsync(cancellationToken);
            }
            else if (arguments.FirstOrDefault() == "commit"
                     && arguments.Contains("Beutl-Snapshot: init"))
            {
                _initialCommitCompleted.TrySetResult();
                await _releaseInitialCommit.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class BlockingFirstStatusRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blocked;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task Started => _started.Task;

        public void Complete()
        {
            _completion.TrySetResult();
        }

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.Count > 0
                && arguments[0] == "status"
                && Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                _started.TrySetResult();
                await _completion.Task.WaitAsync(cancellationToken);
            }

            return await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class ThrowingStatusRunner(Exception exception) : IGitCliRunner
    {
        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            return Task.FromException<GitCommandResult>(exception);
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class FailingPostMutationStatusRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _failNextStatus;
        private int _statusFailureCount;

        public IOException StatusFailure { get; } = new("post-mutation status failed");

        public int StatusFailureCount => Volatile.Read(ref _statusFailureCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "status"
                && Interlocked.Exchange(ref _failNextStatus, 0) != 0)
            {
                Interlocked.Increment(ref _statusFailureCount);
                throw StatusFailure;
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (IsDurableMutation(arguments))
            {
                Volatile.Write(ref _failNextStatus, 1);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);

        private static bool IsDurableMutation(IReadOnlyList<string> arguments)
        {
            string? command = arguments.FirstOrDefault();
            return command is "commit" or "push" or "switch"
                   || (command == "remote"
                       && arguments.Count > 1
                       && arguments[1] is "add" or "set-url");
        }
    }

    private sealed class FailingPostCommitRevisionRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _commitCompleted;
        private int _revisionFailureCount;

        public IOException RevisionFailure { get; } = new("post-commit revision lookup failed");

        public int RevisionFailureCount => Volatile.Read(ref _revisionFailureCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (Volatile.Read(ref _commitCompleted) != 0
                && arguments.SequenceEqual(["rev-parse", "HEAD"]))
            {
                Interlocked.Increment(ref _revisionFailureCount);
                Volatile.Write(ref _commitCompleted, 0);
                throw RevisionFailure;
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (arguments.FirstOrDefault() == "commit")
            {
                Volatile.Write(ref _commitCompleted, 1);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class FailingPostRemoteAuxiliaryRunner(IGitCliRunner inner) : IGitCliRunner
    {
        private int _remoteMutated;
        private int _auxiliaryFailureCount;

        public IOException AuxiliaryFailure { get; } = new("post-remote auxiliary check failed");

        public int AuxiliaryFailureCount => Volatile.Read(ref _auxiliaryFailureCount);

        public bool HasActiveProcess => inner.HasActiveProcess;

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (Volatile.Read(ref _remoteMutated) != 0
                && arguments.SequenceEqual(["remote", "get-url", "origin"]))
            {
                Interlocked.Increment(ref _auxiliaryFailureCount);
                throw AuxiliaryFailure;
            }

            GitCommandResult result = await inner.RunAsync(
                repository,
                arguments,
                options,
                cancellationToken,
                stderrProgress);
            if (arguments.Count > 1
                && arguments[0] == "remote"
                && arguments[1] is "add" or "set-url")
            {
                Volatile.Write(ref _remoteMutated, 1);
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }

    private sealed class StaticStatusRunner : IGitCliRunner
    {
        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            return Task.FromResult(new GitCommandResult(0, "# branch.head main\0", ""));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class RemoteLockFailureRunner(RepositoryLockInfo lockInfo) : IGitCliRunner
    {
        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            return arguments.FirstOrDefault() == "push"
                ? Task.FromException<GitCommandResult>(new GitOperationException(
                    128,
                    $"fatal: Unable to create '{lockInfo.LockPath}': File exists.\n"))
                : Task.FromResult(new GitCommandResult(0, "# branch.head main\0", ""));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => lockInfo;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo candidate)
            => false;
    }

    private sealed class RecordingInitializationRunner : IGitCliRunner
    {
        public List<string> Commands { get; } = [];

        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add(string.Join(' ', arguments));
            if (arguments.FirstOrDefault() == "rev-parse")
            {
                throw new GitOperationException(
                    128,
                    "fatal: not a git repository (or any of the parent directories): .git\n");
            }

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

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed class MismatchedDiscoveryRunner(string discoveredRoot) : IGitCliRunner
    {
        public List<string> Commands { get; } = [];

        public bool HasActiveProcess => false;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Add(string.Join(' ', arguments));
            return Task.FromResult(
                arguments.FirstOrDefault() == "rev-parse"
                    ? new GitCommandResult(0, $"{discoveredRoot}\n\n", "")
                    : new GitCommandResult(0, "", ""));
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => null;

        public bool RemoveRecoverableRepositoryLock(
            RepositoryInfo repository,
            RepositoryLockInfo lockInfo)
            => false;
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);

    private sealed class RecordingLogger : ILogger
    {
        private readonly TaskCompletionSource<LogEntry> _entry =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LogEntry> Entry => _entry.Task;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entry.TrySetResult(
                new LogEntry(logLevel, exception, formatter(state, exception)));
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
