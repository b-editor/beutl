using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics3D;

[NonParallelizable]
[TestFixture]
public class Scene3DRenderNodeScaleTests
{
    [Test]
    public void Measure_RespectsMaxWorkingScale_WhenOutputScaleIsHigher()
    {
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = 32;
        scene.RenderHeight.CurrentValue = 32;
        using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        using var node = new Scene3DRenderNode(resource);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                OutputScale = 2,
                MaxWorkingScale = 0.5f,
                UseRenderCache = false,
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.True,
            "Scene3DRenderNode emitted no fragment for a valid scene");
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False);
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(0.5f).Within(1e-4));
    }

    [Test]
    public void Recording_DrawableMaterialTextureUsesSceneWorkingScaleAndFullDomain()
    {
        var drawable = new RectShape();
        drawable.Width.CurrentValue = 5;
        drawable.Height.CurrentValue = 3;
        drawable.Fill.CurrentValue = Brushes.Red;
        var texture = new DrawableTextureSource();
        texture.Drawable.CurrentValue = drawable;
        texture.TextureWidth.CurrentValue = 11;
        texture.TextureHeight.CurrentValue = 7;
        var material = new BasicMaterial();
        material.DiffuseMap.CurrentValue = texture;
        var cube = new Cube3D();
        cube.Material.CurrentValue = material;

        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = 32;
        scene.RenderHeight.CurrentValue = 24;
        using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        resource.Objects.Add((Object3D.Resource)cube.ToResource(CompositionContext.Default));
        using var node = new Scene3DRenderNode(resource);
        using var owner = new RenderRequestOwner();
        var diagnostics = new RenderPipelineDiagnosticsState();
        var options = new RenderRequestOptions(
            RenderIntent.Delivery,
            RenderRequestPurpose.Frame,
            targetDomain: new Rect(0, 0, 32, 24),
            requestedRegion: new Rect(1, 2, 20, 10),
            outputScale: 1.75f,
            maxWorkingScale: 0.75f,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Disabled,
            owner: owner,
            diagnostics: diagnostics);
        using var request = new RenderRequest(options);

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RecordedNestedRenderRequest nested = graph.NestedRequests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(graph.PublicationRoots, Has.Length.EqualTo(1));
            Assert.That(nested.Request.State, Is.EqualTo(RenderRequestState.Recorded));
            Assert.That(nested.Request.Options.TargetDomain, Is.EqualTo(new Rect(0, 0, 11, 7)));
            Assert.That(nested.Request.Options.RequestedRegion, Is.EqualTo(new Rect(0, 0, 11, 7)));
            Assert.That(nested.Request.Options.Intent, Is.EqualTo(options.Intent));
            Assert.That(nested.Request.Options.Purpose, Is.EqualTo(options.Purpose));
            Assert.That(nested.Request.Options.OutputScale, Is.EqualTo(0.75f));
            Assert.That(nested.Request.Options.MaxWorkingScale, Is.EqualTo(0.75f));
            Assert.That(nested.Request.Options.CachePolicy, Is.EqualTo(options.CachePolicy));
            Assert.That(nested.Request.Options.FusionMode, Is.EqualTo(options.FusionMode));
            Assert.That(nested.Request.Options.Owner, Is.SameAs(owner));
            Assert.That(nested.Request.Options.Diagnostics, Is.SameAs(diagnostics));
            Assert.That(nested.Request.Options.TargetBinding, Is.Not.Null);
            Assert.That(nested.Request.Options.TargetBinding!.IsReady, Is.False,
                "CPU recording must not allocate, execute, or prepare the nested target.");
            Assert.That(nested.Request.Options.TargetBinding.DeviceBounds, Is.EqualTo(default(PixelRect)));
        });
    }

    [Test]
    public void Recording_DrawableTextureThatReferencesItsSceneFailsWithAnExplicitCycleAndRollsBack()
    {
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = 32;
        scene.RenderHeight.CurrentValue = 24;
        var texture = new DrawableTextureSource();
        texture.TextureWidth.CurrentValue = 11;
        texture.TextureHeight.CurrentValue = 7;
        var material = new BasicMaterial();
        var cube = new Cube3D();

        var sceneResource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        var textureResource = (DrawableTextureSource.Resource)texture.ToResource(CompositionContext.Default);
        var materialResource = (BasicMaterial.Resource)material.ToResource(CompositionContext.Default);
        var cubeResource = (Cube3D.Resource)cube.ToResource(CompositionContext.Default);
        textureResource.Drawable = sceneResource;
        materialResource.DiffuseMap = textureResource;
        cubeResource.Material = materialResource;
        sceneResource.Objects.Add(cubeResource);

        try
        {
            using var node = new Scene3DRenderNode(sceneResource);
            using var owner = new RenderRequestOwner();
            using var request = new RenderRequest(new RenderRequestOptions(
                RenderIntent.Preview,
                RenderRequestPurpose.Frame,
                targetDomain: new Rect(0, 0, 32, 24),
                cachePolicy: RenderCacheOptions.Disabled,
                owner: owner));

            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                () => new RenderRequestRecorder(request).Record(node));

            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain("render-node recording cycle"));
                Assert.That(failure.Message, Does.Contain("->"));
                Assert.That(request.State, Is.EqualTo(RenderRequestState.Failed));
                Assert.That(owner.IsCleanedUp, Is.True);
                Assert.That(owner.CleanupFailures, Is.Empty);
            });
        }
        finally
        {
            sceneResource.Objects.Clear();
            cubeResource.Material = null;
            materialResource.DiffuseMap = null;
            textureResource.Drawable = null;
            cubeResource.Dispose();
            materialResource.Dispose();
            textureResource.Dispose();
            sceneResource.Dispose();
        }
    }
}
