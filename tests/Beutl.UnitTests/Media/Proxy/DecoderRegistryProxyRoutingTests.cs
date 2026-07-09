using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
[NonParallelizable]
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
    public void OpenMediaFile_UsesPreferredProxyPreset()
    {
        using var scope = ProxyRouteScope.CreateWithPresets(
            originalSize: new PixelSize(100, 100),
            halfProxySize: new PixelSize(50, 50),
            quarterProxySize: new PixelSize(25, 25));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video)
            {
                PreferProxy = true,
                PreferredProxyPreset = ProxyPreset.Half,
            });

        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(50, 50)));
            Assert.That(reader.ProxyResolution?.Preset, Is.EqualTo(ProxyPreset.Half));
        });
    }

    [Test]
    public void OpenMediaFile_WhenProxyOpenFails_FallsBackToOriginal()
    {
        using var scope = ProxyRouteScope.CreateWithInvalidProxy(originalSize: new PixelSize(100, 100));
        var resolver = new ProxyResolver(scope.Store);
        DecoderRegistry.ProxyResolver = resolver;

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video) { PreferProxy = true });

        ProxyEntry entry = scope.Store.Enumerate().Single();
        string proxyPath = ProxyPathUtilities.ResolveRelativePath(scope.Store.StoreRootPath, entry.ProxyFileRelative);
        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(100, 100)));
            Assert.That(reader.ProxyResolution, Is.Null);
            // A leaked pin would permanently block eviction of that proxy file.
            Assert.That(resolver.IsPinned(proxyPath), Is.False);
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

    // Fix #3: a Ready proxy stands in for a moved/deleted original in PreferProxy mode. The original
    // can no longer be fingerprinted, so the resolver falls back to a path-keyed lookup.
    [Test]
    public void OpenMediaFile_WhenOriginalMissingAndPreferProxy_OpensProxy()
    {
        using var scope = ProxyRouteScope.Create(originalSize: new PixelSize(100, 100), proxySize: new PixelSize(50, 50));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);
        File.Delete(scope.OriginalPath);

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video) { PreferProxy = true });

        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(50, 50)));
            Assert.That(reader.ProxyResolution, Is.Not.Null);
            // Fix #5: the resolution's source key equals the store's version-bump key for this path, so
            // a reader that adopts it (VideoSource) still observes proxy register/replace/delete events.
            Assert.That(
                reader.ProxyResolution!.Source.AbsolutePath,
                Is.EqualTo(ProxyFingerprint.ResolveComparableKey(scope.OriginalPath)));
        });
    }

    // Fix #3: the same missing original must NOT substitute a proxy when proxy is not preferred (e.g.
    // export/save-frame forces the original) — there is nothing to open, so the result is null.
    [Test]
    public void OpenMediaFile_WhenOriginalMissingAndPreferProxyFalse_ReturnsNull()
    {
        using var scope = ProxyRouteScope.Create(originalSize: new PixelSize(100, 100), proxySize: new PixelSize(50, 50));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);
        File.Delete(scope.OriginalPath);

        using MediaReader? reader = DecoderRegistry.OpenMediaFile(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video) { PreferProxy = false });

        Assert.That(reader, Is.Null);
    }

    // Fix #3: MediaReader.Open defers the existence check in PreferProxy mode so the proxy fallback can
    // run; without a preferred proxy the missing original still throws FileNotFound up front.
    [Test]
    public void MediaReaderOpen_WhenOriginalMissing_ProxyPreferenceControlsFallback()
    {
        using var scope = ProxyRouteScope.Create(originalSize: new PixelSize(100, 100), proxySize: new PixelSize(50, 50));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);
        File.Delete(scope.OriginalPath);

        using MediaReader reader = MediaReader.Open(
            scope.OriginalPath,
            new MediaOptions(MediaMode.Video) { PreferProxy = true });

        Assert.Multiple(() =>
        {
            Assert.That(reader.ProxyResolution, Is.Not.Null);
            Assert.That(
                () => MediaReader.Open(scope.OriginalPath, new MediaOptions(MediaMode.Video) { PreferProxy = false }),
                Throws.InstanceOf<FileNotFoundException>());
        });
    }

    [Test]
    public void OpenMediaFile_WhenResolverThrows_FallsBackToOriginal()
    {
        string original = CreateTestVideoFileWithBytes(100, 100);
        DecoderRegistry.ProxyResolver = new ThrowingProxyResolver(throwOnPin: false);

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
    public void OpenMediaFile_WhenPinThrows_FallsBackToOriginal()
    {
        string original = CreateTestVideoFileWithBytes(100, 100);
        DecoderRegistry.ProxyResolver = new ThrowingProxyResolver(throwOnPin: true);

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

    // Fix #6: new Uri(rawPath) parses URI-reserved chars (#, ?) in a filename as delimiters, dropping
    // them from LocalPath so it no longer matches the path ProxyFingerprint stored; the escaped file
    // URI must round-trip the full path so those clips still resolve their proxy.
    [Test]
    [TestCase("clip#1.mov")]
    [TestCase("clip?1.mov")]
    [TestCase("plain.mov")]
    public void ToFileUri_RoundTripsReservedCharsInLocalPath(string fileName)
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);
        string full = Path.GetFullPath(path);

        Uri uri = DecoderRegistry.ToFileUri(path);

        Assert.Multiple(() =>
        {
            Assert.That(uri.IsFile, Is.True);
            Assert.That(uri.LocalPath, Is.EqualTo(full));
        });
    }

    private static string CreateTestVideoFileWithBytes(int width, int height)
    {
        string path = TestMediaHelper.CreateTestVideoFile(width, height, new Rational(30, 1), 30);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private sealed class ThrowingProxyResolver(bool throwOnPin) : IProxyResolver
    {
        public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset)
        {
            if (!throwOnPin)
                throw new InvalidOperationException("resolve failure");

            return new ProxyResolution(
                sourceUri.LocalPath,
                ProxyFingerprint.FromFile(sourceUri.LocalPath),
                preferredPreset,
                new PixelSize(100, 100),
                new PixelSize(50, 50));
        }

        public long GetSourceVersion(ProxyFingerprint source) => 0;

        public IDisposable Pin(ProxyResolution resolution) => throw new InvalidOperationException("pin failure");

        public bool IsPinned(string absoluteProxyFilePath) => false;
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

        public static ProxyRouteScope CreateWithPresets(PixelSize originalSize, PixelSize halfProxySize, PixelSize quarterProxySize)
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            var store = new ProxyStore(root);
            string original = CreateTestVideoFileWithBytes(originalSize.Width, originalSize.Height);
            RegisterProxy(store, root, original, ProxyPreset.Half, halfProxySize, "half");
            RegisterProxy(store, root, original, ProxyPreset.Quarter, quarterProxySize, "quarter");

            return new ProxyRouteScope(root, original, store);
        }

        public static ProxyRouteScope CreateWithInvalidProxy(PixelSize originalSize)
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            var store = new ProxyStore(root);
            string original = CreateTestVideoFileWithBytes(originalSize.Width, originalSize.Height);
            string relative = "proxy/not-a-test-video.testvideo";
            string proxy = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
            File.WriteAllBytes(proxy, [1, 2, 3]);
            var now = DateTime.UtcNow;
            store.Register(new ProxyEntry(
                ProxyFingerprint.FromFile(original),
                ProxyPreset.Quarter,
                ProxyState.Ready,
                relative,
                new FileInfo(proxy).Length,
                originalSize,
                new PixelSize(50, 50),
                now,
                now,
                null));

            return new ProxyRouteScope(root, original, store);
        }

        private static void RegisterProxy(
            ProxyStore store,
            string root,
            string original,
            ProxyPreset preset,
            PixelSize proxySize,
            string fileName)
        {
            string proxyTemplate = CreateTestVideoFileWithBytes(proxySize.Width, proxySize.Height);
            string relative = $"proxy/{fileName}-{Path.GetFileName(proxyTemplate)}";
            string proxy = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
            File.Copy(proxyTemplate, proxy, overwrite: true);
            var now = DateTime.UtcNow;
            store.Register(new ProxyEntry(
                ProxyFingerprint.FromFile(original),
                preset,
                ProxyState.Ready,
                relative.Replace(Path.DirectorySeparatorChar, '/'),
                new FileInfo(proxy).Length,
                new PixelSize(100, 100),
                proxySize,
                now,
                now,
                null));
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
