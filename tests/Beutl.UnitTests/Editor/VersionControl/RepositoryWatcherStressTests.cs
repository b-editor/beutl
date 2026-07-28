using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class RepositoryWatcherStressTests : RealGitTestRepository
{
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
            bool networkOperation,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            if (arguments.FirstOrDefault() == "status")
            {
                Interlocked.Increment(ref _statusCallCount);
                string? optionalLocks = inner
                    .CreateStartInfo(repository, arguments, networkOperation)
                    .Environment["GIT_OPTIONAL_LOCKS"];
                if (optionalLocks != "0")
                {
                    Interlocked.Increment(ref _statusCallsWithoutOptionalLocks);
                }
            }

            return inner.RunAsync(
                repository,
                arguments,
                networkOperation,
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
