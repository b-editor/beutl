using Beutl.Audio.Effects;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneRenderServicesTests
{
    [Test]
    public void WithoutConfigurationPreservesTheRegistries()
    {
        SceneRenderServices services = CreateServices();
        SceneRenderServices withoutConfiguration = services.WithoutConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That(SceneRenderServices.Empty, Is.EqualTo(new SceneRenderServices()));
            Assert.That(SceneRenderServices.Empty.WithoutConfiguration(), Is.SameAs(SceneRenderServices.Empty));
            Assert.That(withoutConfiguration, Is.EqualTo(new SceneRenderServices(services.Fonts, services.Decoders)));
            Assert.That((services with { EditorConfig = null }).WithoutConfiguration(), Is.EqualTo(withoutConfiguration));
            Assert.That((services with { ProxyConfig = null }).WithoutConfiguration(), Is.EqualTo(withoutConfiguration));
            Assert.That(withoutConfiguration.WithoutConfiguration(), Is.SameAs(withoutConfiguration));
        });
    }

    [Test]
    public void RecordEqualityComparesEachServiceByReference()
    {
        SceneRenderServices services = CreateServices();
        SceneRenderServices copy = services with { };

        Assert.Multiple(() =>
        {
            Assert.That(copy, Is.EqualTo(services).And.Not.SameAs(services));
            Assert.That(services with { Fonts = new FontManager(new FontConfig()) }, Is.Not.EqualTo(services));
            Assert.That(services with { Decoders = new DecoderRegistry(new ExtensionConfig()) }, Is.Not.EqualTo(services));
            Assert.That(services with { EditorConfig = new EditorConfig() }, Is.Not.EqualTo(services));
            Assert.That(services with { ProxyConfig = new ProxyStoreConfig() }, Is.Not.EqualTo(services));
        });
    }

    [Test]
    public void RendererUsesTheSuppliedEditorCacheSettingsOrDefaults()
    {
        var scene = new Scene(16, 16, string.Empty);
        var configuration = new EditorConfig();
        configuration.IsNodeCacheEnabled.Value = true;
        configuration.NodeCacheMaxPixels.Value = 4096;
        configuration.NodeCacheMinPixels.Value = 4;
        SceneRenderServices services = SceneRenderServices.Empty with { EditorConfig = configuration };

        using var defaultRenderer = new SceneRenderer(scene, SceneRenderServices.Empty, RenderIntent.Preview);
        using var configuredRenderer = new SceneRenderer(scene, services, RenderIntent.Preview);

        Assert.Multiple(() =>
        {
            Assert.That(defaultRenderer.CacheOptions, Is.SameAs(RenderCacheOptions.Default));
            Assert.That(configuredRenderer.CacheOptions, Is.EqualTo(RenderCacheOptions.CreateFromConfiguration(configuration)));
            Assert.That(configuredRenderer.Compositor.Services, Is.SameAs(services));
        });
    }

    [Test]
    public void ReferencedGraphicsAndAudioInheritTheParentServices()
    {
        SceneRenderServices services = CreateServices();
        var parent = new Scene();
        var child = new Scene();
        var reference = SceneReferenceTestHelper.Create(parent, child);
        var childDrawable = new EvaluationCaptureDrawable();
        AddElement(child, childDrawable);
        AddElement(parent, new SceneDrawable { ReferencedScene = { Expression = reference } });
        AddElement(parent, new SceneSound { ReferencedScene = { Expression = reference } }, layer: 1);

        using var compositor = new SceneCompositor(parent, services);
        compositor.EvaluateGraphics(MediaTime.Zero);
        var graphicsCompositor = (SceneCompositor)childDrawable.Services!.SessionIdentity;
        var sound = (SceneSound.Resource)compositor.EvaluateAudio(TimeRange.FromSeconds(0, 0.01)).Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(graphicsCompositor.Services, Is.EqualTo(services));
            Assert.That(sound._compositor!.Services, Is.EqualTo(services));
        });
    }

    [Test]
    public void RefreshUsesForwardedServicesAndRebuildsWhenTheyChange()
    {
        SceneRenderServices services = CreateServices();
        var scene = new Scene();
        CompositionContext? captured = null;
        AddElement(scene, new EvaluationCaptureDrawable { OnEvaluate = context => captured = context });
        using var parent = new SceneCompositor(scene, services);
        parent.EvaluateGraphics(MediaTime.Zero);
        var forwarded = new CompositionContext(MediaTime.Zero) { Conditions = captured!.Conditions };
        SceneCompositor? nested = null;

        try
        {
            SceneCompositor.Refresh(ref nested, scene, forwarded);
            SceneCompositor first = nested!;
            Assert.That(first.Services, Is.EqualTo(services));

            SceneCompositor.Refresh(ref nested, scene, forwarded);
            Assert.That(nested, Is.SameAs(first));

            forwarded.Fonts = new FontManager(new FontConfig());
            forwarded.Decoders = new DecoderRegistry(new ExtensionConfig());
            SceneCompositor.Refresh(ref nested, scene, forwarded);
            Assert.That(nested, Is.Not.SameAs(first));
            Assert.That(nested!.Services, Is.EqualTo(services with { Fonts = forwarded.Fonts, Decoders = forwarded.Decoders }));
        }
        finally
        {
            nested?.Dispose();
        }
    }

    [Test]
    public void LayerAndAudioQueriesUseTheSameServicesAsTheirOwner()
    {
        SceneRenderServices services = CreateServices();
        var scene = new Scene();
        CompositionContext? layerContext = null;
        CompositionContext? audioContext = null;
        var drawable = new EvaluationCaptureDrawable { OnEvaluate = context => layerContext = context };
        var sound = new EvaluationCaptureSound { OnEvaluate = context => audioContext = context };
        AddElement(scene, drawable);
        AddElement(scene, sound, layer: 1);

        using var compositor = new SceneCompositor(scene, services);
        compositor.EvaluateGraphics(MediaTime.Zero);
        ICompositionEvaluationServices evaluation = drawable.Services!;
        using var layer = evaluation.EvaluateLayer(0, MediaTime.Zero);
        // Layer queries expose the requester as their session identity. A sample uses the query's own compositor.
        using var sample = layerContext!.CreateEvaluationSession();
        var layerCompositor = (SceneCompositor)sample.Context.EvaluationServices!.SessionIdentity;
        using var audio = evaluation.ReadAudio(MediaTime.Zero, MediaTime.FromSeconds(0.01));
        var audioCompositor = (SceneCompositor)audioContext!.EvaluationServices!.SessionIdentity;

        Assert.Multiple(() =>
        {
            Assert.That(layerCompositor.Services, Is.SameAs(services));
            Assert.That(audioCompositor.Services, Is.SameAs(services));
        });
    }

    [Test]
    public void ConstructorsRejectNullServices()
    {
        var scene = new Scene();
        using var directory = new TemporaryDirectory();

        Assert.Multiple(() =>
        {
            Assert.That(() => new SceneCompositor(scene, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
            Assert.That(() => new SceneRenderer(scene, null!, RenderIntent.Preview), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
            Assert.That(() => new SceneComposer(scene, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
            Assert.That(() => new ObjectTemplateService(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("renderServices"));
            Assert.That(() => new ObjectTemplateService(null!, directory.Path), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("renderServices"));
        });
    }

    [Test]
    public void TemplatePreviewMethodsRejectNullServices()
    {
        Assert.That(async () => await ObjectTemplatePreviewRenderer.RenderPngAsync(null!, new AudioEffectGroup()),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
        Assert.That(async () => await ObjectTemplatePreviewRenderer.RenderElementsPngAsync(null!, [], default, default),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("services"));
    }

    [Test]
    public async Task SavingATemplateOverridesOnlyThePreviewFonts()
    {
        SceneRenderServices services = CreateServices();
        var projectFonts = new FontManager(new FontConfig());
        using var directory = new TemporaryDirectory();
        using var templates = new ObjectTemplateService(services, directory.Path);
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.AddObject(new TemplateRenderServicesCapture());
        element.AddObject(new RectShape
        {
            Width = { Value = 32 },
            Height = { Value = 32 },
            Fill = { Value = new SolidColorBrush(Colors.Red) },
        });
        var captured = new List<SceneRenderServices>();
        TemplateRenderServicesCapture.OnEvaluate = captured.Add;

        try
        {
            ObjectTemplateItem? project = await templates.AddFromInstanceAsync(element, "Project", fonts: projectFonts);
            ObjectTemplateItem? host = await templates.AddFromInstanceAsync(element, "Host");

            Assert.Multiple(() =>
            {
                Assert.That(project?.Preview, Is.Not.Null.And.Not.Empty);
                Assert.That(host?.Preview, Is.Not.Null.And.Not.Empty);
                Assert.That(captured, Is.EqualTo(new[] { services with { Fonts = projectFonts }, services }));
            });
        }
        finally
        {
            TemplateRenderServicesCapture.OnEvaluate = null;
        }
    }

    private static SceneRenderServices CreateServices()
        => new(TestFonts.Manager, new DecoderRegistry(new ExtensionConfig()), new EditorConfig(), new ProxyStoreConfig());

    private static void AddElement(Scene scene, EngineObject obj, int layer = 0)
    {
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) }, ZIndex = { Value = layer } };
        element.AddObject(obj);
        scene.Children.Add(element);
    }
}

internal sealed partial class TemplateRenderServicesCapture : Drawable
{
    // Template previews evaluate a serialized copy of the element.
    public static Action<SceneRenderServices>? OnEvaluate { get; set; }

    public partial class Resource
    {
        protected override Size MeasureCore(Size availableSize) => Size.Empty;

        protected override void OnDraw(GraphicsContext2D context) { }

        partial void PostReconcile(TemplateRenderServicesCapture obj, CompositionContext context)
        {
            if (context.EvaluationServices?.SessionIdentity is SceneCompositor compositor)
                OnEvaluate?.Invoke(compositor.Services);
        }
    }
}
