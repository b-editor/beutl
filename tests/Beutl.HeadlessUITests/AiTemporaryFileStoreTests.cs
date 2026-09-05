using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiTemporaryFileStoreTests
{
    [Test]
    public void Create_UsesPrivateHomeDirectoryAndFilePermissions()
    {
        (string path, FileStream stream) = AiTemporaryFileStore.Create(
            "tests",
            "private",
            ".png");
        using (stream)
        {
            stream.WriteByte(1);
        }

        try
        {
            Assert.That(
                Path.GetFullPath(path),
                Does.StartWith(Path.GetFullPath(BeutlEnvironment.GetHomeDirectoryPath())));
            if (!OperatingSystem.IsWindows())
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(
                        File.GetUnixFileMode(path),
                        Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                    Assert.That(
                        File.GetUnixFileMode(Path.GetDirectoryName(path)!),
                        Is.EqualTo(
                            UnixFileMode.UserRead
                            | UnixFileMode.UserWrite
                            | UnixFileMode.UserExecute));
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Cleanup_RemovesOnlyStaleFiles()
    {
        string directory = AiTemporaryFileStore.GetCategoryDirectory(
            $"cleanup-{Guid.NewGuid():N}");
        AiTemporaryFileStore.EnsurePrivateDirectory(directory);
        string stale = Path.Combine(directory, "stale.wav");
        string recent = Path.Combine(directory, "recent.wav");
        File.WriteAllBytes(stale, [1]);
        File.WriteAllBytes(recent, [2]);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(stale, (now - AiTemporaryFileStore.StaleAge - TimeSpan.FromMinutes(1)).UtcDateTime);

        AiTemporaryFileStore.CleanStaleFiles(directory, now);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(stale), Is.False);
            Assert.That(File.Exists(recent), Is.True);
        }
        Directory.Delete(directory, recursive: true);
    }

    [Test]
    public void Cleanup_LeavesASessionAnotherProcessIsStillHolding()
    {
        string categoryRoot = AiTemporaryFileStore.GetCategoryRootDirectory(
            $"sessions-{Guid.NewGuid():N}");
        string live = Path.Combine(categoryRoot, "live");
        string abandoned = Path.Combine(categoryRoot, "abandoned");
        AiTemporaryFileStore.EnsurePrivateDirectory(live);
        AiTemporaryFileStore.EnsurePrivateDirectory(abandoned);
        string held = Path.Combine(live, "held.wav");
        string orphaned = Path.Combine(abandoned, "orphaned.wav");
        File.WriteAllBytes(held, [1]);
        File.WriteAllBytes(orphaned, [2]);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTime stale = (now - AiTemporaryFileStore.StaleAge - TimeSpan.FromMinutes(1)).UtcDateTime;
        File.SetLastWriteTimeUtc(held, stale);
        File.SetLastWriteTimeUtc(orphaned, stale);

        // What another running Beutl holds for as long as it is running.
        using (var lease = new FileStream(
                   Path.Combine(live, ".lock"),
                   FileMode.OpenOrCreate,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            AiTemporaryFileStore.CleanAbandonedSessions(categoryRoot, now);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(held), Is.True,
                "A file of a session still being held is not this process's to remove.");
            Assert.That(File.Exists(orphaned), Is.False);
        }

        Directory.Delete(categoryRoot, recursive: true);
    }

    [Test]
    public async Task Create_ClaimsSessionBeforeAConcurrentProcessCanSweepIt()
    {
        string category = $"creation-race-{Guid.NewGuid():N}";
        string categoryRoot = AiTemporaryFileStore.GetCategoryRootDirectory(category);
        using var directoryExposed = new ManualResetEventSlim();
        using var releaseSessionClaim = new ManualResetEventSlim();
        AiTemporaryFileStore.BeforeSessionClaim = directory =>
        {
            if (!string.Equals(Path.GetDirectoryName(directory), categoryRoot, StringComparison.Ordinal))
                return;

            directoryExposed.Set();
            if (!releaseSessionClaim.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The session claim was not released by the test.");
        };

        Task<(string Path, FileStream Stream)> creation = Task.Run(() =>
            AiTemporaryFileStore.Create(category, "race", ".wav"));
        try
        {
            Assert.That(directoryExposed.Wait(TimeSpan.FromSeconds(5)), Is.True);
            using var sweepStarted = new ManualResetEventSlim();
            Task sweep = Task.Run(() =>
            {
                sweepStarted.Set();
                AiTemporaryFileStore.CleanAbandonedSessions(
                    categoryRoot,
                    DateTimeOffset.UtcNow + AiTemporaryFileStore.StaleAge,
                    "another-process");
            });
            Assert.That(sweepStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);

            await Task.Delay(100);
            Assert.That(sweep.IsCompleted, Is.False,
                "The sweeper must wait until the creator publishes its session lock.");

            releaseSessionClaim.Set();
            (string path, FileStream stream) = await creation.WaitAsync(TimeSpan.FromSeconds(5));
            using (stream)
            {
                stream.WriteByte(1);
            }
            await sweep.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(File.Exists(path), Is.True,
                "A concurrent process must not sweep a session after its creator claims it.");
            File.Delete(path);
        }
        finally
        {
            releaseSessionClaim.Set();
            AiTemporaryFileStore.BeforeSessionClaim = null;
            if (!creation.IsCompleted)
            {
                await creation.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Test]
    public async Task SlowSessionCleanupDoesNotHoldTheCategoryCoordinationLock()
    {
        string category = $"slow-cleanup-{Guid.NewGuid():N}";
        string categoryRoot = AiTemporaryFileStore.GetCategoryRootDirectory(category);
        string abandoned = Path.Combine(categoryRoot, "abandoned");
        AiTemporaryFileStore.EnsurePrivateDirectory(abandoned);
        string stale = Path.Combine(abandoned, "stale.tmp");
        await File.WriteAllBytesAsync(stale, [1]);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(
            stale,
            (now - AiTemporaryFileStore.StaleAge - TimeSpan.FromMinutes(1)).UtcDateTime);
        using var cleanupStarted = new ManualResetEventSlim();
        using var releaseCleanup = new ManualResetEventSlim();
        AiTemporaryFileStore.BeforeSessionCleanup = directory =>
        {
            if (!string.Equals(directory, abandoned, StringComparison.Ordinal))
                return;
            cleanupStarted.Set();
            if (!releaseCleanup.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The slow cleanup was not released by the test.");
        };

        Task cleanup = Task.Run(() => AiTemporaryFileStore.CleanAbandonedSessions(
            categoryRoot,
            now,
            "another-process"));
        try
        {
            Assert.That(cleanupStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);

            Task<(string Path, FileStream Stream)> creation = Task.Run(() =>
                AiTemporaryFileStore.Create(category, "responsive", ".wav"));
            (string path, FileStream stream) = await creation.WaitAsync(TimeSpan.FromSeconds(2));
            stream.Dispose();
            Assert.That(File.Exists(path), Is.True);
            File.Delete(path);

            releaseCleanup.Set();
            await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseCleanup.Set();
            AiTemporaryFileStore.BeforeSessionCleanup = null;
            await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
