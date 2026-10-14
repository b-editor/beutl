using Beutl.Composition;
using Beutl.Configuration;
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
    private PreviewSourceMode _originalPreviewSourceMode;

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
        _originalPreviewSourceMode = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode;
        GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.PreferProxy;
    }

    [TearDown]
    public void TearDown()
    {
        DecoderRegistry.ProxyResolver = _oldResolver;
        GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = _originalPreviewSourceMode;
    }

    [Test]
    public void ProxiedVideo_UsesOriginalLogicalBoundsAndProxySupplyDensity()
    {
        using var scope = ProxyTestScope.Create(new PixelSize(100, 80), new PixelSize(50, 40), ProxyPreset.Half);
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        var source = new VideoSource();
        source.ReadFrom(new Uri(scope.OriginalPath));
        using var resource = source.ToResource(new CompositionContext(TimeSpan.Zero) { PreferProxy = true });
        using var node = new VideoSourceRenderNode(resource, frame: 0, Brushes.Resource.White, null);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(50, 40)));
            Assert.That(resource.LogicalFrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(node.Bounds.Size, Is.EqualTo(new Size(100, 80)));
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(0.5f).Within(1e-6));
        });
    }

    [Test]
    public void OriginalVideo_RemainsNativeSizeAndDensity()
    {
        string path = TestMediaHelper.CreateTestVideoFile(100, 80, new Rational(30, 1), 30);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        using var resource = source.ToResource(new CompositionContext(TimeSpan.Zero) { PreferProxy = false });
        using var node = new VideoSourceRenderNode(resource, frame: 0, Brushes.Resource.White, null);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(resource.LogicalFrameSize, Is.EqualTo(new PixelSize(100, 80)));
            Assert.That(node.Bounds.Size, Is.EqualTo(new Size(100, 80)));
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(1f));
        });
    }

    [Test]
    public void PreferProxyResource_ReloadsWhenProxyStoreChanges()
    {
        using var scope = ProxyTestScope.CreateWithoutProxy(new PixelSize(100, 80));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        var source = new VideoSource();
        source.ReadFrom(new Uri(scope.OriginalPath));
        var context = new CompositionContext(TimeSpan.Zero) { PreferProxy = true };
        using var resource = source.ToResource(context);

        ProxyTestScope.RegisterTestVideoProxy(
            scope.Store, scope.OriginalPath, new PixelSize(100, 80), new PixelSize(50, 40), ProxyPreset.Quarter);
        bool versionBumped = false;
        resource.Reconcile(source, context, ref versionBumped);

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
        // Source B is the one we observe: original media, no proxy of its own.
        using var scope = ProxyTestScope.CreateWithoutProxy(new PixelSize(100, 80));
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        var sourceB = new VideoSource();
        sourceB.ReadFrom(new Uri(scope.OriginalPath));
        var context = new CompositionContext(TimeSpan.Zero) { PreferProxy = true };
        using var resourceB = sourceB.ToResource(context);
        int versionAfterLoad = resourceB.Version;
        PixelSize sizeAfterLoad = resourceB.FrameSize;

        // A completely unrelated source A (distinct dimensions => distinct file) gains
        // a Ready proxy.
        string originalA = TestMediaHelper.CreateTestVideoFile(120, 90, new Rational(30, 1), 30);
        File.WriteAllBytes(originalA, [1, 2, 3, 4]);
        ProxyTestScope.RegisterTestVideoProxy(
            scope.Store, originalA, new PixelSize(120, 90), new PixelSize(60, 45), ProxyPreset.Quarter);

        // B must NOT reopen its reader just because A's proxy changed.
        bool versionBumped = false;
        resourceB.Reconcile(sourceB, context, ref versionBumped);

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
            using var scope = ProxyTestScope.Create(new PixelSize(100, 80), new PixelSize(50, 40), ProxyPreset.Half);
            DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

            var source = new VideoSource();
            source.ReadFrom(new Uri(scope.OriginalPath));
            using var resource = source.ToResource(new CompositionContext(TimeSpan.Zero) { PreferProxy = true });
            using RenderTarget target = RenderTarget.Create(100, 80)!;

            using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1f))
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
        AssertSceneDrawsProxyAtOriginalFootprint(createEffect: null, new PixelSize(120, 100), new PixelPoint(110, 90));
    }

    [Test]
    public void SceneRenderer_DrawsFilteredProxyVideoAtOriginalLogicalFootprint()
    {
        AssertSceneDrawsProxyAtOriginalFootprint(() => new MosaicEffect(), new PixelSize(120, 100), new PixelPoint(110, 90));
    }

    [Test]
    public void SceneRenderer_DrawsSkiaFilteredProxyVideoAtOriginalLogicalFootprint()
    {
        // The point lies outside the bounds the blur inflates.
        AssertSceneDrawsProxyAtOriginalFootprint(
            () =>
            {
                var blur = new Blur();
                blur.Sigma.CurrentValue = new Size(3, 3);
                return blur;
            },
            new PixelSize(160, 130),
            new PixelPoint(140, 110));
    }

    // Renders a scene with the 100x80 video through its Half proxy and checks that the video covers (90, 70) and
    // leaves the outside point black.
    private static void AssertSceneDrawsProxyAtOriginalFootprint(
        Func<FilterEffect>? createEffect, PixelSize sceneSize, PixelPoint outside)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var scope = ProxyTestScope.Create(new PixelSize(100, 80), new PixelSize(50, 40), ProxyPreset.Half);
            DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

            var media = new VideoSource();
            media.ReadFrom(new Uri(scope.OriginalPath));
            var drawable = new SourceVideo();
            drawable.Source.CurrentValue = media;
            if (createEffect != null)
            {
                drawable.FilterEffect.CurrentValue = createEffect();
            }

            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(1),
                IsEnabled = true,
                Uri = new Uri(Path.Combine(scope.RootPath, $"{Guid.NewGuid():N}.layer")),
            };
            element.AddObject(drawable);
            var scene = new Scene(sceneSize.Width, sceneSize.Height, string.Empty)
            {
                Uri = new Uri(Path.Combine(scope.RootPath, "test.scene")),
            };
            scene.Children.Add(element);

            using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(1d / 30d)));

            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Bgra8888 insideLogicalBounds = srgb.GetRow<Bgra8888>(70)[90];
            Bgra8888 outsideBounds = srgb.GetRow<Bgra8888>(outside.Y)[outside.X];

            Assert.Multiple(() =>
            {
                Assert.That(insideLogicalBounds.R, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.G, Is.GreaterThan(0));
                Assert.That(insideLogicalBounds.B, Is.GreaterThan(0));
                Assert.That(outsideBounds.R, Is.EqualTo(0));
                Assert.That(outsideBounds.G, Is.EqualTo(0));
                Assert.That(outsideBounds.B, Is.EqualTo(0));
            });
        });
    }
}
