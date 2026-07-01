using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Graphics;

[TestFixture]
[NonParallelizable]
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

    [Test]
    public void PreferProxyResource_DoesNotReload_WhenAnotherSourcesProxyChanges()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var store = new ProxyStore(root);
        DecoderRegistry.ProxyResolver = new ProxyResolver(store);

        // Source B is the one we observe: original media, no proxy of its own.
        string originalB = TestMediaHelper.CreateTestVideoFile(100, 80, new Rational(30, 1), 30);
        File.WriteAllBytes(originalB, [1, 2, 3, 4]);
        var sourceB = new VideoSource();
        sourceB.ReadFrom(new Uri(originalB));
        var context = new CompositionContext(TimeSpan.Zero) { PreferProxy = true };
        using var resourceB = sourceB.ToResource(context);
        int versionAfterLoad = resourceB.Version;
        PixelSize sizeAfterLoad = resourceB.FrameSize;

        // A completely unrelated source A (distinct dimensions => distinct file) gains
        // a Ready proxy.
        string originalA = TestMediaHelper.CreateTestVideoFile(120, 90, new Rational(30, 1), 30);
        File.WriteAllBytes(originalA, [1, 2, 3, 4]);
        string proxyTemplate = TestMediaHelper.CreateTestVideoFile(60, 45, new Rational(30, 1), 30);
        File.WriteAllBytes(proxyTemplate, [1, 2, 3, 4]);
        string relative = $"proxy/{Path.GetFileName(proxyTemplate)}";
        string proxy = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
        File.Copy(proxyTemplate, proxy, overwrite: true);
        var now = DateTime.UtcNow;
        store.Register(new ProxyEntry(
            ProxyFingerprint.FromFile(originalA),
            ProxyPreset.Quarter,
            ProxyState.Ready,
            relative.Replace(Path.DirectorySeparatorChar, '/'),
            new FileInfo(proxy).Length,
            new PixelSize(120, 90),
            new PixelSize(60, 45),
            now,
            now,
            null));

        // B must NOT reopen its reader just because A's proxy changed (FR-023).
        bool updateOnly = false;
        resourceB.Update(sourceB, context, ref updateOnly);

        Assert.Multiple(() =>
        {
            Assert.That(resourceB.ProxyResolution, Is.Null);
            Assert.That(resourceB.FrameSize, Is.EqualTo(sizeAfterLoad));
            Assert.That(resourceB.Version, Is.EqualTo(versionAfterLoad));
        });
    }

    [Test]
    public void ImmediateCanvas_DrawVideoSource_ScalesProxyBitmapToOriginalLogicalSize()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var scope = ProxyScope.Create(new PixelSize(100, 80), new PixelSize(50, 40));
            DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

            var source = new VideoSource();
            source.ReadFrom(new Uri(scope.OriginalPath));
            using var resource = source.ToResource(new CompositionContext(TimeSpan.Zero) { PreferProxy = true });
            using RenderTarget target = RenderTarget.Create(100, 80)!;

            using (var canvas = new ImmediateCanvas(target, 1f))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawVideoSource(resource, 1, Brushes.Resource.White, null);
            }

            using Bitmap snapshot = target.Snapshot();
            using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Bgra8888 lowerRightSample = srgb.GetRow<Bgra8888>(70)[90];

            Assert.Multiple(() =>
            {
                Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(50, 40)));
                Assert.That(resource.LogicalFrameSize, Is.EqualTo(new PixelSize(100, 80)));
                Assert.That(lowerRightSample.R, Is.GreaterThan(0));
                Assert.That(lowerRightSample.G, Is.GreaterThan(0));
                Assert.That(lowerRightSample.B, Is.GreaterThan(0));
            });
        });
    }

    [Test]
    public void SceneRenderer_DrawsProxyVideoAtOriginalLogicalFootprint()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
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
            var scene = new Scene(120, 100, string.Empty)
            {
                Uri = new Uri(Path.Combine(scope.RootPath, "test.scene")),
                PreviewSourceMode = PreviewSourceMode.PreferProxy,
            };
            scene.Children.Add(element);

            using var renderer = new SceneRenderer(scene);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(1d / 30d)));

            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Bgra8888 insideLogicalBounds = srgb.GetRow<Bgra8888>(70)[90];
            Bgra8888 outsideLogicalBounds = srgb.GetRow<Bgra8888>(90)[110];

            Assert.Multiple(() =>
            {
                Assert.That(insideLogicalBounds.R, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.G, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.B, Is.GreaterThan(0));
                Assert.That(outsideLogicalBounds.R, Is.EqualTo(0));
                Assert.That(outsideLogicalBounds.G, Is.EqualTo(0));
                Assert.That(outsideLogicalBounds.B, Is.EqualTo(0));
            });
        });
    }

    [Test]
    public void SceneRenderer_DrawsFilteredProxyVideoAtOriginalLogicalFootprint()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var scope = ProxyScope.Create(new PixelSize(100, 80), new PixelSize(50, 40));
            DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

            var media = new VideoSource();
            media.ReadFrom(new Uri(scope.OriginalPath));
            var drawable = new SourceVideo();
            drawable.Source.CurrentValue = media;
            drawable.FilterEffect.CurrentValue = new MosaicEffect();
            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(1),
                IsEnabled = true,
                Uri = new Uri(Path.Combine(scope.RootPath, $"{Guid.NewGuid():N}.layer")),
            };
            element.AddObject(drawable);
            var scene = new Scene(120, 100, string.Empty)
            {
                Uri = new Uri(Path.Combine(scope.RootPath, "test.scene")),
                PreviewSourceMode = PreviewSourceMode.PreferProxy,
            };
            scene.Children.Add(element);

            using var renderer = new SceneRenderer(scene);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(1d / 30d)));

            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Bgra8888 insideLogicalBounds = srgb.GetRow<Bgra8888>(70)[90];
            Bgra8888 outsideLogicalBounds = srgb.GetRow<Bgra8888>(90)[110];

            Assert.Multiple(() =>
            {
                Assert.That(insideLogicalBounds.R, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.G, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.B, Is.GreaterThan(0));
                Assert.That(outsideLogicalBounds.R, Is.EqualTo(0));
                Assert.That(outsideLogicalBounds.G, Is.EqualTo(0));
                Assert.That(outsideLogicalBounds.B, Is.EqualTo(0));
            });
        });
    }

    [Test]
    public void SceneRenderer_DrawsSkiaFilteredProxyVideoAtOriginalLogicalFootprint()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var scope = ProxyScope.Create(new PixelSize(100, 80), new PixelSize(50, 40));
            DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

            var media = new VideoSource();
            media.ReadFrom(new Uri(scope.OriginalPath));
            var blur = new Blur();
            blur.Sigma.CurrentValue = new Size(3, 3);
            var drawable = new SourceVideo();
            drawable.Source.CurrentValue = media;
            drawable.FilterEffect.CurrentValue = blur;
            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(1),
                IsEnabled = true,
                Uri = new Uri(Path.Combine(scope.RootPath, $"{Guid.NewGuid():N}.layer")),
            };
            element.AddObject(drawable);
            var scene = new Scene(160, 130, string.Empty)
            {
                Uri = new Uri(Path.Combine(scope.RootPath, "test.scene")),
                PreviewSourceMode = PreviewSourceMode.PreferProxy,
            };
            scene.Children.Add(element);

            using var renderer = new SceneRenderer(scene);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(1d / 30d)));

            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Bgra8888 insideLogicalBounds = srgb.GetRow<Bgra8888>(70)[90];
            Bgra8888 outsideInflatedBounds = srgb.GetRow<Bgra8888>(110)[140];

            Assert.Multiple(() =>
            {
                Assert.That(insideLogicalBounds.R, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.G, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.B, Is.GreaterThan(0));
                Assert.That(outsideInflatedBounds.R, Is.EqualTo(0));
                Assert.That(outsideInflatedBounds.G, Is.EqualTo(0));
                Assert.That(outsideInflatedBounds.B, Is.EqualTo(0));
            });
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
