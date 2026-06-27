using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Graphics;

[TestFixture]
public class ProxyVideoLogicalSizeTests
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
    public void ProxiedVideo_UsesOriginalLogicalBoundsAndProxySupplyDensity()
    {
        using var scope = ProxyScope.Create(new PixelSize(100, 80), new PixelSize(50, 40));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        var source = new VideoSource();
        source.ReadFrom(new Uri(scope.OriginalPath));
        using var resource = source.ToResource(new CompositionContext(TimeSpan.Zero) { PreferProxy = true });
        var node = new VideoSourceRenderNode(resource, frame: 0, Brushes.Resource.White, null);
        RenderNodeOperation[] operations = node.Process(new RenderNodeContext([]));

        Assert.Multiple(() =>
        {
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(50, 40)));
            Assert.That(resource.LogicalFrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(node.Bounds.Size, Is.EqualTo(new Size(100, 80)));
            Assert.That(operations[0].EffectiveScale.Value, Is.EqualTo(0.5f).Within(1e-6));
        });

        foreach (RenderNodeOperation operation in operations)
        {
            operation.Dispose();
        }
    }

    [Test]
    public void OriginalVideo_RemainsNativeSizeAndDensity()
    {
        string path = TestMediaHelper.CreateTestVideoFile(100, 80, new Rational(30, 1), 30);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        using var resource = source.ToResource(new CompositionContext(TimeSpan.Zero) { PreferProxy = false });
        var node = new VideoSourceRenderNode(resource, frame: 0, Brushes.Resource.White, null);
        RenderNodeOperation[] operations = node.Process(new RenderNodeContext([]));

        Assert.Multiple(() =>
        {
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(resource.LogicalFrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(node.Bounds.Size, Is.EqualTo(new Size(100, 80)));
            Assert.That(operations[0].EffectiveScale.Value, Is.EqualTo(1f));
        });

        foreach (RenderNodeOperation operation in operations)
        {
            operation.Dispose();
        }
    }

    [Test]
    public void PreferProxyResource_ReloadsWhenProxyStoreChanges()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var store = new ProxyStore(root);
        DecoderRegistry.ProxyResolver = new ProxyResolver(store);
        string original = TestMediaHelper.CreateTestVideoFile(100, 80, new Rational(30, 1), 30);
        File.WriteAllBytes(original, [1, 2, 3, 4]);
        string proxyTemplate = TestMediaHelper.CreateTestVideoFile(50, 40, new Rational(30, 1), 30);
        File.WriteAllBytes(proxyTemplate, [1, 2, 3, 4]);
        string relative = $"proxy/{Path.GetFileName(proxyTemplate)}";
        string proxy = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
        File.Copy(proxyTemplate, proxy, overwrite: true);

        var source = new VideoSource();
        source.ReadFrom(new Uri(original));
        var context = new CompositionContext(TimeSpan.Zero) { PreferProxy = true };
        using var resource = source.ToResource(context);

        var now = DateTime.UtcNow;
        store.Register(new ProxyEntry(
            ProxyFingerprint.FromFile(original),
            ProxyPreset.Quarter,
            ProxyState.Ready,
            relative.Replace(Path.DirectorySeparatorChar, '/'),
            new FileInfo(proxy).Length,
            new PixelSize(100, 80),
            new PixelSize(50, 40),
            now,
            now,
            null));
        bool updateOnly = false;
        resource.Update(source, context, ref updateOnly);

        Assert.Multiple(() =>
        {
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(50, 40)));
            Assert.That(resource.LogicalFrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(resource.ProxyResolution, Is.Not.Null);
        });
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
                ProxyPreset.Half,
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
}
