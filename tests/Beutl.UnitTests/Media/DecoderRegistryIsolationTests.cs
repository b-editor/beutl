using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Moq;

namespace Beutl.UnitTests.Media;

public sealed class DecoderRegistryIsolationTests
{
    [Test]
    public void SceneOpensTheSameFileWithItsOwnDecoder()
    {
        string file = Path.GetTempFileName();
        try
        {
            var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
            element.Objects.Add(new SourceVideo { Source = { Value = VideoSource.Open(file) } });
            var scene = new Scene(100, 100, "Scene");
            scene.Children.Add(element);
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
            {
                using var a = new SceneRenderer(scene, new SceneRenderServices(Decoders: CreateRegistry(40)), RenderIntent.Preview);
                using var b = new SceneRenderer(scene, new SceneRenderServices(Decoders: CreateRegistry(80)), RenderIntent.Preview);
                Assert.That(Render(a), Is.EqualTo(40));
                Assert.That(Render(b), Is.EqualTo(80));
                Assert.That(Render(a), Is.EqualTo(40));
            });

            static int Render(SceneRenderer renderer)
            {
                CompositionFrame frame = renderer.Compositor.EvaluateGraphics(MediaTime.Zero);
                renderer.Render(frame);
                return ((SourceVideo.Resource)frame.Objects.Single()).Source!.FrameSize.Width;
            }

            DecoderRegistry CreateRegistry(int width)
            {
                var decoder = new Mock<IDecoderInfo>();
                decoder.Setup(x => x.IsSupported(file)).Returns(true);
                decoder.Setup(x => x.Open(file, It.IsAny<MediaOptions>()))
                    .Returns(() => new TestMediaReader(new PixelSize(width, 10), new Rational(30), 30));
                var registry = new DecoderRegistry(new());
                registry.Register(decoder.Object);
                return registry;
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void OnlyOpeningAMediaSourceRequiresTheRegistry()
    {
        using var compositor = new SceneCompositor(new Scene(), SceneRenderServices.Empty);
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects, Is.Empty);
        var context = CompositionContext.Default;
        using var emptyVideo = new VideoSource().ToResource(context);
        using var emptyAudio = new SoundSource().ToResource(context);
        var path = Path.Combine(Path.GetTempPath(), "source.mp4");
        Assert.That(() => VideoSource.Open(path).ToResource(context), Throws.InvalidOperationException);
        Assert.That(() => SoundSource.Open(path).ToResource(context), Throws.InvalidOperationException);
    }

    [Test]
    public void SharedExtensionUnloadsOnlyTheDecodingInfoRegisteredForThatHost()
    {
        var extension = new TestExtension();
        var a = new DecoderRegistry(new());
        var b = new DecoderRegistry(new());
        using var servicesA = new EditorServiceRegistry().Add(() => a);
        using var servicesB = new EditorServiceRegistry().Add(() => b);
        var contextA = new ExtensionContext(servicesA);
        var contextB = new ExtensionContext(servicesB);

        extension.Load(contextA);
        extension.Load(contextB);
        var decoderB = b.EnumerateDecoder().Single();
        Assert.That(a.EnumerateDecoder().Single(), Is.Not.SameAs(decoderB));
        extension.Unload(contextA);
        Assert.That(a.EnumerateDecoder(), Is.Empty);
        Assert.That(b.EnumerateDecoder().Single(), Is.SameAs(decoderB));
        extension.Unload(contextB);
        Assert.That(b.EnumerateDecoder(), Is.Empty);
    }

    private sealed class TestExtension : DecodingExtension
    {
        public override IDecoderInfo GetDecoderInfo() => new TestDecoderInfo();
    }
}
