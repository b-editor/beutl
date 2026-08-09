using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class RepositoryWatcherTests
{
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"beutl-watcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [TestCase(".git/index")]
    [TestCase(".git/refs/heads/main")]
    [TestCase(".beutl/view-state.json")]
    [TestCase("scenes/.beutl/output-profile.json")]
    [TestCase("project.bep.abc.tmp")]
    public void Excluded_paths_do_not_schedule_changes(string relativePath)
    {
        string path = Path.Combine(_tempDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.That(RepositoryWatcher.ShouldExcludePath(_tempDirectory, path), Is.True);
    }

    [Test]
    public void Unix_literal_backslashes_do_not_create_metadata_segments()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Backslashes are directory separators on Windows.");
        }

        string projectPath = Path.Combine(_tempDirectory, @".beutl\clip.scene");
        string metadataPath = Path.Combine(_tempDirectory, @"refs\heads\main");

        Assert.Multiple(() =>
        {
            Assert.That(RepositoryWatcher.ShouldExcludePath(_tempDirectory, projectPath), Is.False);
            Assert.That(RepositoryWatcher.ShouldIncludeGitMetadataPath(_tempDirectory, metadataPath), Is.False);
        });
    }

    [TestCase("index", true)]
    [TestCase("HEAD", true)]
    [TestCase("packed-refs", true)]
    [TestCase("refs/heads/main", true)]
    [TestCase("refs/remotes/origin/main", true)]
    [TestCase("reftable/tables.list", true)]
    [TestCase("reftable/0x000000000001-0x000000000002.ref", true)]
    [TestCase("index.lock", false)]
    [TestCase("HEAD.lock", false)]
    [TestCase("packed-refs.lock", false)]
    [TestCase("refs/heads/main.lock", false)]
    [TestCase("reftable/tables.list.lock", false)]
    [TestCase("objects/ab/cdef", false)]
    [TestCase("logs/HEAD", false)]
    [TestCase("config", true)]
    [TestCase("config.worktree", true)]
    [TestCase("info/exclude", true)]
    [TestCase("info/exclude.lock", false)]
    public void Git_metadata_filter_includes_state_and_refs_without_transient_noise(
        string relativePath,
        bool expected)
    {
        string path = Path.Combine(
            _tempDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.That(
            RepositoryWatcher.ShouldIncludeGitMetadataPath(_tempDirectory, path),
            Is.EqualTo(expected));
    }

    [Test]
    public void Start_watches_the_stable_git_metadata_subdirectories()
    {
        string projectRoot = Path.Combine(_tempDirectory, "project");
        string metadataRoot = Path.Combine(_tempDirectory, ".git");
        string infoDirectory = Path.Combine(metadataRoot, "info");
        string reftableDirectory = Path.Combine(metadataRoot, "reftable");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(metadataRoot, "refs"));
        Directory.CreateDirectory(infoDirectory);
        Directory.CreateDirectory(reftableDirectory);
        var watchedDirectories = new List<string>();

        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, projectRoot),
            TimeProvider.System,
            startWatching: true,
            watcherFactory: path =>
            {
                watchedDirectories.Add(Path.GetFullPath(path));
                return new FileSystemWatcher(path);
            },
            watcherEnabler: static _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(
                watchedDirectories,
                Does.Contain(Path.GetFullPath(infoDirectory)));
            Assert.That(
                watchedDirectories,
                Does.Contain(Path.GetFullPath(reftableDirectory)));
        });
    }

    [Test]
    public async Task Git_metadata_subdirectory_recreation_replaces_the_stale_watcher_and_schedules_a_change()
    {
        string projectRoot = Path.Combine(_tempDirectory, "project");
        string metadataRoot = Path.Combine(_tempDirectory, ".git");
        string infoDirectory = Path.Combine(metadataRoot, "info");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(metadataRoot, "refs"));
        Directory.CreateDirectory(infoDirectory);
        var timeProvider = new FakeTimeProvider();
        var createdWatchers = new List<TrackingFileSystemWatcher>();
        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, projectRoot),
            timeProvider,
            startWatching: true,
            watcherFactory: path =>
            {
                var created = new TrackingFileSystemWatcher(path);
                createdWatchers.Add(created);
                return created;
            },
            watcherEnabler: static _ => { });
        TrackingFileSystemWatcher metadataWatcher = createdWatchers.Single(created =>
            !created.IncludeSubdirectories
            && PathsEqual(created.Path, metadataRoot));
        TrackingFileSystemWatcher originalInfoWatcher = createdWatchers.Single(created =>
            !created.IncludeSubdirectories
            && PathsEqual(created.Path, infoDirectory));
        int raised = 0;
        var firstChange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondChange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) =>
        {
            int count = Interlocked.Increment(ref raised);
            (count == 1 ? firstChange : secondChange).TrySetResult();
        };

        Directory.Delete(infoDirectory, recursive: true);
        metadataWatcher.RaiseDeleted("info");
        Directory.CreateDirectory(infoDirectory);
        await File.WriteAllTextAsync(Path.Combine(infoDirectory, "exclude"), "*.tmp\n");
        metadataWatcher.RaiseCreated("info");
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        await firstChange.Task.WaitAsync(TimeSpan.FromSeconds(2));

        TrackingFileSystemWatcher replacementInfoWatcher = createdWatchers
            .Where(created => !ReferenceEquals(created, originalInfoWatcher))
            .Single(created =>
                !created.IncludeSubdirectories
                && PathsEqual(created.Path, infoDirectory));
        replacementInfoWatcher.RaiseChanged("exclude");
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        await secondChange.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(originalInfoWatcher.IsDisposed, Is.True);
            Assert.That(replacementInfoWatcher.IsDisposed, Is.False);
            Assert.That(Volatile.Read(ref raised), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Git_metadata_subdirectory_disappearance_during_reattach_still_schedules_a_change()
    {
        string projectRoot = Path.Combine(_tempDirectory, "project");
        string metadataRoot = Path.Combine(_tempDirectory, ".git");
        string infoDirectory = Path.Combine(metadataRoot, "info");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(metadataRoot, "refs"));
        Directory.CreateDirectory(infoDirectory);
        var timeProvider = new FakeTimeProvider();
        var createdWatchers = new List<TrackingFileSystemWatcher>();
        bool deleteInfoDuringReattach = false;
        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, projectRoot),
            timeProvider,
            startWatching: true,
            watcherFactory: path =>
            {
                if (deleteInfoDuringReattach && PathsEqual(path, infoDirectory))
                {
                    Directory.Delete(infoDirectory, recursive: true);
                }

                var created = new TrackingFileSystemWatcher(path);
                createdWatchers.Add(created);
                return created;
            },
            watcherEnabler: static _ => { });
        TrackingFileSystemWatcher metadataWatcher = createdWatchers.Single(created =>
            !created.IncludeSubdirectories
            && PathsEqual(created.Path, metadataRoot));
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => changed.TrySetResult();

        deleteInfoDuringReattach = true;
        Assert.DoesNotThrow(() => metadataWatcher.RaiseChanged("info"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(changed.Task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void Git_metadata_directories_resolve_linked_worktree_admin_and_common_directories()
    {
        string commonDirectory = Path.Combine(_tempDirectory, "main", ".git");
        string gitDirectory = Path.Combine(commonDirectory, "worktrees", "linked");
        string linkedRoot = Path.Combine(_tempDirectory, "linked");
        Directory.CreateDirectory(gitDirectory);
        Directory.CreateDirectory(linkedRoot);
        File.WriteAllText(
            Path.Combine(linkedRoot, ".git"),
            $"gitdir: {gitDirectory}{Environment.NewLine}");
        File.WriteAllText(
            Path.Combine(gitDirectory, "commondir"),
            $"../..{Environment.NewLine}");

        (string GitDirectory, string CommonDirectory)? result
            = RepositoryWatcher.ResolveGitMetadataDirectories(linkedRoot);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Value.GitDirectory, Is.EqualTo(Path.GetFullPath(gitDirectory)));
            Assert.That(result.Value.CommonDirectory, Is.EqualTo(Path.GetFullPath(commonDirectory)));
        });
    }

    [TestCase("", ".gitignore", true)]
    [TestCase("nested", ".gitattributes", false)]
    public async Task Ancestor_rule_file_changes_schedule_a_change(
        string ancestorRelativePath,
        string ruleFileName,
        bool createBeforeWatching)
    {
        string projectRoot = Path.Combine(_tempDirectory, "nested", "project");
        Directory.CreateDirectory(projectRoot);
        string ancestorDirectory = Path.Combine(_tempDirectory, ancestorRelativePath);
        string rulePath = Path.Combine(ancestorDirectory, ruleFileName);
        if (createBeforeWatching)
        {
            await File.WriteAllTextAsync(rulePath, "baseline\n");
        }

        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, projectRoot));
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => completion.TrySetResult();

        await File.WriteAllTextAsync(rulePath, "updated\n");

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
    }

    [TestCase(".gitignore", ".gitignore.backup")]
    [TestCase(".gitattributes.pending", ".gitattributes")]
    public async Task Ancestor_rule_file_renames_schedule_a_change(
        string oldFileName,
        string newFileName)
    {
        string projectRoot = Path.Combine(_tempDirectory, "nested", "project");
        Directory.CreateDirectory(projectRoot);
        string oldPath = Path.Combine(_tempDirectory, oldFileName);
        string newPath = Path.Combine(_tempDirectory, newFileName);
        await File.WriteAllTextAsync(oldPath, "rule\n");
        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, projectRoot));
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => completion.TrySetResult();

        File.Move(oldPath, newPath);

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void Start_failure_disposes_timer_and_every_created_watcher()
    {
        string projectRoot = Path.Combine(_tempDirectory, "nested", "project");
        Directory.CreateDirectory(projectRoot);
        var timeProvider = new TrackingTimeProvider();
        var createdWatchers = new List<TrackingFileSystemWatcher>();
        int enableCount = 0;

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new RepositoryWatcher(
                new RepositoryInfo(_tempDirectory, projectRoot),
                timeProvider,
                startWatching: true,
                watcherFactory: path =>
                {
                    var watcher = new TrackingFileSystemWatcher(path);
                    createdWatchers.Add(watcher);
                    return watcher;
                },
                watcherEnabler: watcher =>
                {
                    if (Interlocked.Increment(ref enableCount) == 3)
                    {
                        throw new InvalidOperationException("Injected watcher startup failure.");
                    }

                    watcher.EnableRaisingEvents = true;
                });
        });

        bool timerWasDisposed = timeProvider.Timer?.IsDisposed == true;
        bool[] watcherDisposal = createdWatchers.Select(watcher => watcher.IsDisposed).ToArray();
        timeProvider.Timer?.Dispose();
        foreach (TrackingFileSystemWatcher watcher in createdWatchers)
        {
            watcher.Dispose();
        }

        Assert.Multiple(() =>
        {
            Assert.That(createdWatchers, Has.Count.EqualTo(3));
            Assert.That(timerWasDisposed, Is.True);
            Assert.That(watcherDisposal, Is.All.True);
        });
    }

    [Test]
    public async Task Debounce_coalesces_a_burst_at_exactly_500_milliseconds()
    {
        var timeProvider = new FakeTimeProvider();
        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, _tempDirectory),
            timeProvider,
            startWatching: false);
        int raised = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) =>
        {
            Interlocked.Increment(ref raised);
            completion.TrySetResult();
        };

        watcher.NotifyPathChanged(Path.Combine(_tempDirectory, "first.belm"));
        timeProvider.Advance(TimeSpan.FromMilliseconds(300));
        watcher.NotifyPathChanged(Path.Combine(_tempDirectory, "second.belm"));
        timeProvider.Advance(TimeSpan.FromMilliseconds(499));
        Assert.That(Volatile.Read(ref raised), Is.Zero);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(Volatile.Read(ref raised), Is.EqualTo(1));
    }

    [Test]
    public async Task Changed_is_raised_on_a_background_thread()
    {
        var timeProvider = new FakeTimeProvider();
        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, _tempDirectory),
            timeProvider,
            startWatching: false);
        int callerThread = Environment.CurrentManagedThreadId;
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => completion.TrySetResult(Environment.CurrentManagedThreadId);

        watcher.NotifyPathChanged(Path.Combine(_tempDirectory, "project.bep"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);
        int eventThread = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(eventThread, Is.Not.EqualTo(callerThread));
    }

    [Test]
    public async Task Rename_from_tracked_path_to_excluded_path_still_schedules_a_change()
    {
        var timeProvider = new FakeTimeProvider();
        using var watcher = new RepositoryWatcher(
            new RepositoryInfo(_tempDirectory, _tempDirectory),
            timeProvider,
            startWatching: false);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => completion.TrySetResult();

        watcher.NotifyPathRenamed(
            Path.Combine(_tempDirectory, "scene.belm"),
            Path.Combine(_tempDirectory, "scene.belm.tmp"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
    }

    private sealed class TrackingFileSystemWatcher(string path) : FileSystemWatcher(path)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public void RaiseChanged(string name)
        {
            OnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, Path, name));
        }

        public void RaiseCreated(string name)
        {
            OnCreated(new FileSystemEventArgs(WatcherChangeTypes.Created, Path, name));
        }

        public void RaiseDeleted(string name)
        {
            OnDeleted(new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path, name));
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private sealed class TrackingTimeProvider : TimeProvider
    {
        public TrackingTimer? Timer { get; private set; }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            Timer = new TrackingTimer();
            return Timer;
        }
    }

    private sealed class TrackingTimer : ITimer
    {
        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;

        public void Dispose()
        {
            IsDisposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
