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
    public void Resolve_ReturnsReadyEntry_AndTouchesOnce()
    {
        string source = CreateSourceFile();
        ProxyEntry entry = RegisterProxy(source, ProxyPreset.Quarter, new PixelSize(100, 80), new PixelSize(25, 20));
        DateTime beforeResolve = entry.LastUsedUtc;

        ProxyResolution? result = _resolver.Resolve(new Uri(source), ProxyPreset.Quarter);

        ProxyEntry? touched = _store.TryGet(entry.Source, ProxyPreset.Quarter);
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
}
