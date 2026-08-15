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
        using var service = new DirectoryWatcherService();
        service.Watch(_scratch);
        service.Watch(_scratch);

        Assert.That(service.IsWatching, Is.True);
    }

    [Test]
    public void Watching_the_same_path_does_not_restore_spent_retries()
    {
        using var service = new DirectoryWatcherService();
        service.Watch(_scratch);
        while (service.TryRearmAfterError())
        {
        }

        service.Watch(_scratch);

        Assert.Multiple(() =>
        {
            Assert.That(service.IsWatching, Is.True);
            Assert.That(service.TryRearmAfterError(), Is.False);
            Assert.That(service.IsWatching, Is.False);
        });
    }

    [Test]
    public void A_watcher_that_delivers_again_gets_its_retries_back()
    {
        using var service = new DirectoryWatcherService();
        service.Watch(_scratch);
        service.TryRearmAfterError();
        service.TryRearmAfterError();

        service.MarkDelivered();

        var results = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            results.Add(service.TryRearmAfterError());
        }

        Assert.That(results, Is.EqualTo(new[] { true, true, true, false }));
    }

    [Test]
    public void A_rebuild_that_cannot_construct_a_watcher_spends_the_whole_budget()
    {
        using var service = new DirectoryWatcherService();
        string doomed = Path.Combine(_scratch, "doomed");
        Directory.CreateDirectory(doomed);
        service.Watch(doomed);
        Directory.Delete(doomed);

        bool rearmed = service.TryRearmAfterError();

        // Re-Watching does not restore the budget — the path is still the failing one.
        Directory.CreateDirectory(doomed);
        service.Watch(doomed);

        Assert.Multiple(() =>
        {
            Assert.That(rearmed, Is.False);
            Assert.That(service.IsWatching, Is.True);
            Assert.That(service.TryRearmAfterError(), Is.False);
        });
    }

    [Test]
    public void Watching_a_missing_path_leaves_nothing_armed()
    {
        using var service = new DirectoryWatcherService();

        service.Watch(Path.Combine(_scratch, "does-not-exist"));

        Assert.That(service.IsWatching, Is.False);
    }
}
