using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class DecoderRegistryProxyRoutingTests
{
    private IProxyResolver? _oldResolver;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    [SetUp]
    public void SetUp()
    {
        _oldResolver = DecoderRegistry.ProxyResolver;
        DecoderRegistry.ProxyResolver = null;
    }

    [TearDown]
    public void TearDown()
    {
        DecoderRegistry.ProxyResolver = _oldResolver;
    }

    [Test]
    public void OpenMediaFile_WhenPreferProxyAndReadyProxy_OpensProxy()
    {
        using var scope = ProxyRouteScope.Create(originalSize: new PixelSize(100, 100), proxySize: new PixelSize(50, 50));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video) { PreferProxy = true });

        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(50, 50)));
            Assert.That(reader.ProxyResolution, Is.Not.Null);
            Assert.That(reader.ProxyResolution!.OriginalLogicalFrameSize, Is.EqualTo(new PixelSize(100, 100)));
        });
    }

    [Test]
    public void OpenMediaFile_WhenPreferProxyAndNoProxy_OpensOriginal()
    {
        string original = CreateTestVideoFileWithBytes(100, 100);

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            original,
            new MediaOptions(MediaMode.Video) { PreferProxy = true });

        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(100, 100)));
            Assert.That(reader.ProxyResolution, Is.Null);
        });
    }

    [Test]
    public void OpenMediaFile_WhenPreferProxyFalse_OpensOriginalEvenWhenProxyExists()
    {
        using var scope = ProxyRouteScope.Create(originalSize: new PixelSize(100, 100), proxySize: new PixelSize(50, 50));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video) { PreferProxy = false });

        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(100, 100)));
            Assert.That(reader.ProxyResolution, Is.Null);
        });
    }

    [Test]
    public void OpenMediaFile_WhenOriginalUnavailableAndPreferProxyFalse_DoesNotSubstituteProxy()
    {
        using var scope = ProxyRouteScope.CreateUnsupportedOriginalWithProxy(proxySize: new PixelSize(50, 50));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);
        File.Delete(scope.OriginalPath);

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video) { PreferProxy = false });

        Assert.That(reader, Is.Null);
    }

    private static string CreateTestVideoFileWithBytes(int width, int height)
    {
        string path = TestMediaHelper.CreateTestVideoFile(width, height, new Rational(30, 1), 30);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private sealed class ProxyRouteScope : IDisposable
    {
        private readonly string _root;

        private ProxyRouteScope(string root, string originalPath, ProxyStore store)
        {
            _root = root;
            OriginalPath = originalPath;
            Store = store;
        }

        public string OriginalPath { get; }

        public ProxyStore Store { get; }

        public static ProxyRouteScope Create(PixelSize originalSize, PixelSize proxySize)
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            var store = new ProxyStore(root);
            string original = CreateTestVideoFileWithBytes(originalSize.Width, originalSize.Height);
            string proxyTemplate = CreateTestVideoFileWithBytes(proxySize.Width, proxySize.Height);
            string relative = $"proxy/{Path.GetFileName(proxyTemplate)}";
            string proxy = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
            File.Copy(proxyTemplate, proxy, overwrite: true);

            var now = DateTime.UtcNow;
            var entry = new ProxyEntry(
                ProxyFingerprint.FromFile(original),
                ProxyPreset.Quarter,
                ProxyState.Ready,
                relative.Replace(Path.DirectorySeparatorChar, '/'),
                new FileInfo(proxy).Length,
                originalSize,
                proxySize,
                now,
                now,
                null);
            store.Register(entry);

            return new ProxyRouteScope(root, original, store);
        }

        public static ProxyRouteScope CreateUnsupportedOriginalWithProxy(PixelSize proxySize)
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            var store = new ProxyStore(root);
            Directory.CreateDirectory(root);
            string original = Path.Combine(root, $"{Guid.NewGuid():N}.mov");
            File.WriteAllBytes(original, [1, 2, 3, 4]);
            string relative = $"proxy/{Path.GetFileName(CreateTestVideoFileWithBytes(proxySize.Width, proxySize.Height))}";
            string proxy = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
            File.Copy(CreateTestVideoFileWithBytes(proxySize.Width, proxySize.Height), proxy, overwrite: true);

            var now = DateTime.UtcNow;
            var entry = new ProxyEntry(
                ProxyFingerprint.FromFile(original),
                ProxyPreset.Quarter,
                ProxyState.Ready,
                relative.Replace(Path.DirectorySeparatorChar, '/'),
                new FileInfo(proxy).Length,
                new PixelSize(100, 100),
                proxySize,
                now,
                now,
                null);
            store.Register(entry);

            return new ProxyRouteScope(root, original, store);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
