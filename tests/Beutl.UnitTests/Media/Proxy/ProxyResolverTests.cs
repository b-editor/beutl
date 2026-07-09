using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyResolverTests
{
    private string _root = null!;
    private ProxyStore _store = null!;
    private ProxyResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        _store = new ProxyStore(_root);
        _resolver = new ProxyResolver(_store);
    }

    [Test]
    public void Resolve_ReturnsNull_WhenStoreHasNoEntry()
    {
        string source = CreateSourceFile();

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Resolve_ReturnsReadyEntry_AndTouchesOnce()
    {
        string source = CreateSourceFile();
        ProxyEntry entry = RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        DateTime beforeResolve = entry.LastUsedUtc;

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        ProxyEntry? touched = _store.TryGet(entry.Source, ProxyPreset.Quarter);
        await _store.FlushAsync(CancellationToken.None);
        var reloaded = new ProxyStore(_root);
        ProxyEntry? persisted = reloaded.TryGet(entry.Source, ProxyPreset.Quarter);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.AbsoluteProxyFilePath, Is.EqualTo(Path.Combine(_root, entry.ProxyFileRelative)));
            Assert.That(result.OriginalLogicalFrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(result.ProxyDecodedFrameSize, Is.EqualTo(new PixelSize(25, 20)));
            Assert.That(result.SupplyDensity, Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(touched!.LastUsedUtc, Is.GreaterThan(beforeResolve));
            Assert.That(persisted!.LastUsedUtc, Is.EqualTo(touched.LastUsedUtc));
        });
    }

    [Test]
    public void Resolve_FallsBackAcrossPresets()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Half, new PixelSize(100, 80), new PixelSize(50, 40));

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Preset, Is.EqualTo(ProxyPreset.Half));
            Assert.That(result.SupplyDensity, Is.EqualTo(0.5f).Within(1e-6));
        });
    }

    [Test]
    public void Resolve_HonorsPreferredPresetAsDensityCap()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        RegisterProxy(source, ProxyPreset.Half, new PixelSize(100, 80), new PixelSize(50, 40));

        // preferredPreset=Quarter caps the resolve at Quarter density, so the denser Half proxy is
        // NOT chosen even though it is available.
        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Preset, Is.EqualTo(ProxyPreset.Quarter));
            Assert.That(result.SupplyDensity, Is.EqualTo(0.25f).Within(1e-6));
        });
    }

    [Test]
    public void Resolve_UsesActualDensityForClampedPresetRanking()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Half, new PixelSize(7680, 4320), new PixelSize(1920, 1080));
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(7680, 4320), new PixelSize(1280, 720));

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Preset, Is.EqualTo(ProxyPreset.Half));
            Assert.That(result.SupplyDensity, Is.EqualTo(0.25f).Within(1e-6));
        });
    }

    [Test]
    public void Resolve_FallsBackToDensestOverall_WhenNothingUnderCap()
    {
        string source = CreateSourceFile();
        // Only a Half proxy exists; preferredPreset=Quarter caps below it, so nothing satisfies the
        // cap and the fallback serves the densest Ready proxy overall.
        RegisterProxy(source, ProxyPreset.Half, new PixelSize(100, 80), new PixelSize(50, 40));

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Preset, Is.EqualTo(ProxyPreset.Half));
            Assert.That(result.SupplyDensity, Is.EqualTo(0.5f).Within(1e-6));
        });
    }

    [Test]
    public void Resolve_PicksDensestProxyUnderCap_WhenMultipleUnderCapExist()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Eighth, new PixelSize(100, 80), new PixelSize(12, 10));
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));

        // Both Eighth and Quarter are at or under the Half cap; the densest-under-cap (Quarter) wins.
        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Half);

        Assert.That(result?.Preset, Is.EqualTo(ProxyPreset.Quarter));
    }

    [Test]
    public void Resolve_FallsBackToLowerFidelity_WhenNoneAtOrAboveRequested()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Eighth, new PixelSize(100, 80), new PixelSize(12, 10));

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Half);

        Assert.That(result?.Preset, Is.EqualTo(ProxyPreset.Eighth));
    }

    [Test]
    public void GetSourceVersion_BumpsOnlyChangedSource()
    {
        string sourceA = CreateSourceFile();
        string sourceB = CreateSourceFile();
        ProxyFingerprint fingerprintA = ProxyFingerprint.FromFile(sourceA);
        ProxyFingerprint fingerprintB = ProxyFingerprint.FromFile(sourceB);

        Assert.Multiple(() =>
        {
            Assert.That(_resolver.GetSourceVersion(fingerprintA), Is.EqualTo(0));
            Assert.That(_resolver.GetSourceVersion(fingerprintB), Is.EqualTo(0));
        });

        RegisterProxy(sourceA, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));

        long versionA = _resolver.GetSourceVersion(fingerprintA);
        Assert.Multiple(() =>
        {
            Assert.That(versionA, Is.GreaterThan(0));
            Assert.That(_resolver.GetSourceVersion(fingerprintB), Is.EqualTo(0));
        });

        // A real state change to A must keep advancing A's version (SC-003) ...
        _store.TryTransition(fingerprintA, ProxyPreset.Quarter, ProxyState.Stale);
        Assert.Multiple(() =>
        {
            Assert.That(_resolver.GetSourceVersion(fingerprintA), Is.GreaterThan(versionA));
            // ... without ever touching B.
            Assert.That(_resolver.GetSourceVersion(fingerprintB), Is.EqualTo(0));
        });
    }

    // An offline original cannot be fingerprinted, but its normalized path key still identifies its
    // store version, so a reader opened before its proxy existed reopens when one is registered.
    [Test]
    public void GetSourceVersion_ByPathKey_TracksBumpsAfterOriginalDeleted()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        long expected = _resolver.GetSourceVersion(fingerprint);

        File.Delete(source);
        string pathKey = ProxyFingerprint.ResolveComparableKey(source);

        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.GreaterThan(0));
            Assert.That(_resolver.GetSourceVersion(pathKey), Is.EqualTo(expected),
                "the path-keyed version must match the fingerprint-keyed one for an offline original");
        });
    }

    // Every successful Resolve calls Touch; if Touched bumped the version, resolve → touch →
    // version-bump → reader-reopen → resolve would loop on the preview hot path.
    [Test]
    public void GetSourceVersion_DoesNotBumpOnTouch()
    {
        string source = CreateSourceFile();
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        long version = _resolver.GetSourceVersion(fingerprint);

        _store.Touch(fingerprint, ProxyPreset.Quarter, DateTime.UtcNow);

        Assert.That(_resolver.GetSourceVersion(fingerprint), Is.EqualTo(version));
    }

    [Test]
    public void Resolve_IgnoresStaleEntries()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20), ProxyState.Stale);

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Resolve_TransitionsOldEntryToStale_WhenSourceChangesMidSession()
    {
        string source = CreateSourceFile();
        // Register a Ready proxy keyed on the source's current fingerprint.
        ProxyEntry entry = RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        ProxyFingerprint original = entry.Source;

        // Simulate an external mid-session edit: different size and mtime.
        File.WriteAllBytes(source, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        File.SetLastWriteTimeUtc(source, original.MtimeUtc.AddSeconds(30));

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.Multiple(() =>
        {
            // The new fingerprint no longer matches the stored entry → fall back to original.
            Assert.That(result, Is.Null);
            // The old (same-path, stale) entry is surfaced as Stale so the badge refreshes instead of
            // waiting for the next startup reconcile.
            ProxyEntry? surfaced = _store.TryGet(original, ProxyPreset.Quarter);
            Assert.That(surfaced, Is.Not.Null);
            Assert.That(surfaced!.State, Is.EqualTo(ProxyState.Stale));
        });
    }

    [Test]
    public void Pin_ReferenceCountsPath()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        ProxyResolution resolution = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter)!;

        using IDisposable first = _resolver.Pin(resolution);
        using IDisposable second = _resolver.Pin(resolution);

        Assert.That(_resolver.IsPinned(resolution.AbsoluteProxyFilePath), Is.True);
        second.Dispose();
        Assert.That(_resolver.IsPinned(resolution.AbsoluteProxyFilePath), Is.True);
        first.Dispose();
        Assert.That(_resolver.IsPinned(resolution.AbsoluteProxyFilePath), Is.False);
    }

    [Test]
    public void Pin_Unpin_Concurrent_StaysConsistent()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        ProxyResolution resolution = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter)!;

        const int threadCount = 16;
        const int iterations = 1000;
        var barrier = new Barrier(threadCount);
        int liveHandles = 0;
        int pinViolations = 0;

        Thread[] threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            threads[t] = new Thread(() =>
            {
                // Hold threadCount pins simultaneously at the barrier; IsPinned must be true there.
                Interlocked.Increment(ref liveHandles);
                using IDisposable barrierHandle = _resolver.Pin(resolution);
                if (!_resolver.IsPinned(resolution.AbsoluteProxyFilePath))
                    Interlocked.Increment(ref pinViolations);
                barrier.SignalAndWait();

                // IsPinned must be true right after each Pin — a racing Unpin must not drop it.
                for (int i = 0; i < iterations; i++)
                {
                    Interlocked.Increment(ref liveHandles);
                    using IDisposable handle = _resolver.Pin(resolution);
                    if (!_resolver.IsPinned(resolution.AbsoluteProxyFilePath))
                        Interlocked.Increment(ref pinViolations);
                    handle.Dispose();
                    Interlocked.Decrement(ref liveHandles);
                }

                barrierHandle.Dispose();
                Interlocked.Decrement(ref liveHandles);
            });
            threads[t].IsBackground = true;
        }

        foreach (Thread t in threads)
            t.Start();
        foreach (Thread t in threads)
            t.Join();

        Assert.Multiple(() =>
        {
            Assert.That(pinViolations, Is.Zero,
                "IsPinned must be true immediately after Pin; a racing Unpin must not drop a concurrent Pin");
            Assert.That(liveHandles, Is.EqualTo(0));
            Assert.That(_resolver.IsPinned(resolution.AbsoluteProxyFilePath), Is.False,
                "after all handles are disposed, IsPinned must be false");
        });

        // The count must not be stuck negative or corrupted: a subsequent single Pin makes IsPinned true.
        using (IDisposable post = _resolver.Pin(resolution))
        {
            Assert.That(_resolver.IsPinned(resolution.AbsoluteProxyFilePath), Is.True);
        }
        Assert.That(_resolver.IsPinned(resolution.AbsoluteProxyFilePath), Is.False);
    }

    [Test]
    public void Resolve_RejectsProxyPathEscapingStoreRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        string source = CreateSourceFile();
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        var store = new UnsafeStore(root, new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "../outside.mp4",
            3,
            new PixelSize(100, 80),
            new PixelSize(25, 20),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null));
        var resolver = new ProxyResolver(store);

        ProxyResolution? result = resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Resolve_RejectsReadyEntryWithInvalidRecordedSize()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = CreateSourceFile();
        string proxyPath = Path.Combine(root, "proxy.mp4");
        File.WriteAllBytes(proxyPath, [1, 2, 3]);
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        var store = new UnsafeStore(root, new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "proxy.mp4",
            0,
            new PixelSize(100, 80),
            new PixelSize(25, 20),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null));
        var resolver = new ProxyResolver(store);

        ProxyResolution? result = resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Resolve_ReturnsNullWhenReadyProxyFileIsMissing()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = CreateSourceFile();
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        var store = new UnsafeStore(root, new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "proxy.mp4",
            3,
            new PixelSize(100, 80),
            new PixelSize(25, 20),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null));
        var resolver = new ProxyResolver(store);

        ProxyResolution? result = resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Resolve_RejectsReadyEntryWithInvalidDimensions()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = CreateSourceFile();
        string proxyPath = Path.Combine(root, "proxy.mp4");
        File.WriteAllBytes(proxyPath, [1, 2, 3]);
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        var store = new UnsafeStore(root, new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "proxy.mp4",
            3,
            PixelSize.Empty,
            new PixelSize(25, 20),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null));
        var resolver = new ProxyResolver(store);

        ProxyResolution? result = resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SupplyDensity_ReturnsNeutralDensityForEmptyProxySize()
    {
        var resolution = new ProxyResolution(
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "proxy.mp4"),
            new ProxyFingerprint(Path.Combine(TestContext.CurrentContext.WorkDirectory, "source.mov"), 1, DateTime.UtcNow),
            ProxyPreset.Quarter,
            new PixelSize(100, 80),
            PixelSize.Empty);

        Assert.That(resolution.SupplyDensity, Is.EqualTo(1f));
    }

    private static string CreateSourceFile()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".mov");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private ProxyEntry RegisterProxy(
        string source,
        ProxyPreset preset,
        PixelSize originalSize,
        PixelSize proxySize,
        ProxyState state = ProxyState.Ready)
    {
        string relative = Path.Combine(Guid.NewGuid().ToString("N"), $"{preset}.mp4");
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);

        var now = DateTime.UtcNow;
        var entry = new ProxyEntry(
            ProxyFingerprint.FromFile(source),
            preset,
            state,
            relative.Replace(Path.DirectorySeparatorChar, '/'),
            3,
            originalSize,
            proxySize,
            now,
            now,
            null);

        _store.Register(entry);
        return entry;
    }

    private sealed class UnsafeStore(string root, ProxyEntry entry) : IProxyStore
    {
        public string StoreRootPath { get; } = root;

        public event EventHandler<ProxyStoreChangedEventArgs>? Changed;

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset)
        {
            return source == entry.Source && preset == entry.Preset ? entry : null;
        }

        public IReadOnlyList<ProxyEntry> Enumerate() => [entry];

        public void Register(ProxyEntry entry)
        {
        }

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
            => false;

        public bool Delete(ProxyFingerprint source, ProxyPreset preset) => false;

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc)
        {
            Changed?.Invoke(this, new ProxyStoreChangedEventArgs
            {
                Source = source,
                Preset = preset,
                Kind = ProxyStoreChangeKind.Touched,
            });
        }

        public long GetTotalBytes() => 0;

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => 0;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReconcileAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
