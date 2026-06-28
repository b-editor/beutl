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
    public void Resolve_IgnoresStaleEntries()
    {
        string source = CreateSourceFile();
        RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20), ProxyState.Stale);

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        Assert.That(result, Is.Null);
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
