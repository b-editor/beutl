using Beutl.Composition;
using Beutl.Graphics;
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
        using var scope = ProxyTestScope.Create(new PixelSize(100, 80), new PixelSize(50, 40), ProxyPreset.Quarter);
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);
        using var compositor = CreateExportCompositor(scope, scope.OriginalPath);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
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
        using var scope = ProxyTestScope.Create(new PixelSize(100, 80), new PixelSize(50, 40), ProxyPreset.Quarter);
        DecoderRegistry.ProxyResolver = new ProxyResolver(scope.Store);

        // A Ready proxy is registered for an original that does not exist.
        // If export switched to the proxy, ProxyResolution would not be null.
        string missingOriginalPath = Path.Combine(scope.RootPath, "missing-source.mp4");

        // A dummy that passes the Ready checks of Register (exists, size matches); its content is never used.
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

        using var compositor = CreateExportCompositor(scope, missingOriginalPath);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
        var resource = (SourceVideo.Resource)frame.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(resource.Source, Is.Not.Null, "A VideoSource.Resource should still be produced even when the original is missing.");
            Assert.That(resource.Source!.ProxyResolution, Is.Null, "Export must not silently use a proxy when the original is missing.");
            Assert.That(resource.Source.MediaReader, Is.Null, "The export reader must not have opened a proxy as a substitute.");
        });
    }

    // An export compositor over a scene whose element plays mediaPath through a SourceVideo.
    private static SceneCompositor CreateExportCompositor(ProxyTestScope scope, string mediaPath)
    {
        var media = new VideoSource();
        media.ReadFrom(new Uri(mediaPath));
        var drawable = new SourceVideo();
        drawable.Source.Value = media;
        var element = new Element
        {
            Start = MediaTime.Zero,
            Length = MediaTime.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(scope.RootPath, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(100, 80, string.Empty)
        {
            Uri = new Uri(Path.Combine(scope.RootPath, "test.scene")),
        };
        scene.Children.Add(element);
        return new SceneCompositor(scene)
        {
            DisableResourceShare = true,
            ForceOriginalSource = true,
        };
    }
}
