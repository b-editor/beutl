using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
[NonParallelizable]
public sealed class ExportRenderContextSafetyTests
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
    public void ExportCompositor_DoesNotPreferProxyEvenWhenScenePrefersProxy()
    {
        using var scope = ProxyScope.Create(new PixelSize(100, 80), new PixelSize(50, 40));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);
        var media = new VideoSource();
        media.ReadFrom(new Uri(scope.OriginalPath));
        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = media;
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(scope.RootPath, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(100, 80, string.Empty)
        {
            Uri = new Uri(Path.Combine(scope.RootPath, "test.scene")),
        };
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene, RenderIntent.Delivery)
        {
            DisableResourceShare = true,
            ForceOriginalSource = true,
        };

        CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.Zero);
        var resource = (SourceVideo.Resource)frame.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(resource.Source, Is.Not.Null);
            Assert.That(resource.Source!.FrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(resource.Source.ProxyResolution, Is.Null);
        });
    }

    [Test]
    public void ExportCompositor_WithMissingOriginalFile_FailsRatherThanUsingProxy()
    {
        using var scope = ProxyScope.Create(new PixelSize(100, 80), new PixelSize(50, 40));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        // Point the VideoSource at a path that does not exist on disk so the export-time
        // reader open fails. A Ready proxy IS registered for that source fingerprint, so if
        // FR-004 were violated (export silently substituting the proxy), ProxyResolution would
        // be non-null and the resource would decode from the proxy instead of failing.
        string missingOriginalPath = Path.Combine(scope.RootPath, "missing-source.mp4");

        // Create a dummy proxy file on disk so ProxyStore.Register's Ready-state validation
        // (file exists + size matches) passes; the proxy content is irrelevant — the test
        // asserts it is NOT used.
        string dummyProxyRelative = $"proxy/{Guid.NewGuid():N}.mp4";
        string dummyProxyPath = Path.Combine(scope.RootPath, dummyProxyRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dummyProxyPath)!);
        File.WriteAllBytes(dummyProxyPath, [1, 2, 3, 4]);
        long dummyProxySize = new FileInfo(dummyProxyPath).Length;

        scope.Store.Register(new ProxyEntry(
            new ProxyFingerprint(missingOriginalPath, 4, DateTime.UtcNow),
            ProxyPreset.Quarter,
            ProxyState.Ready,
            dummyProxyRelative,
            dummyProxySize,
            new PixelSize(100, 80),
            new PixelSize(50, 40),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null));

        var media = new VideoSource();
        media.ReadFrom(new Uri(missingOriginalPath));
        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = media;
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(scope.RootPath, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(100, 80, string.Empty)
        {
            Uri = new Uri(Path.Combine(scope.RootPath, "test.scene")),
        };
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene, RenderIntent.Delivery)
        {
            DisableResourceShare = true,
            ForceOriginalSource = true,
        };

        CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.Zero);
        var resource = (SourceVideo.Resource)frame.Objects.Single();

        Assert.Multiple(() =>
        {
            // FR-004: the proxy must NOT be substituted when the original is missing at export.
            Assert.That(resource.Source, Is.Not.Null, "A VideoSource.Resource should still be produced even when the original is missing.");
            Assert.That(resource.Source!.ProxyResolution, Is.Null, "Export must not silently use a proxy when the original is missing.");
            Assert.That(resource.Source.MediaReader, Is.Null, "The export reader must not have opened a proxy as a substitute.");
        });
    }

    private sealed class ProxyScope : IDisposable
    {
        private readonly string _root;
        private readonly string _originalPath;
        private readonly string _proxyTemplatePath;

        private ProxyScope(string root, string originalPath, string proxyTemplatePath, ProxyStore store)
        {
            _root = root;
            _originalPath = originalPath;
            _proxyTemplatePath = proxyTemplatePath;
            OriginalPath = originalPath;
            Store = store;
        }

        public string OriginalPath { get; }

        public string RootPath => _root;

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

            return new ProxyScope(root, original, proxyTemplate, store);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);

            if (File.Exists(_originalPath))
                File.Delete(_originalPath);

            if (File.Exists(_proxyTemplatePath))
                File.Delete(_proxyTemplatePath);
        }
    }
}
