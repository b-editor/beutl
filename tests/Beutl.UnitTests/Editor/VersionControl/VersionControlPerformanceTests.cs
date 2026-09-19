using System.Collections.Concurrent;
using System.Diagnostics;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlPerformanceTests : RealGitTestRepository
{
    private static readonly TimeSpan s_snapshotLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_historyLimit = TimeSpan.FromSeconds(5);

    [TestCase("main")]
    [TestCase("release")]
    public async Task Origin_tracking_status_reuses_counts_without_walking_history_again(string branch)
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("remote", "add", "origin", "https://example.invalid/repository.git");
        await RunGitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        if (branch != "main")
        {
            await RunGitAsync("switch", "-c", branch);
        }

        await RunGitAsync("branch", "--set-upstream-to=origin/main", branch);
        await RunGitAsync("config", "status.aheadBehind", "false");
        await CommitFileAsync("project.bep", "changed\n", "local change");
        var runner = new StatusCountingRunner(CreateRunner());
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(), Repository, watcher: null, _ => runner);

        WorkspaceStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.Branch, Is.EqualTo(branch));
            Assert.That(status.Ahead, Is.EqualTo(1));
            Assert.That(status.Behind, Is.Zero);
            Assert.That(runner.Commands, Has.Count.EqualTo(3));
            Assert.That(runner.Commands.Any(command => command[0] == "rev-list"), Is.False);
        });
    }

    [Test]
    public async Task Branch_list_uses_one_command_and_preserves_origin_only_branches()
    {
        await CommitFileAsync("project.bep", "baseline\n", "baseline");
        await RunGitAsync("branch", "feature");
        await RunGitAsync("update-ref", "refs/remotes/origin/feature", "HEAD");
        await RunGitAsync("update-ref", "refs/remotes/origin/remote-only", "HEAD");
        await RunGitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/feature");
        var runner = new StatusCountingRunner(CreateRunner());
        using var service = new GitCliVersionControlService(CreateInstalledLocator(), Repository, null, _ => runner);
        IReadOnlyList<BranchInfo> branches = await service.GetBranchesAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(runner.Commands.Count, Is.EqualTo(1));
            Assert.That(branches.Select(branch => branch.Name), Is.EqualTo(new[] { "feature", "main", "remote-only" }));
            Assert.That(branches.Single(branch => branch.Name == "main").IsCurrent, Is.True);
            Assert.That(branches.Last().IsRemote, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Watcher_burst_queues_only_one_refresh_behind_an_active_status(bool watcherStartsFirst)
    {
        var timeProvider = new FakeTimeProvider();
        var watcher = new RepositoryWatcher(Repository, timeProvider, startWatching: false);
        var runner = new StatusCountingRunner(CreateRunner(), blockFirstStatus: true);
        using var service = new GitCliVersionControlService(
            CreateInstalledLocator(), Repository, watcher, _ => runner,
            statusNotificationScheduler: action => action());
        var latestNotification = new TaskCompletionSource<WorkspaceStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += (_, status) =>
        {
            if (status.Changes.Any(change => change.Path == "latest.bep"))
            {
                latestNotification.TrySetResult(status);
            }
        };
        Task<WorkspaceStatus>? initialStatus = null;
        if (watcherStartsFirst)
        {
            await NotifyWatcherAsync();
        }
        else
        {
            initialStatus = service.GetStatusAsync(CancellationToken.None);
        }

        await runner.FirstStatusStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(Root, "latest.bep"), "{}\n");
            for (int i = 0; i < 20; i++)
            {
                await NotifyWatcherAsync();
            }
        }
        finally
        {
            runner.ReleaseFirstStatus.TrySetResult();
        }

        if (initialStatus is not null)
        {
            await initialStatus;
        }

        WorkspaceStatus latest = await latestNotification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // A normal read queued behind the notifications must not wait for one status per event.
        await service.GetRemotesAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(runner.StatusCount, Is.EqualTo(2));
            Assert.That(latest.Changes,
                Does.Contain(new FileChange("latest.bep", FileChangeStatus.Added)));
        });

        async Task NotifyWatcherAsync()
        {
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler handler = (_, _) => delivered.TrySetResult();
            watcher.Changed += handler;
            try
            {
                watcher.NotifyPathChanged(Path.Combine(Root, "latest.bep"));
                timeProvider.Advance(RepositoryWatcher.DebounceInterval);
                await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                watcher.Changed -= handler;
            }
        }
    }

    [Test]
    public async Task Snapshot_of_500_element_project_completes_within_bound()
    {
        const int elementCount = 500;
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), """{"name":"Performance fixture"}""" + "\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "main.scene"), """{"elements":[]}""" + "\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("commit", "-m", "project baseline");

        string elementsDirectory = Path.Combine(Root, "elements");
        Directory.CreateDirectory(elementsDirectory);
        var elementNames = new string[elementCount];
        for (int index = 0; index < elementCount; index++)
        {
            string id = (index + 1).ToString("x32");
            string fileName = $"{id}.belm";
            elementNames[index] = fileName;
            await File.WriteAllTextAsync(
                Path.Combine(elementsDirectory, fileName),
                $"{{\"id\":\"{id}\",\"opacity\":1.0,\"position\":{{\"x\":{index},\"y\":{index}}}}}\n");
        }

        string elementReferences = string.Join(
            ',',
            elementNames.Select(static name => $"\"elements/{name}\""));
        await File.WriteAllTextAsync(
            Path.Combine(Root, "main.scene"),
            $"{{\"elements\":[{elementReferences}]}}\n");
        using var service = CreateService();

        var stopwatch = Stopwatch.StartNew();
        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        stopwatch.Stop();
        TestContext.Progress.WriteLine(
            $"SC-003 snapshot of {elementCount} elements: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(s_snapshotLimit),
                $"A {elementCount}-element snapshot took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
        });
    }

    [Test]
    public async Task Loading_200_commit_history_completes_within_bound()
    {
        const int requestedCommitCount = 200;
        await CommitFileAsync("project.bep", "0\n", "history baseline");
        for (int index = 1; index <= requestedCommitCount; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), $"{index}\n");
            await RunGitAsync("commit", "-am", $"history {index}");
        }

        using var service = CreateService();
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<CommitInfo> history = await service.GetHistoryAsync(
            0,
            requestedCommitCount,
            CancellationToken.None);
        stopwatch.Stop();
        TestContext.Progress.WriteLine(
            $"SC-003 history load of {requestedCommitCount} commits: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Multiple(() =>
        {
            Assert.That(history, Has.Count.EqualTo(requestedCommitCount));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(s_historyLimit),
                $"Loading {requestedCommitCount} commits took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
        });
    }

    private GitCliVersionControlService CreateService()
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(TimeSpan.FromSeconds(30)));
    }

    private sealed class StatusCountingRunner(IGitCliRunner inner, bool blockFirstStatus = false)
        : IGitCliRunner
    {
        private int _statusCount;

        public ConcurrentQueue<string[]> Commands { get; } = new();
        public int StatusCount => Volatile.Read(ref _statusCount);
        public bool HasActiveProcess => inner.HasActiveProcess;
        public TaskCompletionSource FirstStatusStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstStatus { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GitCommandResult> RunAsync(
            RepositoryInfo repository,
            IReadOnlyList<string> arguments,
            GitCommandOptions options,
            CancellationToken cancellationToken,
            IProgress<string>? stderrProgress = null)
        {
            Commands.Enqueue([.. arguments]);
            GitCommandResult result = await inner.RunAsync(
                repository, arguments, options, cancellationToken, stderrProgress);
            if (arguments[0] == "status" && Interlocked.Increment(ref _statusCount) == 1)
            {
                FirstStatusStarted.TrySetResult();
                if (blockFirstStatus)
                {
                    await ReleaseFirstStatus.Task.WaitAsync(cancellationToken);
                }
            }

            return result;
        }

        public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
            => inner.GetRecoverableRepositoryLock(repository);

        public bool RemoveRecoverableRepositoryLock(RepositoryInfo repository, RepositoryLockInfo lockInfo)
            => inner.RemoveRecoverableRepositoryLock(repository, lockInfo);
    }
}
