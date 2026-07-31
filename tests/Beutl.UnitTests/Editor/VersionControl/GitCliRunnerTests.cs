using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class GitCliRunnerTests : RealGitTestRepository
{
    [Test]
    public void CreateStartInfo_uses_argument_list_and_required_environment()
    {
        var runner = new GitCliRunner(GitPath);

        var startInfo = runner.CreateStartInfo(
            Repository,
            ["show", "--format=value with spaces", "HEAD"],
            GitExecutionPolicy.Local);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.RedirectStandardInput, Is.True);
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(Repository.RepoRoot));
            Assert.That(startInfo.ArgumentList,
                Is.EqualTo(new[] { "show", "--format=value with spaces", "HEAD" }));
            Assert.That(startInfo.Environment["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
            Assert.That(startInfo.Environment["GIT_OPTIONAL_LOCKS"], Is.EqualTo("0"));
            Assert.That(startInfo.Environment["GIT_LITERAL_PATHSPECS"], Is.EqualTo("1"));
            Assert.That(startInfo.Environment["LC_ALL"], Is.EqualTo("C"));
            Assert.That(startInfo.Environment.TryGetValue("GIT_CONFIG_GLOBAL", out string? globalConfig),
                Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL") is not null));
            Assert.That(globalConfig, Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL")));
            Assert.That(startInfo.Environment.TryGetValue("GIT_CONFIG_NOSYSTEM", out string? noSystemConfig),
                Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM") is not null));
            Assert.That(noSystemConfig, Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM")));
        });
    }

    [Test]
    public async Task CreateStartInfo_adds_batch_mode_for_default_open_ssh()
    {
        GitCliRunner runner = CreateSshIsolatedRunner();

        var startInfo = await runner.CreateStartInfoAsync(
            Repository,
            ["push", "origin", "HEAD"],
            GitCommandOptions.Network);

        Assert.That(
            startInfo.Environment["GIT_SSH_COMMAND"],
            Is.EqualTo("ssh -oBatchMode=yes"));
    }

    [TestCase("GIT_SSH_COMMAND", "custom-ssh --identity test-key")]
    [TestCase("GIT_SSH", "custom-ssh")]
    [TestCase("GIT_SSH_VARIANT", "plink")]
    public async Task CreateStartInfo_preserves_inherited_ssh_environment(
        string variable,
        string value)
    {
        Dictionary<string, string?> environment = CreateSshIsolatedEnvironment();
        environment[variable] = value;
        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            environment);

        var startInfo = await runner.CreateStartInfoAsync(
            Repository,
            ["push", "origin", "HEAD"],
            GitCommandOptions.Network);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.Environment[variable], Is.EqualTo(value));
            if (variable != "GIT_SSH_COMMAND")
            {
                Assert.That(startInfo.Environment.ContainsKey("GIT_SSH_COMMAND"), Is.False);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateStartInfo_preserves_repository_or_global_core_ssh_command(
        bool useGlobalConfig)
    {
        const string customSshCommand = "custom-ssh-wrapper --nonstandard-option";
        Dictionary<string, string?> environment = CreateSshIsolatedEnvironment();
        if (useGlobalConfig)
        {
            string globalConfigPath = Path.Combine(CreateTemporaryDirectory(), "gitconfig");
            await RunGitAsync(
                "config",
                "--file",
                globalConfigPath,
                "core.sshCommand",
                customSshCommand);
            environment["GIT_CONFIG_GLOBAL"] = globalConfigPath;
        }
        else
        {
            await RunGitAsync("config", "core.sshCommand", customSshCommand);
        }

        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            environment);

        var startInfo = await runner.CreateStartInfoAsync(
            Repository,
            ["push", "origin", "HEAD"],
            GitCommandOptions.Network);

        Assert.That(startInfo.Environment.ContainsKey("GIT_SSH_COMMAND"), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateStartInfo_preserves_repository_or_global_ssh_variant(
        bool useGlobalConfig)
    {
        Dictionary<string, string?> environment = CreateSshIsolatedEnvironment();
        if (useGlobalConfig)
        {
            string globalConfigPath = Path.Combine(CreateTemporaryDirectory(), "gitconfig");
            await RunGitAsync(
                "config",
                "--file",
                globalConfigPath,
                "ssh.variant",
                "plink");
            environment["GIT_CONFIG_GLOBAL"] = globalConfigPath;
        }
        else
        {
            await RunGitAsync("config", "ssh.variant", "plink");
        }

        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            environment);

        var startInfo = await runner.CreateStartInfoAsync(
            Repository,
            ["push", "origin", "HEAD"],
            GitCommandOptions.Network);

        Assert.That(startInfo.Environment.ContainsKey("GIT_SSH_COMMAND"), Is.False);
    }

    [Test]
    public async Task Per_command_environment_overrides_constructor_environment()
    {
        const string constructorIndex = "/constructor/index";
        const string commandIndex = "/command/index";
        Dictionary<string, string?> environment = CreateSshIsolatedEnvironment();
        environment["GIT_INDEX_FILE"] = constructorIndex;
        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            environment);
        var options = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = commandIndex,
                ["GIT_TERMINAL_PROMPT"] = "1",
            });

        var startInfo = await runner.CreateStartInfoAsync(
            Repository,
            ["status"],
            options);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.Environment["GIT_INDEX_FILE"], Is.EqualTo(commandIndex));
            Assert.That(startInfo.Environment["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
        });
    }

    [Test]
    public void SplitNullSeparated_preserves_spaces_and_omits_terminal_empty_record()
    {
        IReadOnlyList<string> values = GitCliRunner.SplitNullSeparated("one\0two words\0three\0");

        Assert.That(values, Is.EqualTo(new[] { "one", "two words", "three" }));
    }

    [Test]
    public async Task RunAsync_writes_explicit_null_separated_standard_input()
    {
        string excludePath = Path.Combine(Root, ".git", "info", "exclude");
        await File.AppendAllTextAsync(excludePath, "ignored-*\nignored dir/\n");

        GitCommandResult result = await Runner.RunAsync(
            Repository,
            ["check-ignore", "--stdin", "-z"],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                StandardInput: "ignored-file\0ignored dir/file.txt\0visible.txt\0",
                UseLiteralPathspecs: false),
            CancellationToken.None);

        Assert.That(
            GitCliRunner.SplitNullSeparated(result.Stdout),
            Is.EqualTo(new[] { "ignored-file", "ignored dir/file.txt" }));
    }

    [Test]
    public async Task RunAsync_closes_standard_input_when_payload_is_null()
    {
        GitCommandResult result = await Runner.RunAsync(
            Repository,
            ["hash-object", "--stdin"],
            GitCommandOptions.Local,
            CancellationToken.None);

        Assert.That(
            result.Stdout.Trim(),
            Is.EqualTo("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391"));
    }

    [Test]
    public async Task Progress_reader_reports_carriage_return_updates_and_preserves_stderr()
    {
        const string stderr = "Counting objects: 10%\rCounting objects: 100%\r\nDone\n";
        var progress = new RecordingProgress();

        string captured = await GitCliRunner.ReadStandardErrorAsync(
            new StringReader(stderr),
            progress);

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.EqualTo(stderr));
            Assert.That(
                progress.Messages,
                Is.EqualTo(new[]
                {
                    "Counting objects: 10%",
                    "Counting objects: 100%",
                    "Done",
                }));
        });
    }

    [Test]
    public async Task Progress_reader_redacts_url_credentials_before_reporting_or_returning_stderr()
    {
        const string secret = "super-secret-token";
        string stderr =
            $"fatal: Authentication failed for 'https://user:{secret}@example.invalid/repo.git/'\n";
        var progress = new RecordingProgress();

        string captured = await GitCliRunner.ReadStandardErrorAsync(
            new StringReader(stderr),
            progress);

        Assert.Multiple(() =>
        {
            Assert.That(captured, Does.Not.Contain(secret));
            Assert.That(progress.Messages, Has.Count.EqualTo(1));
            Assert.That(progress.Messages[0], Does.Not.Contain(secret));
            Assert.That(
                progress.Messages[0],
                Does.Contain("https://***@example.invalid/repo.git/"));
        });
    }

    [Test]
    public void Nonzero_exit_throws_typed_error_with_verbatim_stderr()
    {
        GitOperationException? exception = Assert.ThrowsAsync<GitOperationException>(
            async () => await RunGitAsync("rev-parse", "--verify", "definitely-not-a-ref"));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.ExitCode, Is.Not.Zero);
            Assert.That(exception.Stderr, Is.Not.Empty.And.EndsWith("\n"));
        });
    }

    [Test]
    public void Cancellation_kills_a_waiting_git_process()
    {
        var runner = CreateRunner(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await runner.RunAsync(
                Repository,
                ["-c", "alias.wait=!printf output; sleep 30", "wait"],
                GitCommandOptions.Local with { MaxStdoutBytes = 1 },
                cancellation.Token));
        Assert.That(runner.HasActiveProcess, Is.False);
    }

    [Test]
    public async Task Standard_input_is_closed_after_process_start()
    {
        var runner = CreateRunner(TimeSpan.FromSeconds(1));

        GitCommandResult result = await runner.RunAsync(
            Repository,
            ["cat-file", "--batch"],
            GitCommandOptions.Local,
            CancellationToken.None);

        Assert.That(result.ExitCode, Is.Zero);
    }

    [Test]
    public async Task Stdout_byte_limit_drains_the_process_and_omits_partial_utf8_sequence()
    {
        string contents = string.Concat(Enumerable.Repeat("あ", 100_000));
        await CommitFileAsync("large.txt", contents, "large output");
        var runner = CreateRunner(TimeSpan.FromSeconds(10));
        var options = GitCommandOptions.Local with { MaxStdoutBytes = 5 };

        GitCommandResult result = await runner.RunAsync(
            Repository,
            ["show", "HEAD:large.txt"],
            options,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Is.EqualTo("あ"));
            Assert.That(result.Stdout, Does.Not.Contain('\uFFFD'));
            Assert.That(result.StdoutTruncated, Is.True);
            Assert.That(runner.HasActiveProcess, Is.False);
        });
    }

    [Test]
    public async Task Stale_repository_lock_is_recoverable_only_after_explicit_removal()
    {
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        await File.WriteAllTextAsync(Path.Combine(Root, "locked.txt"), "locked");
        string lockPath = Path.Combine(Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "");
        File.SetLastWriteTimeUtc(
            lockPath,
            (now - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1)).UtcDateTime);
        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            IsolatedGitEnvironment,
            timeProvider);
        GitRepositoryLockEventArgs? eventArgs = null;
        runner.RepositoryLockFailed += (_, e) => eventArgs = e;

        GitOperationException? exception = Assert.ThrowsAsync<GitOperationException>(
            async () => await runner.RunAsync(
                Repository,
                ["add", "--", "locked.txt"],
                GitCommandOptions.Local,
                CancellationToken.None));
        RepositoryLockInfo? recoverable = runner.GetRecoverableRepositoryLock(Repository);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.IsRepositoryLockFailure, Is.True);
            Assert.That(eventArgs, Is.Not.Null);
            Assert.That(eventArgs!.Repository, Is.EqualTo(Repository));
            Assert.That(recoverable, Is.Not.Null);
            Assert.That(File.Exists(lockPath), Is.True);
        });

        Assert.That(
            runner.RemoveRecoverableRepositoryLock(Repository, recoverable!),
            Is.True);
        Assert.That(File.Exists(lockPath), Is.False);
    }

    [Test]
    public async Task Recent_or_active_repository_lock_is_not_recoverable()
    {
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        string lockPath = Path.Combine(Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "");
        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            IsolatedGitEnvironment,
            timeProvider);

        Assert.That(runner.GetRecoverableRepositoryLock(Repository), Is.Null);

        File.SetLastWriteTimeUtc(
            lockPath,
            (now - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1)).UtcDateTime);
        using var cancellation = new CancellationTokenSource();
        Task<GitCommandResult> activeCommand = runner.RunAsync(
            Repository,
            ["-c", "alias.wait=!sleep 30", "wait"],
            GitCommandOptions.Local,
            cancellation.Token);
        for (int attempt = 0; attempt < 100 && !runner.HasActiveProcess; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.That(runner.HasActiveProcess, Is.True);
        Assert.That(runner.GetRecoverableRepositoryLock(Repository), Is.Null);

        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await activeCommand);
        Assert.That(runner.GetRecoverableRepositoryLock(Repository), Is.Not.Null);
    }

    [Test]
    public async Task Stale_linked_worktree_HEAD_lock_is_removed_only_with_matching_identity()
    {
        await CommitFileAsync("project.beutl", "base", "initial");
        string worktreeRoot = CreateTemporaryDirectory();
        await RunGitAsync("worktree", "add", "-b", "linked", worktreeRoot);
        var worktree = new RepositoryInfo(worktreeRoot, worktreeRoot);
        string gitFile = await File.ReadAllTextAsync(Path.Combine(worktreeRoot, ".git"));
        string gitDirectory = gitFile["gitdir:".Length..].Trim();
        if (!Path.IsPathFullyQualified(gitDirectory))
        {
            gitDirectory = Path.Combine(worktreeRoot, gitDirectory);
        }

        string lockPath = Path.Combine(Path.GetFullPath(gitDirectory), "HEAD.lock");
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        await File.WriteAllTextAsync(lockPath, "stale");
        DateTime firstTimestamp = (now - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(2)).UtcDateTime;
        File.SetLastWriteTimeUtc(lockPath, firstTimestamp);
        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            IsolatedGitEnvironment,
            timeProvider);

        RepositoryLockInfo first = runner.GetRecoverableRepositoryLock(worktree)!;
        Assert.That(first.LockPath, Is.EqualTo(Path.GetFullPath(lockPath)));

        DateTime replacementTimestamp = firstTimestamp.AddMinutes(1);
        File.SetLastWriteTimeUtc(lockPath, replacementTimestamp);
        Assert.That(
            runner.RemoveRecoverableRepositoryLock(worktree, first),
            Is.False);
        Assert.That(File.Exists(lockPath), Is.True);

        RepositoryLockInfo replacement = runner.GetRecoverableRepositoryLock(worktree)!;
        Assert.That(
            runner.RemoveRecoverableRepositoryLock(worktree, replacement),
            Is.True);
        Assert.That(File.Exists(lockPath), Is.False);
    }

    [Test]
    public void Unreadable_git_file_does_not_hide_the_original_git_failure()
    {
        string worktreeRoot = CreateTemporaryDirectory();
        File.WriteAllText(
            Path.Combine(worktreeRoot, ".git"),
            $"gitdir: {Path.Combine(Root, ".git")}");
        var worktree = new RepositoryInfo(worktreeRoot, worktreeRoot);
        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            IsolatedGitEnvironment,
            readAllText: _ => throw new IOException("simulated read failure"));

        Assert.That(runner.GetRecoverableRepositoryLock(worktree), Is.Null);
    }

    [Test]
    public async Task Failed_stale_lock_deletion_is_reported_without_throwing()
    {
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        string lockPath = Path.Combine(Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "");
        File.SetLastWriteTimeUtc(
            lockPath,
            (now - GitCliRunner.StaleLockAge - TimeSpan.FromMinutes(1)).UtcDateTime);
        var runner = new GitCliRunner(
            GitPath,
            TimeSpan.FromSeconds(10),
            IsolatedGitEnvironment,
            timeProvider,
            deleteFile: _ => throw new IOException("simulated delete failure"));
        RepositoryLockInfo lockInfo = runner.GetRecoverableRepositoryLock(Repository)!;

        Assert.Multiple(() =>
        {
            Assert.That(
                runner.RemoveRecoverableRepositoryLock(Repository, lockInfo),
                Is.False);
            Assert.That(File.Exists(lockPath), Is.True);
        });
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value)
        {
            Messages.Add(value);
        }
    }

    private GitCliRunner CreateSshIsolatedRunner()
        => new(
            GitPath,
            TimeSpan.FromSeconds(10),
            CreateSshIsolatedEnvironment());

    private static Dictionary<string, string?> CreateSshIsolatedEnvironment()
    {
        var environment = IsolatedGitEnvironment.ToDictionary(
            static pair => pair.Key,
            static pair => (string?)pair.Value);
        environment["GIT_SSH_COMMAND"] = null;
        environment["GIT_SSH"] = null;
        environment["GIT_SSH_VARIANT"] = null;
        return environment;
    }
}
