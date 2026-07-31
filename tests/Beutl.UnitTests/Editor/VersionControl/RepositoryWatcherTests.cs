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
