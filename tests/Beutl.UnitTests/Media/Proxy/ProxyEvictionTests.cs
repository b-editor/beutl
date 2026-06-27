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
}
