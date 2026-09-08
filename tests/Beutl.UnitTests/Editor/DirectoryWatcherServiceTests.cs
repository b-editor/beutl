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
    public void An_event_that_arrives_after_disposal_is_dropped()
    {
        var service = new DirectoryWatcherService();
        service.Watch(_scratch);
        string changed = Path.Combine(_scratch, "late.txt");

        service.NotifyPathChanged(changed);
        service.Dispose();

        // The OS keeps delivering on the watcher's own thread after Dispose, and the
        // exception that used to escape there took the whole process with it.
        Assert.DoesNotThrow(() => service.NotifyPathChanged(changed));
    }

    [Test]
    public void Watching_after_disposal_cannot_rearm_the_watcher()
    {
        var service = new DirectoryWatcherService();
        service.Watch(_scratch);
        service.Dispose();
        service.Watch(_scratch);
        Assert.That(service.IsWatching, Is.False);
        Assert.That(service.TryRearmAfterError(), Is.False);
    }

    [Test]
    public async Task Events_watch_and_disposal_can_contend_without_rearming_or_throwing()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var service = new DirectoryWatcherService();
            service.Watch(_scratch);
            service.NotifyPathChanged(Path.Combine(_scratch, "before.txt"));
            using var start = new ManualResetEventSlim();
            Task callbacks = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 10; i++)
                    service.NotifyPathChanged(Path.Combine(_scratch, "event.txt"));
            });
            Task watch = Task.Run(() => { start.Wait(); service.Watch(_scratch); });
            Task dispose = Task.Run(() => { start.Wait(); service.Dispose(); });
            start.Set();
            await Task.WhenAll(callbacks, watch, dispose).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(service.IsWatching, Is.False);
        }
    }

    [Test]
    public void Watching_a_missing_path_leaves_nothing_armed()
    {
        using var service = new DirectoryWatcherService();

        service.Watch(Path.Combine(_scratch, "does-not-exist"));

        Assert.That(service.IsWatching, Is.False);
    }

    [Test]
    public void Failed_watcher_start_does_not_publish_a_disposed_instance()
    {
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            _ => throw new IOException("Injected watcher start failure."));

        service.Watch(_scratch);

        Assert.Multiple(() =>
        {
            Assert.That(service.IsWatching, Is.False);
            Assert.That(service.TryRearmAfterError(), Is.False);
        });
    }

    [Test]
    public void Retargeted_symbolic_link_rebuilds_the_watcher_for_the_new_identity()
    {
        string firstTarget = Path.Combine(_scratch, "first-target");
        string secondTarget = Path.Combine(_scratch, "second-target");
        string alias = Path.Combine(_scratch, "alias");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        try
        {
            Directory.CreateSymbolicLink(alias, firstTarget);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are unavailable: {ex.Message}");
        }

        var startedPaths = new List<string>();
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            watcher =>
            {
                startedPaths.Add(watcher.Path);
                watcher.EnableRaisingEvents = true;
            });

        service.Watch(alias);
        Directory.Delete(alias);
        Directory.CreateSymbolicLink(alias, secondTarget);
        service.Watch(alias);

        Assert.That(
            startedPaths,
            Is.EqualTo(new[]
            {
                FilePathComparison.ResolveCanonicalPath(firstTarget),
                FilePathComparison.ResolveCanonicalPath(secondTarget),
            }));
    }

    [Test]
    public void Error_rearm_resolves_the_original_symbolic_link_again()
    {
        string firstTarget = Path.Combine(_scratch, "first-error-target");
        string secondTarget = Path.Combine(_scratch, "second-error-target");
        string alias = Path.Combine(_scratch, "error-alias");
        Directory.CreateDirectory(firstTarget);
        Directory.CreateDirectory(secondTarget);
        try
        {
            Directory.CreateSymbolicLink(alias, firstTarget);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are unavailable: {ex.Message}");
        }

        var startedPaths = new List<string>();
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            watcher => startedPaths.Add(watcher.Path));
        service.Watch(alias);
        Directory.Delete(alias);
        Directory.CreateSymbolicLink(alias, secondTarget);

        bool rearmed = service.TryRearmAfterError();

        Assert.Multiple(() =>
        {
            Assert.That(rearmed, Is.True);
            Assert.That(startedPaths, Has.Count.EqualTo(2));
            Assert.That(startedPaths[1],
                Is.EqualTo(FilePathComparison.ResolveCanonicalPath(secondTarget)));
        });
    }

    [Test]
    public void Concurrent_rearms_cannot_exceed_the_retry_budget()
    {
        int starts = 0;
        using var service = new DirectoryWatcherService(
            TimeSpan.Zero,
            _ => { },
            _ => Interlocked.Increment(ref starts));
        service.Watch(_scratch);
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        Parallel.For(0, 128, _ =>
        {
            try
            {
                service.TryRearmAfterError();
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty);
            Assert.That(Volatile.Read(ref starts), Is.LessThanOrEqualTo(4));
        });
    }

    [Test]
    public void Navigating_to_a_symbolic_link_cycle_does_not_escape_path_comparison()
    {
        string cycle = Path.Combine(_scratch, "cycle");
        try
        {
            Directory.CreateSymbolicLink(cycle, cycle);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are unavailable: {ex.Message}");
        }

        using var service = new DirectoryWatcherService();
        service.Watch(_scratch);
        Assert.That(service.IsWatching, Is.True);

        Assert.DoesNotThrow(() => service.Watch(cycle));
        Assert.That(service.IsWatching, Is.False);
    }
}
