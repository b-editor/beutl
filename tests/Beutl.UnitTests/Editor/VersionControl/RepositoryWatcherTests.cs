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

    [TestCase("index", true)]
    [TestCase("HEAD", true)]
    [TestCase("packed-refs", true)]
    [TestCase("refs/heads/main", true)]
    [TestCase("refs/remotes/origin/main", true)]
    [TestCase("index.lock", false)]
    [TestCase("HEAD.lock", false)]
    [TestCase("packed-refs.lock", false)]
    [TestCase("refs/heads/main.lock", false)]
    [TestCase("objects/ab/cdef", false)]
    [TestCase("logs/HEAD", false)]
    [TestCase("config", false)]
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

    [Test]
    public async Task Debounce_coalesces_a_burst_at_exactly_500_milliseconds()
    {
        var timeProvider = new FakeTimeProvider();
        using var watcher = new RepositoryWatcher(_tempDirectory, timeProvider, startWatching: false);
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
        using var watcher = new RepositoryWatcher(_tempDirectory, timeProvider, startWatching: false);
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
        using var watcher = new RepositoryWatcher(_tempDirectory, timeProvider, startWatching: false);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => completion.TrySetResult();

        watcher.NotifyPathRenamed(
            Path.Combine(_tempDirectory, "scene.belm"),
            Path.Combine(_tempDirectory, "scene.belm.tmp"));
        timeProvider.Advance(RepositoryWatcher.DebounceInterval);

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(completion.Task.IsCompletedSuccessfully, Is.True);
    }
}
