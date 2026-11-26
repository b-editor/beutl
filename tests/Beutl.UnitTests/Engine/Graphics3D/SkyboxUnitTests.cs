using System.Numerics;
using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Skyboxes;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public sealed class SkyboxUnitTests
{
    [TestCase(typeof(ProceduralSkybox))]
    [TestCase(typeof(EquirectangularSkybox))]
    [TestCase(typeof(CubemapSkybox))]
    public void Scene_RoundTripsSkyboxAndItsProperties(Type type)
    {
        Skybox3D skybox = type == typeof(ProceduralSkybox)
            ? new ProceduralSkybox
            {
                SkyColor = { Value = Colors.Red },
                HorizonColor = { Value = Colors.Green },
                GroundColor = { Value = Colors.Blue },
                HorizonExponent = { Value = 1.5f },
                SunDirection = { Value = Vector3.UnitX },
                SunColor = { Value = Colors.Orange },
                SunSize = { Value = 7f },
                SunIntensity = { Value = 4f },
            }
            : type == typeof(EquirectangularSkybox)
                ? new EquirectangularSkybox { Texture = { Value = MakeDrawableTexture(64) } }
                : new CubemapSkybox
                {
                    PositiveX = { Value = MakeDrawableTexture(48) },
                    NegativeX = { Value = MakeDrawableTexture(49) },
                    PositiveY = { Value = MakeDrawableTexture(50) },
                    NegativeY = { Value = MakeDrawableTexture(51) },
                    PositiveZ = { Value = MakeDrawableTexture(52) },
                    NegativeZ = { Value = MakeDrawableTexture(53) },
                };
        skybox.Rotation.Value = new Vector3(15, 30, 45);
        skybox.Tint.Value = Colors.Pink;
        skybox.Intensity.Value = 2f;
        skybox.Opacity.Value = 0.4f;
        var scene = new Scene3D { Skybox = { Value = skybox } };

        JsonObject json = CoreSerializer.SerializeToJsonObject(scene);
        var restored = (Scene3D)CoreSerializer.DeserializeFromJsonObject(JsonNode.Parse(json.ToJsonString())!.AsObject(), typeof(Scene3D));
        Assert.That(restored.Skybox.Value, Is.TypeOf(type));
        Assert.That(JsonNode.DeepEquals(json, CoreSerializer.SerializeToJsonObject(restored)), Is.True);

        Assert.That(json.Remove(nameof(Scene3D.Skybox)), Is.True);
        var legacy = (Scene3D)CoreSerializer.DeserializeFromJsonObject(json, typeof(Scene3D));
        Assert.That(legacy.Skybox.Value, Is.Null);
    }

    [Test]
    public void Reconcile_MovesSceneVersionWhenAnySkyboxResourceChanges()
    {
        var texture = MakeDrawableTexture(64);
        var skybox = new EquirectangularSkybox { Texture = { Value = texture } };
        var scene = new Scene3D { Skybox = { Value = skybox } };
        CompositionContext context = CompositionContext.Default;
        using Scene3D.Resource resource = scene.ToResource(context);
        using var node = new Scene3DRenderNode(resource);
        var skyboxResource = (EquirectangularSkybox.Resource)resource.Skybox!;
        int sceneVersion = resource.Version;
        int skyboxVersion = skyboxResource.Version;

        Assert.That(Reconcile(), Is.False);
        Assert.That(node.Update(resource), Is.False);

        texture.TextureWidth.Value = 96;
        Assert.That(Reconcile(), Is.True);
        Assert.That(resource.Version, Is.EqualTo(sceneVersion + 1));
        Assert.That(resource.Skybox, Is.SameAs(skyboxResource));
        Assert.That(skyboxResource.Version, Is.EqualTo(skyboxVersion + 1));
        Assert.That(((DrawableTextureSource.Resource)skyboxResource.Texture!).TextureWidth, Is.EqualTo(96));
        Assert.That(node.Update(resource), Is.True);

        skybox.Rotation.Value = new Vector3(0, 90, 0);
        skybox.Opacity.Value = 0.5f;
        Assert.That(Reconcile(), Is.True);
        Assert.That(resource.Version, Is.EqualTo(sceneVersion + 2));
        Assert.That(skyboxResource.Version, Is.EqualTo(skyboxVersion + 2));
        Assert.That(skyboxResource.Rotation.Y, Is.EqualTo(90));
        Assert.That(skyboxResource.Opacity, Is.EqualTo(0.5f));

        scene.Skybox.Value = new ProceduralSkybox();
        Assert.That(Reconcile(), Is.True);
        Assert.That(resource.Skybox, Is.TypeOf<ProceduralSkybox.Resource>());
        Assert.That(skyboxResource.IsDisposed, Is.True);

        scene.Skybox.Value = null;
        Assert.That(Reconcile(), Is.True);
        Assert.That(resource.Skybox, Is.Null);

        bool Reconcile()
        {
            bool changed = false;
            resource.Reconcile(scene, context, ref changed);
            return changed;
        }
    }

    [TestCase(false, true, 1f, false)]
    [TestCase(true, false, 1f, false)]
    [TestCase(true, true, 0f, false)]
    [TestCase(true, true, 0.2f, true)]
    public void HitTest_AnswersForAnEnabledSkyboxWithPositiveOpacity(
        bool hasSkybox, bool enabled, float opacity, bool expected)
    {
        var scene = new Scene3D
        {
            RenderWidth = { Value = 32 },
            RenderHeight = { Value = 32 },
            Skybox = { Value = hasSkybox ? new ProceduralSkybox { IsEnabled = { Value = enabled }, Opacity = { Value = opacity } } : null }
        };
        using Scene3D.Resource resource = scene.ToResource(CompositionContext.Default);
        using var node = new Scene3DRenderNode(resource);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = new Rect(0, 0, 32, 32),
            CacheOptions = RenderCacheOptions.Disabled,
            Supports3DRendering = true,
        });
        Assert.That(renderer.HitTest(new Point(16, 16)), Is.EqualTo(expected));
        Assert.That(renderer.HitTest(new Point(40, 40)), Is.False);
    }

    [Test]
    public void HitTest_IgnoresSkyboxesWithoutDrawableContent()
    {
        Assert.That(HitTestCenter(new EquirectangularSkybox()), Is.False);
        Assert.That(HitTestCenter(new EquirectangularSkybox { Texture = { Value = new ImageTextureSource() } }), Is.False);
        Assert.That(HitTestCenter(new EquirectangularSkybox { Texture = { Value = new DrawableTextureSource() } }), Is.False);

        var disabledTexture = MakeDrawableTexture(64);
        disabledTexture.IsEnabled.Value = false;
        Assert.That(HitTestCenter(new EquirectangularSkybox { Texture = { Value = disabledTexture } }), Is.False);

        var disabledDrawable = MakeDrawableTexture(64);
        disabledDrawable.Drawable.Value!.IsEnabled.Value = false;
        Assert.That(HitTestCenter(new EquirectangularSkybox { Texture = { Value = disabledDrawable } }), Is.False);

        Assert.That(HitTestCenter(new EquirectangularSkybox { Texture = { Value = MakeDrawableTexture(64) } }), Is.True);

        Assert.That(HitTestCenter(new CubemapSkybox()), Is.False);
        Assert.That(HitTestCenter(new CubemapSkybox { PositiveX = { Value = new ImageTextureSource() } }), Is.False);
        Assert.That(HitTestCenter(new CubemapSkybox { PositiveX = { Value = MakeDrawableTexture(64) } }), Is.True);

        var transparentProcedural = new ProceduralSkybox
        {
            SkyColor = { Value = Colors.Transparent },
            HorizonColor = { Value = Colors.Transparent },
            GroundColor = { Value = Colors.Transparent },
        };
        Assert.That(HitTestCenter(transparentProcedural), Is.False);

        static bool HitTestCenter(Skybox3D skybox)
        {
            var scene = new Scene3D
            {
                RenderWidth = { Value = 32 },
                RenderHeight = { Value = 32 },
                Skybox = { Value = skybox },
            };
            using Scene3D.Resource resource = scene.ToResource(CompositionContext.Default);
            using var node = new Scene3DRenderNode(resource);
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(0, 0, 32, 32),
                CacheOptions = RenderCacheOptions.Disabled,
                Supports3DRendering = true,
            });
            return renderer.HitTest(new Point(16, 16));
        }
    }

    [Test]
    public void MediaDiscovery_FindsImagesAndDrawablesInsideSkyboxFaces()
    {
        string projectDir = Path.Combine(Path.GetTempPath(), "beutl-skybox-project");
        Uri faceUri = new(Path.Combine(Path.GetTempPath(), "skybox-face.png"));
        Uri drawableUri = new(Path.Combine(Path.GetTempPath(), "skybox-drawable.png"));
        var faceImage = new ImageSource();
        faceImage.ReadFrom(faceUri);
        var drawableImage = new ImageSource();
        drawableImage.ReadFrom(drawableUri);
        var skybox = new CubemapSkybox
        {
            PositiveX = { Value = new ImageTextureSource { Source = { Value = faceImage } } },
            NegativeX =
            {
                Value = new DrawableTextureSource
                {
                    Drawable = { Value = new SourceImage { Source = { Value = drawableImage } } },
                },
            },
        };
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.AddObject(new Scene3D { Skybox = { Value = skybox } });
        var scene = new Scene();
        scene.Children.Add(element);

        Uri[] expected = [faceUri, drawableUri];
        Assert.That(ProxySourceEnumerator.EnumerateFileSources(element).Select(source => source.Uri), Is.EquivalentTo(expected));
        Assert.That(MissingMediaService.GetFileUris(scene), Is.EquivalentTo(expected));
        Assert.That(ExternalResourceCollector.Collect(element, projectDir).FileSources.Select(source => source.OriginalUri).Distinct(),
            Is.EquivalentTo(expected));
    }

    private static DrawableTextureSource MakeDrawableTexture(int width)
        => new()
        {
            TextureWidth = { Value = width },
            Drawable = { Value = new RectShape { Width = { Value = 31 }, Height = { Value = 27 } } },
        };
}
