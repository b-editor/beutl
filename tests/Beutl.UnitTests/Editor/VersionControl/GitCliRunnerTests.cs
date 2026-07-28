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
            networkOperation: true);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(Repository.RepoRoot));
            Assert.That(startInfo.ArgumentList,
                Is.EqualTo(new[] { "show", "--format=value with spaces", "HEAD" }));
            Assert.That(startInfo.Environment["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
            Assert.That(startInfo.Environment["GIT_OPTIONAL_LOCKS"], Is.EqualTo("0"));
            Assert.That(startInfo.Environment["LC_ALL"], Is.EqualTo("C"));
            Assert.That(startInfo.Environment["GIT_SSH_COMMAND"], Is.EqualTo("ssh -oBatchMode=yes"));
            Assert.That(startInfo.Environment.TryGetValue("GIT_CONFIG_GLOBAL", out string? globalConfig),
                Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL") is not null));
            Assert.That(globalConfig, Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL")));
            Assert.That(startInfo.Environment.TryGetValue("GIT_CONFIG_NOSYSTEM", out string? noSystemConfig),
                Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM") is not null));
            Assert.That(noSystemConfig, Is.EqualTo(Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM")));
        });
    }

    [Test]
    public void SplitNullSeparated_preserves_spaces_and_omits_terminal_empty_record()
    {
        IReadOnlyList<string> values = GitCliRunner.SplitNullSeparated("one\0two words\0three\0");

        Assert.That(values, Is.EqualTo(new[] { "one", "two words", "three" }));
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
                ["cat-file", "--batch"],
                networkOperation: false,
                cancellation.Token));
        Assert.That(runner.HasActiveProcess, Is.False);
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
                networkOperation: false,
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
            ["cat-file", "--batch"],
            networkOperation: false,
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

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value)
        {
            Messages.Add(value);
        }
    }
}
