using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class RepositoryWatcherStressTests : RealGitTestRepository
{
    [Test]
    public async Task External_index_and_commit_updates_raise_changes()
    {
        string projectPath = Path.Combine(Root, "project.bep");
        await CommitFileAsync("project.bep", "{\"value\":0}\n", "baseline");
        await File.WriteAllTextAsync(projectPath, "{\"value\":1}\n");

        using (var indexWatcher = new RepositoryWatcher(Root))
        {
            await AssertChangedDuringAsync(
                indexWatcher,
                () => RunGitAndAssertSuccessAsync(Repository, "add", "--", "project.bep"));
        }

        using (var commitWatcher = new RepositoryWatcher(Root))
        {
            await AssertChangedDuringAsync(
                commitWatcher,
                () => RunGitAndAssertSuccessAsync(Repository, "commit", "-m", "external commit"));
        }
    }

    [Test]
    public async Task Linked_worktree_watches_its_index_and_the_common_ref_store()
    {
        await CommitFileAsync("project.bep", "{\"value\":0}\n", "baseline");
        string linkedRoot = CreateTemporaryDirectory();
        Directory.Delete(linkedRoot);
        await RunGitAndAssertSuccessAsync(
            Repository,
            "worktree",
            "add",
            "-b",
            "linked",
            linkedRoot);
        var linkedRepository = new RepositoryInfo(linkedRoot, linkedRoot);
        await File.WriteAllTextAsync(
            Path.Combine(linkedRoot, "project.bep"),
            "{\"value\":1}\n");

        using (var indexWatcher = new RepositoryWatcher(linkedRoot))
        {
            await AssertChangedDuringAsync(
                indexWatcher,
                () => RunGitAndAssertSuccessAsync(
                    linkedRepository,
                    "add",
                    "--",
                    "project.bep"));
        }

        using (var commonRefsWatcher = new RepositoryWatcher(linkedRoot))
        {
            await AssertChangedDuringAsync(
                commonRefsWatcher,
                () => RunGitAndAssertSuccessAsync(
                    Repository,
                    "update-ref",
                    "refs/heads/external-update",
                    "HEAD"));
        }
    }

    [Test]
    public async Task Rapid_write_burst_has_bounded_status_refreshes_without_self_feedback()
    {
        const int writeCount = 1000;
        const int maximumStatusCalls = 25;
        string projectPath = Path.Combine(Root, "project.bep");
        await CommitFileAsync("project.bep", "{\"value\":0}\n", "baseline");

        var gitRunner = CreateRunner(TimeSpan.FromSeconds(30));
        var countingRunner = new StatusCountingRunner(gitRunner);
        using var watcher = new RepositoryWatcher(Root);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher,
            _ => countingRunner);
        var firstRefresh = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += (_, _) => firstRefresh.TrySetResult();

        for (int index = 1; index <= writeCount; index++)
        {
            File.WriteAllText(projectPath, $"{{\"value\":{index}}}\n");
        }

        await firstRefresh.Task.WaitAsync(TimeSpan.FromSeconds(15));
        int settledStatusCalls = await WaitForStatusCallsToSettleAsync(
            countingRunner,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(15));
        TestContext.Progress.WriteLine(
            $"R-8 watcher burst: {writeCount} writes produced {settledStatusCalls} git status call(s).");

        Assert.Multiple(() =>
        {
            Assert.That(settledStatusCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                settledStatusCalls,
                Is.LessThanOrEqualTo(maximumStatusCalls),
                $"{writeCount} writes must be coalesced to well under 100 status calls.");
            Assert.That(
                countingRunner.AllStatusCallsDisabledOptionalLocks,
                Is.True,
                "Every status process must set GIT_OPTIONAL_LOCKS=0.");
        });

        await Task.Delay(RepositoryWatcher.DebounceInterval + TimeSpan.FromSeconds(1));

        Assert.That(
            countingRunner.StatusCallCount,
            Is.EqualTo(settledStatusCalls),
            "Reading status must not generate a watcher event that schedules another status call.");
    }

    private static async Task AssertChangedDuringAsync(
        RepositoryWatcher watcher,
        Func<Task> operation)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += OnChanged;
        try
        {
            await operation();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            watcher.Changed -= OnChanged;
        }

        void OnChanged(object? sender, EventArgs args)
        {
            completion.TrySetResult();
        }
    }

    private async Task RunGitAndAssertSuccessAsync(
        RepositoryInfo repository,
        params string[] arguments)
    {
        GitCommandResult result = await Runner.RunAsync(
            repository,
            arguments,
            GitCommandOptions.Local,
            CancellationToken.None);
        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
    }

    private static async Task<int> WaitForStatusCallsToSettleAsync(
        StatusCountingRunner runner,
        TimeSpan quietPeriod,
        TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        int previousCount = runner.StatusCallCount;
        DateTime quietSince = DateTime.UtcNow;

        while (DateTime.UtcNow - quietSince < quietPeriod)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeoutCts.Token);
            int currentCount = runner.StatusCallCount;
            if (currentCount != previousCount)
            {
                previousCount = currentCount;
                quietSince = DateTime.UtcNow;
            }
        }

        return previousCount;
    }

    private sealed class StatusCountingRunner(GitCliRunner inner) : IGitCliRunner
    {
        private int _statusCallCount;
        private int _statusCallsWithoutOptionalLocks;

        public int StatusCallCount => Volatile.Read(ref _statusCallCount);

        public bool AllStatusCallsDisabledOptionalLocks
            => Volatile.Read(ref _statusCallsWithoutOptionalLocks) == 0;

        public bool HasActiveProcess => inner.HasActiveProcess;

        public Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "status")
            {
                Interlocked.Increment(ref _statusCallCount);
                bool foundOptionalLocks = inner
                    .CreateStartInfo(repository, arguments, GitExecutionPolicy.Local)
                    .Environment
                    .TryGetValue("GIT_OPTIONAL_LOCKS", out string? optionalLocks);
                if (!foundOptionalLocks || optionalLocks != "0")
                {
                    Interlocked.Increment(ref _statusCallsWithoutOptionalLocks);
                }
            }

            return inner.RunAsync(
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
}
