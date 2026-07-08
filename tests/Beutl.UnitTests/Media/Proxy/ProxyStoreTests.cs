using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public sealed class ProxyStoreTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Test]
    public void Register_RoundTripsAndPersistsContractShape()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry entry = CreateEntry(root, "quarter.mp4");

        store.Register(entry);

        var reloaded = new ProxyStore(root);
        ProxyEntry? persisted = reloaded.TryGet(entry.Source, entry.Preset);
        string indexJson = File.ReadAllText(Path.Combine(root, "index.json"));
        Assert.Multiple(() =>
        {
            Assert.That(persisted, Is.EqualTo(entry));
            Assert.That(indexJson, Does.Contain($"\"version\": {ProxyStoreIndex.CurrentVersion}"));
            Assert.That(indexJson, Does.Contain("\"preset\": \"Quarter\""));
            Assert.That(indexJson, Does.Contain("\"state\": \"Ready\""));
        });
    }

    [Test]
    public void Delete_RemovesIndexEntryAndProxyFile()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry entry = CreateEntry(root, "quarter.mp4");
        store.Register(entry);

        bool deleted = store.Delete(entry.Source, entry.Preset);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.Null);
            Assert.That(File.Exists(Path.Combine(root, entry.ProxyFileRelative)), Is.False);
        });
    }

    [Test]
    public async Task ReconcileAsync_DropsMissingEntriesAndDeletesOnlyOldGeneratedProxyTmpFiles()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        string hashDirectory = new('a', 64);
        ProxyEntry entry = CreateEntry(root, $"{hashDirectory}/quarter.mp4");
        store.Register(entry);
        File.Delete(Path.Combine(root, entry.ProxyFileRelative));
        string tmpPath = Path.Combine(root, hashDirectory, "quarter.mp4.tmp");
        string unrelatedTmpPath = Path.Combine(root, hashDirectory, "clip.tmp.backup.mp4");
        string encodedTmpPath = Path.Combine(root, hashDirectory, $"quarter.{Guid.NewGuid():N}.tmp.mp4");
        string recentEncodedTmpPath = Path.Combine(root, hashDirectory, $"quarter.{Guid.NewGuid():N}.tmp.mp4");
        string nestedGeneratedLookingTmpPath = Path.Combine(root, "nested", hashDirectory, $"quarter.{Guid.NewGuid():N}.tmp.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedGeneratedLookingTmpPath)!);
        File.WriteAllBytes(tmpPath, [1, 2, 3]);
        File.WriteAllBytes(unrelatedTmpPath, [1, 2, 3]);
        File.WriteAllBytes(encodedTmpPath, [1, 2, 3]);
        File.WriteAllBytes(recentEncodedTmpPath, [1, 2, 3]);
        File.WriteAllBytes(nestedGeneratedLookingTmpPath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(encodedTmpPath, DateTime.UtcNow.AddHours(-25));

        await store.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.Null);
            Assert.That(File.Exists(tmpPath), Is.True);
            Assert.That(File.Exists(unrelatedTmpPath), Is.True);
            Assert.That(File.Exists(encodedTmpPath), Is.False);
            Assert.That(File.Exists(recentEncodedTmpPath), Is.True);
            Assert.That(File.Exists(nestedGeneratedLookingTmpPath), Is.True);
        });
    }

    [Test]
    public async Task ReconcileAsync_OrphanCleanup_DeletesOnlyProxyShapedFinalFiles()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        string hashDirectory = new('b', 64);
        DateTime aged = DateTime.UtcNow.AddHours(-25);

        string proxyOrphan = Path.Combine(root, hashDirectory, "quarter.mp4");
        string unrelatedTopLevel = Path.Combine(root, "archive.mp4");
        string wrongName = Path.Combine(root, hashDirectory, "render-final.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(proxyOrphan)!);
        foreach (string file in new[] { proxyOrphan, unrelatedTopLevel, wrongName })
        {
            File.WriteAllBytes(file, [1, 2, 3]);
            File.SetLastWriteTimeUtc(file, aged);
        }

        await store.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(proxyOrphan), Is.False, "an aged untracked proxy-shaped file should be reclaimed");
            Assert.That(File.Exists(unrelatedTopLevel), Is.True, "an unrelated *.mp4 under the store root must not be deleted");
            Assert.That(File.Exists(wrongName), Is.True, "an *.mp4 not matching the proxy filename pattern must not be deleted");
        });
    }

    [Test]
    public async Task ReconcileAsync_MarksReadyEntryStaleWhenSourceFingerprintChanges()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry entry = CreateEntry(root, "quarter.mp4");
        store.Register(entry);
        File.AppendAllBytes(entry.Source.AbsolutePath, [9]);
        File.SetLastWriteTimeUtc(entry.Source.AbsolutePath, DateTime.UtcNow.AddMinutes(1));

        await store.ReconcileAsync(CancellationToken.None);

        Assert.That(store.TryGet(entry.Source, entry.Preset)?.State, Is.EqualTo(ProxyState.Stale));
    }

    [Test]
    public void ReconcileAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        store.Register(CreateEntry(root, "quarter.mp4"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(async () => await store.ReconcileAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void TryTransition_ToNonReadyState_PreservesGeneratedAtUtc()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        DateTime generatedAt = DateTime.UtcNow.AddHours(-3);
        ProxyEntry entry = CreateEntry(root, "quarter.mp4") with { GeneratedAtUtc = generatedAt };
        store.Register(entry);

        bool ok = store.TryTransition(entry.Source, entry.Preset, ProxyState.Stale);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(store.TryGet(entry.Source, entry.Preset)?.GeneratedAtUtc, Is.EqualTo(generatedAt));
        });
    }

    [Test]
    public void TryTransition_IntoReadyState_RefreshesGeneratedAtUtc()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        DateTime generatedAt = DateTime.UtcNow.AddHours(-3);
        ProxyEntry generating = CreateEntry(root, "quarter.mp4") with
        {
            State = ProxyState.Generating,
            GeneratedAtUtc = generatedAt,
        };
        store.Register(generating);

        bool ok = store.TryTransition(generating.Source, generating.Preset, ProxyState.Ready);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(store.TryGet(generating.Source, generating.Preset)?.GeneratedAtUtc, Is.GreaterThan(generatedAt));
        });
    }

    [Test]
    public async Task ReconcileAsync_AdoptsSidecarWhenIndexIsCorrupt()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        string metadataPath = Path.Combine(root, "hash", "meta.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var metadata = new ProxySourceMetadata
        {
            Source = entry.Source,
            Entries = [entry],
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
        File.WriteAllText(Path.Combine(root, "index.json"), "{not valid json");

        var store = new ProxyStore(root);
        await store.ReconcileAsync(CancellationToken.None);

        Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
    }

    [Test]
    public async Task ReconcileAsync_AdoptsLegacySingleEntrySidecar()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        string metadataPath = Path.Combine(root, "hash", "meta.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        // A legacy sidecar is a bare ProxyEntry (no ProxySourceMetadata wrapper). It still
        // deserializes as ProxySourceMetadata with the default version and empty entries, so recovery
        // must fall through to the legacy parser and adopt it.
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(entry, s_jsonOptions));
        File.WriteAllText(Path.Combine(root, "index.json"), "{not valid json");

        var store = new ProxyStore(root);
        await store.ReconcileAsync(CancellationToken.None);

        Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
    }

    [Test]
    public void FlushCore_DegradedRegistrationSurvivesTouchReplay()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root, lockAcquireMaxAttempts: 0);
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");

        // Hold the index lock so Register degrades: the entry stays only in memory + pending
        // persistence. A Touch while degraded then marks the same key touch-dirty.
        string lockPath = Path.Combine(root, "index.lock");
        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            store.Register(entry);
            Assert.That(store.IsPersistenceDegraded, Is.True);
            store.Touch(entry.Source, entry.Preset, DateTime.UtcNow);
        }

        // Lock released: the replay flush must persist the pending registration, not drop it via the
        // touch-replay branch.
        store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

        var reloaded = new ProxyStore(root);
        ProxyEntry? persisted = reloaded.TryGet(entry.Source, entry.Preset);
        Assert.Multiple(() =>
        {
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.State, Is.EqualTo(ProxyState.Ready));
            Assert.That(persisted.ProxyFileRelative, Is.EqualTo(entry.ProxyFileRelative));
        });
    }

    [Test]
    public void LoadIndex_IgnoresPreviousStoreVersion()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        var oldIndex = new ProxyStoreIndex
        {
            Version = ProxyStoreIndex.CurrentVersion - 1,
            Entries = [entry],
        };
        File.WriteAllText(Path.Combine(root, "index.json"), JsonSerializer.Serialize(oldIndex, s_jsonOptions));

        var store = new ProxyStore(root);
        string indexJson = File.ReadAllText(Path.Combine(root, "index.json"));

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.Null);
            Assert.That(indexJson, Does.Contain($"\"version\": {ProxyStoreIndex.CurrentVersion}"));
            Assert.That(indexJson, Does.Not.Contain(entry.ProxyFileRelative));
        });
    }

    [Test]
    public async Task ReconcileAsync_IgnoresPreviousSourceMetadataVersion()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        string metadataPath = Path.Combine(root, "hash", "meta.json");
        var metadata = new ProxySourceMetadata
        {
            Version = ProxySourceMetadata.CurrentVersion - 1,
            Source = entry.Source,
            Entries = [entry],
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));

        var store = new ProxyStore(root);
        await store.ReconcileAsync(CancellationToken.None);

        Assert.That(store.TryGet(entry.Source, entry.Preset), Is.Null);
    }

    [Test]
    public void Register_AllowsConcurrentDistinctKeys()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry first = CreateEntry(root, "first.mp4");
        ProxyEntry second = CreateEntry(root, "second.mp4");

        Parallel.Invoke(
            () => store.Register(first),
            () => store.Register(second));

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(first.Source, first.Preset), Is.EqualTo(first));
            Assert.That(store.TryGet(second.Source, second.Preset), Is.EqualTo(second));
        });
    }

    [Test]
    public void Register_MergesEntriesWrittenByAnotherStoreInstance()
    {
        string root = CreateRoot();
        var firstStore = new ProxyStore(root);
        var secondStore = new ProxyStore(root);
        ProxyEntry first = CreateEntry(root, "first.mp4");
        ProxyEntry second = CreateEntry(root, "second.mp4");

        firstStore.Register(first);
        secondStore.Register(second);

        var reloaded = new ProxyStore(root);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.TryGet(first.Source, first.Preset), Is.EqualTo(first));
            Assert.That(reloaded.TryGet(second.Source, second.Preset), Is.EqualTo(second));
        });
    }

    [Test]
    public void LoadIndex_IgnoresReadyEntryWithInvalidSize()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4") with
        {
            ProxyFileSizeBytes = 0,
        };
        var index = new ProxyStoreIndex { Entries = [entry] };
        File.WriteAllText(Path.Combine(root, "index.json"), JsonSerializer.Serialize(index, s_jsonOptions));

        var store = new ProxyStore(root);

        Assert.That(store.TryGet(entry.Source, entry.Preset), Is.Null);
    }

    [Test]
    public async Task ReconcileAsync_PreservesFailedEntryWithoutProxyFile()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry ready = CreateEntry(root, "hash/quarter.mp4");
        ProxyEntry failed = ready with
        {
            State = ProxyState.Failed,
            ProxyFileRelative = "hash/failed.mp4",
            ProxyFileSizeBytes = 0,
            OriginalLogicalFrameSize = PixelSize.Empty,
            ProxyDecodedFrameSize = PixelSize.Empty,
            FailureReason = "decode failed",
        };
        store.Register(failed);

        await store.ReconcileAsync(CancellationToken.None);

        Assert.That(store.TryGet(failed.Source, failed.Preset), Is.EqualTo(failed));
    }

    [Test]
    public async Task ReconcileAsync_ReplacesFailedIndexEntryWithRecoveredReadySidecar()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry ready = CreateEntry(root, "hash/quarter.mp4");
        ProxyEntry failed = ready with
        {
            State = ProxyState.Failed,
            ProxyFileRelative = "hash/failed.mp4",
            ProxyFileSizeBytes = 0,
            OriginalLogicalFrameSize = PixelSize.Empty,
            ProxyDecodedFrameSize = PixelSize.Empty,
            FailureReason = "decode failed",
        };
        store.Register(failed);
        string metadataPath = Path.Combine(root, "hash", "meta.json");
        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(
                new ProxySourceMetadata
                {
                    Source = ready.Source,
                    Entries = [ready],
                },
                s_jsonOptions));

        await store.ReconcileAsync(CancellationToken.None);

        var reloaded = new ProxyStore(root);
        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(ready.Source, ready.Preset), Is.EqualTo(ready));
            Assert.That(reloaded.TryGet(ready.Source, ready.Preset), Is.EqualTo(ready));
        });
    }

    [Test]
    public async Task TouchFlush_DoesNotRestoreEntryRemovedByAnotherStoreInstance()
    {
        string root = CreateRoot();
        var staleStore = new ProxyStore(root);
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        staleStore.Register(entry);
        var emptyIndex = new ProxyStoreIndex { Entries = [] };
        File.WriteAllText(Path.Combine(root, "index.json"), JsonSerializer.Serialize(emptyIndex, s_jsonOptions));

        staleStore.Touch(entry.Source, entry.Preset, DateTime.UtcNow.AddMinutes(1));
        await staleStore.FlushAsync(CancellationToken.None);

        string indexJson = File.ReadAllText(Path.Combine(root, "index.json"));
        var reloaded = new ProxyStore(root);
        Assert.Multiple(() =>
        {
            Assert.That(indexJson, Does.Not.Contain(entry.ProxyFileRelative));
            Assert.That(staleStore.TryGet(entry.Source, entry.Preset), Is.Null);
            Assert.That(reloaded.TryGet(entry.Source, entry.Preset), Is.Null);
        });
    }

    [Test]
    public void Register_RejectsProxyPathEscapingStoreRoot()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry entry = CreateEntry(root, "safe/quarter.mp4") with
        {
            ProxyFileRelative = "../outside.mp4",
        };

        Assert.Throws<ArgumentException>(() => store.Register(entry));
    }

    [Test]
    public void LoadIndex_IgnoresProxyPathEscapingStoreRoot()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "safe/quarter.mp4") with
        {
            ProxyFileRelative = "../outside.mp4",
        };
        var index = new ProxyStoreIndex { Entries = [entry] };
        File.WriteAllText(Path.Combine(root, "index.json"), JsonSerializer.Serialize(index, s_jsonOptions));

        var store = new ProxyStore(root);

        Assert.That(store.Enumerate(), Is.Empty);
    }

    [Test]
    public void Delete_RemovesSidecarMetadataEntry()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        string metadataPath = Path.Combine(root, "hash", "meta.json");
        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(
                new ProxySourceMetadata
                {
                    Source = entry.Source,
                    Entries = [entry],
                },
                s_jsonOptions));
        store.Register(entry);

        store.Delete(entry.Source, entry.Preset);

        Assert.That(File.Exists(metadataPath), Is.False);
    }

    [Test]
    public void Register_DegradesGracefullyWhenIndexLockContendedAndReplaysOnceReleased()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        string lockPath = Path.Combine(root, "index.lock");
        var store = new ProxyStore(root, lockAcquireMaxAttempts: 0);

        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.DoesNotThrow(() => store.Register(entry));
            Assert.Multiple(() =>
            {
                Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
                Assert.That(store.IsPersistenceDegraded, Is.True);
                Assert.That(File.Exists(Path.Combine(root, "index.json")), Is.False);
            });
        }

        store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

        var reloaded = new ProxyStore(root);
        Assert.Multiple(() =>
        {
            Assert.That(store.IsPersistenceDegraded, Is.False);
            Assert.That(reloaded.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
        });
    }

    [Test]
    public async Task ReconcileAsync_ReclaimsOldOrphanProxyAndPreservesTrackedFreshAndInFlightFiles()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry tracked = CreateEntry(root, $"{new string('a', 64)}/quarter.mp4");
        store.Register(tracked);

        ProxyEntry generating = CreateEntry(root, $"{new string('d', 64)}/quarter.mp4") with
        {
            State = ProxyState.Generating,
        };
        store.Register(generating);
        string generatingPath = Path.Combine(root, generating.ProxyFileRelative);
        File.SetLastWriteTimeUtc(generatingPath, DateTime.UtcNow.AddHours(-2));

        string orphanDir = Path.Combine(root, new string('b', 64));
        Directory.CreateDirectory(orphanDir);
        string oldOrphan = Path.Combine(orphanDir, "quarter.mp4");
        File.WriteAllBytes(oldOrphan, [1, 2, 3]);
        File.SetLastWriteTimeUtc(oldOrphan, DateTime.UtcNow.AddHours(-25));

        string freshDir = Path.Combine(root, new string('c', 64));
        Directory.CreateDirectory(freshDir);
        string freshOrphan = Path.Combine(freshDir, "quarter.mp4");
        File.WriteAllBytes(freshOrphan, [4, 5, 6]);

        await store.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(root, tracked.ProxyFileRelative)), Is.True);
            Assert.That(File.Exists(generatingPath), Is.True);
            Assert.That(File.Exists(oldOrphan), Is.False);
            Assert.That(File.Exists(freshOrphan), Is.True);
        });
    }

    [Test]
    public void Register_ReRegisterAfterDeleteWhileDegradedSurvivesReplay()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        string lockPath = Path.Combine(root, "index.lock");
        var store = new ProxyStore(root, lockAcquireMaxAttempts: 0);

        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            store.Register(entry);
            Assert.That(store.Delete(entry.Source, entry.Preset), Is.True);

            // Delete removes the proxy file; a real re-register regenerates it first.
            File.WriteAllBytes(Path.Combine(root, entry.ProxyFileRelative), [5, 6, 7]);
            store.Register(entry);

            Assert.Multiple(() =>
            {
                Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
                Assert.That(store.IsPersistenceDegraded, Is.True);
            });
        }

        store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

        var reloaded = new ProxyStore(root);
        Assert.Multiple(() =>
        {
            Assert.That(store.IsPersistenceDegraded, Is.False);
            Assert.That(reloaded.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
        });
    }

    [Test]
    public void Register_DegradesGracefullyWhenIndexWriteFailsAndReplaysOnceWritable()
    {
        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        var store = new ProxyStore(root);
        string indexPath = Path.Combine(root, "index.json");

        // A directory at the index.json path makes the tmp-file File.Move fail with IOException
        // after the lock is acquired, exercising the mid-write degradation path.
        Directory.CreateDirectory(indexPath);

        Assert.DoesNotThrow(() => store.Register(entry));
        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
            Assert.That(store.IsPersistenceDegraded, Is.True);
        });

        Directory.Delete(indexPath);
        store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

        var reloaded = new ProxyStore(root);
        Assert.Multiple(() =>
        {
            Assert.That(store.IsPersistenceDegraded, Is.False);
            Assert.That(reloaded.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
        });
    }

    [Test]
    public void Register_StoreRootWithoutWritePermission_DegradesAndReplaysAfterRestore()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Revoking directory write permission via UnixFileMode is not supported on Windows.");

        // Root bypasses the permission check, so under a root container this fails loudly
        // (Register throws) rather than passing vacuously; CI runners are non-root.

        string root = CreateRoot();
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        var store = new ProxyStore(root);

        static void SetMode(string path, UnixFileMode mode)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, mode);
        }

        UnixFileMode readOnly = UnixFileMode.UserRead | UnixFileMode.UserExecute;
        UnixFileMode writable = readOnly | UnixFileMode.UserWrite;
        SetMode(root, readOnly);
        try
        {
            Assert.DoesNotThrow(() => store.Register(entry));
            Assert.Multiple(() =>
            {
                Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
                Assert.That(store.IsPersistenceDegraded, Is.True);
            });
        }
        finally
        {
            SetMode(root, writable);
        }

        store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

        var reloaded = new ProxyStore(root);
        Assert.Multiple(() =>
        {
            Assert.That(store.IsPersistenceDegraded, Is.False);
            Assert.That(reloaded.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
        });
    }

    [Test]
    public void Register_WhenChangedSubscriberThrows_StillCommitsAndNotifiesOtherSubscribers()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry entry = CreateEntry(root, "hash/quarter.mp4");
        bool otherNotified = false;
        store.Changed += (_, _) => throw new InvalidOperationException("bad subscriber");
        store.Changed += (_, _) => otherNotified = true;

        Assert.DoesNotThrow(() => store.Register(entry));
        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
            Assert.That(otherNotified, Is.True, "a throwing subscriber must not starve later subscribers");
        });
    }

    [Test]
    public void GetTotalBytes_SumsOnlyReadyStaleAndFailedEntries()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);

        ProxyEntry ready = CreateEntry(root, "ready/quarter.mp4");
        ProxyEntry stale = CreateEntry(root, "stale/quarter.mp4") with { State = ProxyState.Stale, ProxyFileSizeBytes = 10 };
        File.WriteAllBytes(Path.Combine(root, "stale", "quarter.mp4"), new byte[10]);
        ProxyEntry failed = CreateEntry(root, "failed/quarter.mp4") with { State = ProxyState.Failed, ProxyFileSizeBytes = 100 };
        ProxyEntry generating = CreateEntry(root, "generating/quarter.mp4") with { State = ProxyState.Generating, ProxyFileSizeBytes = 1000 };
        store.Register(ready);
        store.Register(stale);
        store.Register(failed);
        store.Register(generating);

        Assert.That(store.GetTotalBytes(), Is.EqualTo(ready.ProxyFileSizeBytes + 10 + 100));
    }

    [Test]
    public void ConcurrentMixedOperations_DoNotThrowOrLoseEntries()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);

        const int EntryCount = 40;
        const int DistinctCount = 30;
        var entries = new ProxyEntry[EntryCount];
        for (int i = 0; i < EntryCount; i++)
            entries[i] = CreateEntry(root, $"hash{i}/quarter.mp4");

        var exceptions = new ConcurrentQueue<Exception>();
        var distinctEntries = entries.Take(DistinctCount).ToArray();
        var sharedEntries = entries.Skip(DistinctCount).ToArray();

        Parallel.For(0, EntryCount, i =>
        {
            try
            {
                store.Register(entries[i]);
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        Parallel.Invoke(
            () =>
            {
                foreach (ProxyEntry entry in distinctEntries)
                {
                    for (int j = 0; j < 5; j++)
                    {
                        try
                        {
                            store.Touch(entry.Source, entry.Preset, DateTime.UtcNow);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }
                    }
                }
            },
            () =>
            {
                foreach (ProxyEntry entry in entries)
                {
                    for (int j = 0; j < 5; j++)
                    {
                        try
                        {
                            store.TryGet(entry.Source, entry.Preset);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }
                    }
                }
            },
            () =>
            {
                foreach (ProxyEntry entry in sharedEntries)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        try
                        {
                            store.TryTransition(entry.Source, entry.Preset, ProxyState.Stale);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }
                    }
                }
            },
            () =>
            {
                foreach (ProxyEntry entry in sharedEntries.Take(5))
                {
                    try
                    {
                        store.Delete(entry.Source, entry.Preset);
                        File.WriteAllBytes(Path.Combine(root, entry.ProxyFileRelative), [5, 6, 7]);
                        store.Register(entry);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                }
            },
            () =>
            {
                for (int j = 0; j < 10; j++)
                {
                    try
                    {
                        store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                }
            });

        store.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty, "no concurrent operation should throw");
            foreach (ProxyEntry entry in distinctEntries)
            {
                Assert.That(
                    store.TryGet(entry.Source, entry.Preset),
                    Is.Not.Null,
                    $"distinct entry for {entry.ProxyFileRelative} was lost");
            }

            Assert.That(
                store.IsPersistenceDegraded,
                Is.False,
                "persistence should recover once the cross-process lock is uncontended");
        });

        var reloaded = new ProxyStore(root);
        foreach (ProxyEntry entry in distinctEntries)
        {
            Assert.That(
                reloaded.TryGet(entry.Source, entry.Preset),
                Is.Not.Null,
                $"distinct entry for {entry.ProxyFileRelative} was not persisted to disk");
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProxyEntry CreateEntry(string root, string relative)
    {
        string sourcePath = Path.Combine(root, $"{Guid.NewGuid():N}.mov");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);
        string proxyPath = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)!);
        File.WriteAllBytes(proxyPath, [5, 6, 7]);

        var now = DateTime.UtcNow;
        return new ProxyEntry(
            ProxyFingerprint.FromFile(sourcePath),
            ProxyPreset.Quarter,
            ProxyState.Ready,
            relative.Replace(Path.DirectorySeparatorChar, '/'),
            3,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null);
    }
}
