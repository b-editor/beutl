using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

// F-TL-1: the timeline filmstrip must honor the scene's PreferProxy intent. Passing preferProxy:true
// to GetThumbnailStripAsync must route the decode through the proxy resolver (so a 4K/8K clip decodes
// its filmstrip from the smaller proxy), while preferProxy:false must never consult the resolver.
[TestFixture]
[NonParallelizable]
public class SourceVideoThumbnailProxyTests
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
    public async Task GetThumbnailStrip_WhenPreferProxy_ConsultsProxyResolver()
    {
        using var scope = ProxyScope.Create(new PixelSize(100, 100), new PixelSize(50, 50));
        var resolver = new SpyProxyResolver(new ProxyResolver(scope.Store));
        DecoderRegistry.ProxyResolver = resolver;

        await ConsumeThumbnailProbeAsync(scope.OriginalPath, preferProxy: true);

        Assert.That(resolver.ResolveCallCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetThumbnailStrip_WhenNotPreferProxy_DoesNotConsultProxyResolver()
    {
        using var scope = ProxyScope.Create(new PixelSize(100, 100), new PixelSize(50, 50));
        var resolver = new SpyProxyResolver(new ProxyResolver(scope.Store));
        DecoderRegistry.ProxyResolver = resolver;

        await ConsumeThumbnailProbeAsync(scope.OriginalPath, preferProxy: false);

        Assert.That(resolver.ResolveCallCount, Is.Zero);
    }

    [Test]
    public void PreferProxy_SecondFreshResource_ReusesSharedReaderWithoutReopening()
    {
        using var scope = ProxyScope.Create(new PixelSize(100, 100), new PixelSize(50, 50));
        var resolver = new SpyProxyResolver(new ProxyResolver(scope.Store));
        DecoderRegistry.ProxyResolver = resolver;

        var source = new VideoSource();
        source.ReadFrom(new Uri(scope.OriginalPath));
        var context = new CompositionContext(TimeSpan.Zero) { PreferProxy = true };

        using var first = source.ToResource(context);
        int resolvesAfterFirst = resolver.ResolveCallCount;

        using var second = source.ToResource(context);

        // A fresh Resource must reuse the source's shared proxy reader rather than opening (and
        // resolving) a second one; before the fix its default-valued loaded fields blocked reuse.
        Assert.Multiple(() =>
        {
            Assert.That(resolvesAfterFirst, Is.GreaterThan(0), "the first resource should open a proxy reader");
            Assert.That(resolver.ResolveCallCount, Is.EqualTo(resolvesAfterFirst));
        });
    }

    // F-TL-3: a proxy register/delete must drop the filmstrip cache for BOTH preview-source modes,
    // since the strip is stored under baseKey + "|proxy" / "|original" and Invalidate(baseKey) hits
    // neither (nothing is stored under the unsuffixed key).
    [Test]
    public void InvalidateThumbnailCacheKeys_DropsBothModePartitions()
    {
        var cache = new RecordingThumbnailCacheService();

        SourceVideo.InvalidateThumbnailCacheKeys(cache, "clip");

        Assert.Multiple(() =>
        {
            Assert.That(cache.Invalidated, Is.EquivalentTo(new[] { "clip" + SourceVideo.ProxyThumbnailCacheKeySuffix, "clip" + SourceVideo.OriginalThumbnailCacheKeySuffix }));
            Assert.That(cache.Invalidated.Contains("clip"), Is.False, "the unsuffixed base key is never stored and must not be the only key invalidated");
        });
    }

    [Test]
    public void InvalidateThumbnailCacheKeys_NullBaseKey_IsNoOp()
    {
        var cache = new RecordingThumbnailCacheService();
        SourceVideo.InvalidateThumbnailCacheKeys(cache, null);
        Assert.That(cache.Invalidated, Is.Empty);
    }

    // startIndex > endIndex skips the per-frame rasterization loop (which needs a GPU), so only the
    // media-open seam runs — enough to observe whether the proxy resolver was consulted GPU-free.
    private static async Task ConsumeThumbnailProbeAsync(string originalPath, bool preferProxy)
    {
        var media = new VideoSource();
        media.ReadFrom(new Uri(originalPath));
        var drawable = new SourceVideo
        {
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        };
        drawable.Source.CurrentValue = media;

        await foreach (var _ in drawable.GetThumbnailStripAsync(
                           maxWidth: 100,
                           maxHeight: 25,
                           cacheService: null,
                           cancellationToken: CancellationToken.None,
                           startIndex: 1,
                           endIndex: 0,
                           preferProxy: preferProxy))
        {
        }
    }

    private sealed class SpyProxyResolver(IProxyResolver inner) : IProxyResolver
    {
        public int ResolveCallCount { get; private set; }

        public long GetSourceVersion(ProxyFingerprint source) => inner.GetSourceVersion(source);

        public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset)
        {
            ResolveCallCount++;
            return inner.Resolve(sourceUri, preferredPreset);
        }

        public IDisposable Pin(ProxyResolution resolution) => inner.Pin(resolution);
    }

    private sealed class ProxyScope : IDisposable
    {
        private readonly string _root;

        private ProxyScope(string root, string originalPath, ProxyStore store)
        {
            _root = root;
            OriginalPath = originalPath;
            Store = store;
        }

        public string OriginalPath { get; }

        public ProxyStore Store { get; }

        public static ProxyScope Create(PixelSize originalSize, PixelSize proxySize)
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            var store = new ProxyStore(root);
            string original = TestMediaHelper.CreateTestVideoFile(originalSize.Width, originalSize.Height, new Rational(30, 1), 30);
            File.WriteAllBytes(original, [1, 2, 3, 4]);
            string proxyTemplate = TestMediaHelper.CreateTestVideoFile(proxySize.Width, proxySize.Height, new Rational(30, 1), 30);
            File.WriteAllBytes(proxyTemplate, [1, 2, 3, 4]);
            string relative = $"proxy/{Path.GetFileName(proxyTemplate)}";
            string proxy = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
            File.Copy(proxyTemplate, proxy, overwrite: true);

            var now = DateTime.UtcNow;
            store.Register(new ProxyEntry(
                ProxyFingerprint.FromFile(original),
                ProxyPreset.Quarter,
                ProxyState.Ready,
                relative.Replace(Path.DirectorySeparatorChar, '/'),
                new FileInfo(proxy).Length,
                originalSize,
                proxySize,
                now,
                now,
                null));

            return new ProxyScope(root, original, store);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingThumbnailCacheService : IThumbnailCacheService
    {
        public List<string> Invalidated { get; } = [];

        public bool TryGet(string cacheKey, TimeSpan time, TimeSpan threshold, out Bitmap? bitmap)
        {
            bitmap = null;
            return false;
        }

        public void Save(string cacheKey, TimeSpan time, Bitmap bitmap)
        {
        }

        public bool TryGetWaveform(string cacheKey, TimeSpan time, TimeSpan threshold, out float minValue, out float maxValue)
        {
            minValue = 0;
            maxValue = 0;
            return false;
        }

        public void SaveWaveform(string cacheKey, TimeSpan time, float minValue, float maxValue)
        {
        }

        public void Invalidate(string cacheKey) => Invalidated.Add(cacheKey);
    }
}
