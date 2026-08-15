using Beutl.Editor.Components.FileBrowserTab.Services;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class DirectoryWatcherServiceTests
{
    private string _scratch = null!;

    [SetUp]
    public void SetUp()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"beutl-watcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, true);
        }
    }

    [Test]
    public void Rearms_the_watcher_after_an_error()
    {
        // Refresh never calls Watch, so a tab left on the same folder would otherwise stay
        // un-watched until the user navigated elsewhere.
        using var service = new DirectoryWatcherService();
        service.Watch(_scratch);

        bool rearmed = service.TryRearmAfterError();

        Assert.Multiple(() =>
        {
            Assert.That(rearmed, Is.True);
            Assert.That(service.IsWatching, Is.True);
        });
    }

    [Test]
    public void Stops_rearming_once_the_retry_budget_is_spent()
    {
        // A watcher failing for a persistent reason -- an exhausted inotify budget -- raises Error
        // again the moment it is rebuilt, so retrying forever would spin.
        using var service = new DirectoryWatcherService();
        service.Watch(_scratch);

        var results = new List<bool>();
        for (int i = 0; i < 5; i++)
        {
            results.Add(service.TryRearmAfterError());
        }

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.EqualTo(new[] { true, true, true, false, false }));
            Assert.That(service.IsWatching, Is.False);
        });
    }

    [Test]
    public void Navigating_to_another_folder_restores_the_retry_budget()
    {
        using var service = new DirectoryWatcherService();
        string other = Path.Combine(_scratch, "other");
        Directory.CreateDirectory(other);
        service.Watch(_scratch);
        while (service.TryRearmAfterError())
        {
        }

        service.Watch(other);

        Assert.Multiple(() =>
        {
            Assert.That(service.IsWatching, Is.True);
            Assert.That(service.TryRearmAfterError(), Is.True);
        });
    }

    [Test]
    public void Watching_the_same_path_twice_keeps_the_original_watcher()
    {
        // A recursive watcher costs an inotify descriptor per subdirectory, and callers re-subscribe
        // on unrelated state changes.
        using var service = new DirectoryWatcherService();
        service.Watch(_scratch);
        service.Watch(_scratch);

        Assert.That(service.IsWatching, Is.True);
    }

    [Test]
    public void Watching_a_missing_path_leaves_nothing_armed()
    {
        using var service = new DirectoryWatcherService();

        service.Watch(Path.Combine(_scratch, "does-not-exist"));

        Assert.That(service.IsWatching, Is.False);
    }
}
