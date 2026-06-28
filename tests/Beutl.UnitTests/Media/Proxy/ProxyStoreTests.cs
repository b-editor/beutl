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
    public async Task ReconcileAsync_DropsMissingEntriesAndDeletesOnlyGeneratedProxyTmpFiles()
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
        string nestedGeneratedLookingTmpPath = Path.Combine(root, "nested", hashDirectory, $"quarter.{Guid.NewGuid():N}.tmp.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedGeneratedLookingTmpPath)!);
        File.WriteAllBytes(tmpPath, [1, 2, 3]);
        File.WriteAllBytes(unrelatedTmpPath, [1, 2, 3]);
        File.WriteAllBytes(encodedTmpPath, [1, 2, 3]);
        File.WriteAllBytes(nestedGeneratedLookingTmpPath, [1, 2, 3]);

        await store.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.Null);
            Assert.That(File.Exists(tmpPath), Is.True);
            Assert.That(File.Exists(unrelatedTmpPath), Is.True);
            Assert.That(File.Exists(encodedTmpPath), Is.False);
            Assert.That(File.Exists(nestedGeneratedLookingTmpPath), Is.True);
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
