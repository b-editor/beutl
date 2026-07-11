using Beutl.Editor;
using Beutl.Engine;
using Beutl.IO;
using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyEvictionTests
{
    [Test]
    public void Sweep_RemovesLeastRecentlyUsedUntilUnderCap()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry oldEntry = Register(store, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry newEntry = Register(store, root, "new.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(store, null, maxTotalBytes: 7);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(oldEntry.Source, oldEntry.Preset), Is.Null);
            Assert.That(store.TryGet(newEntry.Source, newEntry.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void Sweep_SkipsPinnedEntries()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry pinned = Register(store, root, "pinned.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry candidate = Register(store, root, "candidate.mp4", DateTime.UtcNow, 7);
        var resolver = new ProxyResolver(store);
        var resolution = new ProxyResolution(
            Path.Combine(root, pinned.ProxyFileRelative),
            pinned.Source,
            pinned.Preset,
            pinned.OriginalLogicalFrameSize,
            pinned.ProxyDecodedFrameSize);
        using IDisposable pin = resolver.Pin(resolution);
        var service = new ProxyEvictionService(store, resolver, maxTotalBytes: 7);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(pinned.Source, pinned.Preset), Is.Not.Null);
            Assert.That(store.TryGet(candidate.Source, candidate.Preset), Is.Null);
        });
    }

    [Test]
    public void Sweep_ReChecksPinTakenAfterCandidateCollection()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry candidate = Register(store, root, "candidate.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        // Simulates a decode-lifetime pin taken between candidate collection and the delete loop:
        // false on the first (collection) probe, true on the second (delete) probe for the same path.
        var resolver = new PinOnSecondProbeResolver();
        var service = new ProxyEvictionService(store, resolver, maxTotalBytes: 7);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            Assert.That(store.TryGet(candidate.Source, candidate.Preset), Is.Not.Null);
        });
    }

    // Store.Delete removes the index entry but only best-effort-deletes the file; when the file survives
    // (a sharing violation), the bytes are not actually reclaimed, so the sweep must not credit them or a
    // disk-pressure sweep stops early while the orphan still occupies disk.
    [Test]
    public void Sweep_FileSurvivesDelete_DoesNotCreditDiskReclamation()
    {
        string root = CreateRoot();
        var inner = new ProxyStore(root);
        Register(inner, root, "survives.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        var store = new RestoreFileOnDeleteStore(inner);
        var service = new ProxyEvictionService(store, null, maxTotalBytes: 0);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1), "the index entry is still removed");
            Assert.That(result.ReclaimedBytes, Is.EqualTo(0),
                "a proxy whose file survived the delete must not be credited as reclaimed disk");
            Assert.That(File.Exists(Path.Combine(root, "survives.mp4")), Is.True, "the orphan file remains on disk");
        });
    }

    [Test]
    public void Sweep_ProtectsOpenProjectEntries_EvictingUnrelatedFirst()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        // The open-project proxy is the OLDEST, so pure LRU would reap it first.
        ProxyEntry openProject = Register(store, root, "open.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry unrelated = Register(store, root, "other.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            openProjectSourceProvider: () => new HashSet<string> { openProject.Source.AbsolutePath });

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(openProject.Source, openProject.Preset), Is.Not.Null);
            Assert.That(store.TryGet(unrelated.Source, unrelated.Preset), Is.Null);
        });
    }

    [Test]
    public void Sweep_ProtectsInProjectSource_CollectedFromProjectGraph()
    {
        string root = CreateRoot();
        // Media stored INSIDE the project directory: ExternalResourceCollector would have
        // filtered this out, so its proxy would have fallen back to plain LRU.
        string projectDir = Path.Combine(root, "project");
        Directory.CreateDirectory(projectDir);
        string inProjectSource = Path.Combine(projectDir, "clip.mov");
        File.WriteAllBytes(inProjectSource, [1]);

        var store = new ProxyStore(root);
        // The in-project proxy is the OLDEST, so pure LRU would reap it first.
        ProxyEntry inProject = RegisterForSource(store, root, inProjectSource, "in.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry unrelated = Register(store, root, "other.mp4", DateTime.UtcNow, 7);

        // Drive the real collection path CollectOpenProjectSources uses.
        var graph = new TestHierarchical();
        graph.AddChild(new TestEngineObjectWithFileSource(new TestFileSource(new Uri(inProjectSource))));
        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateFileSources(graph);

        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            openProjectSourceProvider: () => collected);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(collected, Has.Count.EqualTo(1));
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(inProject.Source, inProject.Preset), Is.Not.Null);
            Assert.That(store.TryGet(unrelated.Source, unrelated.Preset), Is.Null);
        });
    }

    [Test]
    public void EnumerateFileSources_IncludesAnimatedKeyframeSources()
    {
        string animatedPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "animated-source.mov");
        var keyframeSource = new Beutl.Media.Source.VideoSource();
        keyframeSource.ReadFrom(new Uri(animatedPath));
        var animation = new Beutl.Animation.KeyFrameAnimation<Beutl.Media.Source.VideoSource?>();
        animation.KeyFrames.Add(new Beutl.Animation.KeyFrame<Beutl.Media.Source.VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = keyframeSource,
        });
        var drawable = new Beutl.Graphics.SourceVideo();
        drawable.Source.Animation = animation;

        var graph = new TestHierarchical();
        graph.AddChild(drawable);

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateFileSources(graph);

        Assert.That(collected, Does.Contain(new Uri(animatedPath).LocalPath));
    }

    [Test]
    public void Constructor_NegativeMaxTotalBytes_Throws()
    {
        var store = new ProxyStore(CreateRoot());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProxyEvictionService(store, resolver: null, maxTotalBytes: -1));
    }

    // A runtime cap change must take effect on the next sweep: CapOverage reads the cap through the
    // volatile property, so lowering MaxTotalBytes after construction evicts down to the new limit.
    [Test]
    public void MaxTotalBytes_LoweredAtRuntime_NextSweepEnforcesNewCap()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry oldEntry = Register(store, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry newEntry = Register(store, root, "new.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(store, null, maxTotalBytes: 100);

        // Under the initial 100-byte cap the 14-byte store is within budget — nothing evicted.
        Assert.That(service.Sweep().RemovedCount, Is.EqualTo(0));

        service.MaxTotalBytes = 7;
        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(oldEntry.Source, oldEntry.Preset), Is.Null);
            Assert.That(store.TryGet(newEntry.Source, newEntry.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void Sweep_SkipsEntriesWithActiveGeneration()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry generating = Register(store, root, "generating.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry idle = Register(store, root, "idle.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            isGenerationActive: (source, preset) => (source, preset) == (generating.Source, generating.Preset));

        ProxyEvictionResult result = service.Sweep();

        // The LRU candidate is being regenerated, so deleting it now could lose the usable proxy if
        // that generation fails; eviction must fall through to the idle entry instead.
        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(generating.Source, generating.Preset), Is.Not.Null);
            Assert.That(store.TryGet(idle.Source, idle.Preset), Is.Null);
        });
    }

    // A regenerate can be queued while the sweep is mid-flight — after earlier candidates are deleted
    // but before this one is reached. A snapshot of active generations taken once before the loop would
    // miss it and delete the proxy whose replacement is still in flight. Modeled by a provider that
    // reports the second candidate active only once the first has been deleted; the per-candidate
    // re-read must then spare it.
    [Test]
    public void Sweep_ReChecksActiveGenerationQueuedDuringSweep()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry first = Register(store, root, "first.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry second = Register(store, root, "second.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 0,
            isGenerationActive: (source, preset) =>
                store.TryGet(first.Source, first.Preset) is null
                && (source, preset) == (second.Source, second.Preset));

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1), "only the first (idle) candidate is evicted");
            Assert.That(store.TryGet(first.Source, first.Preset), Is.Null);
            Assert.That(store.TryGet(second.Source, second.Preset), Is.Not.Null,
                "the candidate whose regenerate was queued mid-sweep must be spared");
        });
    }

    [Test]
    public void Sweep_EvictsOpenProjectEntries_AsLastResort()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry olderOpen = Register(store, root, "older.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry newerOpen = Register(store, root, "newer.mp4", DateTime.UtcNow, 7);
        var protectedSet = new HashSet<string>
        {
            olderOpen.Source.AbsolutePath,
            newerOpen.Source.AbsolutePath,
        };
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            openProjectSourceProvider: () => protectedSet);

        ProxyEvictionResult result = service.Sweep();

        // Everything is protected, but the cap must still be met: the LRU protected one goes.
        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(olderOpen.Source, olderOpen.Preset), Is.Null);
            Assert.That(store.TryGet(newerOpen.Source, newerOpen.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_EvictsWhenHostFreeSpaceLow()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry oldEntry = Register(store, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 20);
        ProxyEntry newEntry = Register(store, root, "new.mp4", DateTime.UtcNow, 20);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            // Cap is generous, so only host free-space pressure can drive eviction.
            maxTotalBytes: 1_000_000,
            minFreeDiskBytes: 100,
            availableFreeSpaceProvider: _ => 90);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(oldEntry.Source, oldEntry.Preset), Is.Null);
            Assert.That(store.TryGet(newEntry.Source, newEntry.Preset), Is.Not.Null);
        });
    }

    // A stale entry whose proxy file was deleted outside the process frees no disk when removed, so
    // it must not satisfy a disk-pressure sweep early — the sweep has to reach a real file.
    [Test]
    public void SweepForDiskPressure_MissingProxyFileDoesNotCreditDiskHeadroom()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry missing = Register(store, root, "missing.mp4", DateTime.UtcNow.AddMinutes(-10), 20);
        ProxyEntry real = Register(store, root, "real.mp4", DateTime.UtcNow, 20);
        File.Delete(Path.Combine(root, "missing.mp4"));
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 1_000_000,
            minFreeDiskBytes: 100,
            availableFreeSpaceProvider: _ => 85); // shortfall 15

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(real.Source, real.Preset), Is.Null, "the sweep must reach the real file the missing one couldn't free");
            Assert.That(result.ReclaimedBytes, Is.EqualTo(20), "only the actually-freed on-disk bytes are reported");
            Assert.That(store.TryGet(missing.Source, missing.Preset), Is.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_DoesNotEvictWhenEvictionCannotFreeEnough()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry a = Register(store, root, "a.mp4", DateTime.UtcNow.AddMinutes(-10), 5);
        ProxyEntry b = Register(store, root, "b.mp4", DateTime.UtcNow, 5);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 1_000_000,
            // Shortfall (1000 - 100 = 900) far exceeds the 10 bytes that could be reclaimed.
            minFreeDiskBytes: 1000,
            availableFreeSpaceProvider: _ => 100);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            Assert.That(store.TryGet(a.Source, a.Preset), Is.Not.Null);
            Assert.That(store.TryGet(b.Source, b.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_NoEvictionWhenHeadroomSatisfied()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry a = Register(store, root, "a.mp4", DateTime.UtcNow.AddMinutes(-10), 20);
        ProxyEntry b = Register(store, root, "b.mp4", DateTime.UtcNow, 20);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 1_000_000,
            minFreeDiskBytes: 100,
            availableFreeSpaceProvider: _ => 500);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            Assert.That(store.TryGet(a.Source, a.Preset), Is.Not.Null);
            Assert.That(store.TryGet(b.Source, b.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_StillEnforcesCapWhenDiskGoalUnreachable()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry oldEntry = Register(store, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry newEntry = Register(store, root, "new.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            // Over cap by 7 bytes; the disk goal (1000) is unreachable, so only the
            // cap overage should be reclaimed rather than over-evicting.
            maxTotalBytes: 7,
            minFreeDiskBytes: 1000,
            availableFreeSpaceProvider: _ => 0);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(oldEntry.Source, oldEntry.Preset), Is.Null);
            Assert.That(store.TryGet(newEntry.Source, newEntry.Preset), Is.Not.Null);
        });
    }

    // Finding C: a regenerate for a candidate's (Source, Preset) that lands between collection and the
    // delete loop must not have its fresh entry removed — Delete keys on (Source, Preset), so the sweep
    // re-checks that the current entry is still the ranked proxy (a regenerate bumps GeneratedAtUtc) first.
    [Test]
    public void Sweep_EntryRegeneratedSinceCollection_IsNotDeleted()
    {
        string root = CreateRoot();
        var inner = new ProxyStore(root);
        ProxyEntry collected = Register(inner, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry regenerated = collected with { GeneratedAtUtc = collected.GeneratedAtUtc.AddMinutes(1) };
        var store = new RegeneratedEntryStore(inner, regenerated);
        var service = new ProxyEvictionService(store, null, maxTotalBytes: 0);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(store.DeleteCalls, Is.EqualTo(0),
                "a candidate whose current entry was regenerated since collection must not be deleted");
            Assert.That(result.RemovedCount, Is.EqualTo(0));
        });
    }

    // The identity re-check must tolerate a LastUsedUtc-only change: ProxyStore.Touch replaces the entry
    // on every playback resolve, so a full-record comparison would read a touch as a regenerate and starve
    // eviction while the store is over cap.
    [Test]
    public void Sweep_EntryTouchedSinceCollection_StillEvicts()
    {
        string root = CreateRoot();
        var inner = new ProxyStore(root);
        ProxyEntry collected = Register(inner, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry touched = collected with { LastUsedUtc = collected.LastUsedUtc.AddMinutes(1) };
        var store = new RegeneratedEntryStore(inner, touched);
        var service = new ProxyEvictionService(store, null, maxTotalBytes: 0);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(store.DeleteCalls, Is.EqualTo(1), "a touch-only change must not block eviction");
            Assert.That(result.RemovedCount, Is.EqualTo(1));
        });
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProxyEntry Register(ProxyStore store, string root, string fileName, DateTime lastUsed, int bytes)
    {
        string sourcePath = Path.Combine(root, $"{Guid.NewGuid():N}.mov");
        File.WriteAllBytes(sourcePath, [1]);
        return RegisterForSource(store, root, sourcePath, fileName, lastUsed, bytes);
    }

    private static ProxyEntry RegisterForSource(
        ProxyStore store, string root, string sourcePath, string fileName, DateTime lastUsed, int bytes)
    {
        string proxyPath = Path.Combine(root, fileName);
        File.WriteAllBytes(proxyPath, Enumerable.Repeat((byte)1, bytes).ToArray());
        var source = ProxyFingerprint.FromFile(sourcePath);
        var entry = new ProxyEntry(
            source,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            fileName,
            bytes,
            new PixelSize(100, 100),
            new PixelSize(25, 25),
            lastUsed,
            lastUsed,
            null);
        store.Register(entry);
        return entry;
    }

    private sealed class PinOnSecondProbeResolver : IProxyResolver
    {
        private readonly HashSet<string> _probed = new(StringComparer.Ordinal);

        public bool IsPinned(string absoluteProxyFilePath) => !_probed.Add(absoluteProxyFilePath);

        public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset) => null;

        public long GetSourceVersion(string absolutePath) => 0;

        public IDisposable Pin(ProxyResolution resolution) => throw new NotSupportedException();
    }

    // Delegates to a real store but keeps the proxy file on disk after Delete, simulating a sharing
    // violation where the index entry is removed yet File.Delete could not remove the file.
    private sealed class RestoreFileOnDeleteStore(ProxyStore inner) : IProxyStore
    {
        public string StoreRootPath => inner.StoreRootPath;

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => inner.TryGet(source, preset);

        public IReadOnlyList<ProxyEntry> Enumerate() => inner.Enumerate();

        public void Register(ProxyEntry entry) => inner.Register(entry);

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
            => inner.TryTransition(source, preset, newState, failureReason);

        public bool Delete(ProxyFingerprint source, ProxyPreset preset)
        {
            ProxyEntry? entry = inner.TryGet(source, preset);
            string? path = entry is null
                ? null
                : Path.Combine(inner.StoreRootPath, entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
            byte[]? bytes = path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;

            bool result = inner.Delete(source, preset);

            if (bytes is not null && path is not null)
                File.WriteAllBytes(path, bytes);
            return result;
        }

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc) => inner.Touch(source, preset, nowUtc);

        public long GetTotalBytes() => inner.GetTotalBytes();

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => inner.GetTotalBytes(sourceAbsolutePaths);

        public Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public Task ReconcileAsync(CancellationToken cancellationToken) => inner.ReconcileAsync(cancellationToken);

        public event EventHandler<ProxyStoreChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }
    }

    // Enumerates the original entry (so it is collected as a candidate) but reports a different current
    // entry from TryGet, simulating a regenerate that replaced the same-key entry after collection.
    private sealed class RegeneratedEntryStore(ProxyStore inner, ProxyEntry regenerated) : IProxyStore
    {
        public int DeleteCalls { get; private set; }

        public string StoreRootPath => inner.StoreRootPath;

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => regenerated;

        public IReadOnlyList<ProxyEntry> Enumerate() => inner.Enumerate();

        public void Register(ProxyEntry entry) => inner.Register(entry);

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
            => inner.TryTransition(source, preset, newState, failureReason);

        public bool Delete(ProxyFingerprint source, ProxyPreset preset)
        {
            DeleteCalls++;
            return inner.Delete(source, preset);
        }

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc) => inner.Touch(source, preset, nowUtc);

        public long GetTotalBytes() => inner.GetTotalBytes();

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => inner.GetTotalBytes(sourceAbsolutePaths);

        public Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public Task ReconcileAsync(CancellationToken cancellationToken) => inner.ReconcileAsync(cancellationToken);

        public event EventHandler<ProxyStoreChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }
    }

    private sealed class TestHierarchical : Hierarchical
    {
        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    private sealed class TestFileSource(Uri? uri) : IFileSource
    {
        public Uri Uri { get; private set; } = uri!;

        public void ReadFrom(Uri uri)
        {
            Uri = uri;
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class TestEngineObjectWithFileSource : EngineObject
    {
        public TestEngineObjectWithFileSource(IFileSource? fileSource)
        {
            ScanProperties<TestEngineObjectWithFileSource>();
            FileSource.CurrentValue = fileSource;
        }

        public IProperty<IFileSource?> FileSource { get; } = Property.Create<IFileSource?>();
    }
}
