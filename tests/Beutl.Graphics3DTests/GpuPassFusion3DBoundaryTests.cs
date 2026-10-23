using System.Numerics;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Textures;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class GpuPassFusion3DBoundaryTests
{
    [TestCase(0.3f, false)]
    [TestCase(0.5f, false)]
    [TestCase(1.7f, false)]
    [TestCase(0.3f, true)]
    [TestCase(0.5f, true)]
    [TestCase(1.7f, true)]
    public void DrawableTextureDensity_MatchesUnderFractionalParentTransforms(float scale, bool underFilter)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new RectShape();
            drawable.Width.Value = 11;
            drawable.Height.Value = 7;
            drawable.Fill.Value = Brushes.Red;
            var texture = new DrawableTextureSource();
            texture.Drawable.Value = drawable;
            texture.TextureWidth.Value = 11;
            texture.TextureHeight.Value = 7;
            using var resource = CreateSceneResource(CreateMaterial(MaterialTextureDependency.BasicDiffuseMap, texture));
            using var root = new TransformRenderNode(Matrix.CreateScale(scale, scale), TransformOperator.Prepend);
            var sceneNode = new Scene3DRenderNode(resource);
            using var filterResource = new Invert().ToResource(CompositionContext.Default);
            if (underFilter)
            {
                var filterNode = filterResource.CreateRenderNode();
                filterNode.AddChild(sceneNode);
                root.AddChild(filterNode);
            }
            else
            {
                root.AddChild(sceneNode);
            }
            using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(0, 0, 96, 72),
                OutputScale = 1.3f,
                MaxWorkingScale = 0.7f,
            });
            using var result = renderer.Rasterize();
            Assert.That(result.Bitmap, Is.Not.Null);
        });
    }

    [Test]
    public void Scene3D_MaterializesOneBackendBoundary_ThenResumesTwoDimensionalWork()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene3D();
            scene.RenderWidth.Value = 32;
            scene.RenderHeight.Value = 24;
            scene.BackgroundColor.Value = new Color(255, 32, 64, 96);
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            using var sceneNode = new Scene3DRenderNode(resource);
            using var root = new DownstreamShaderNode(sceneNode);

            using (CompiledRenderRequest compiled = Compile(root))
            {
                RenderFragmentReference sceneBoundary = compiled.Graph.Fragments
                    .Single(static fragment => fragment.Kind == RenderFragmentKind.OpaqueSource);
                CompiledShaderRun[] shaderRuns = compiled.ExecutionPlan.Islands
                    .Where(static island => island.ShaderRun is not null)
                    .Select(static island => island.ShaderRun!)
                    .ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(
                        sceneBoundary.ValueCardinality,
                        Is.EqualTo(RenderValueCardinality.ZeroOrOne));
                    Assert.That(
                        ((OpaqueRenderFragmentPayload)sceneBoundary.Payload!).Description.BackendBoundary,
                        Is.EqualTo(RenderBackendBoundary.Graphics3D));
                    Assert.That(compiled.ExecutionPlan.Islands.Count(island =>
                        island.ShaderRun is null
                        && ReferenceEquals(compiled.Graph.Fragments[island.FragmentIndices[0]], sceneBoundary)),
                        Is.EqualTo(1));
                    Assert.That(shaderRuns, Has.Length.EqualTo(1));
                });
                Assert.That(shaderRuns.Single().StageFragmentIndices, Has.Length.EqualTo(1));
            }

            using var renderer = new RenderNodeRenderer(
                root,
                new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = new Rect(0, 0, 32, 24),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                });
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            Bitmap bitmap = rasterization.Bitmap
                ?? throw new AssertionException("The 3D-to-2D hand-off must publish a bitmap.");
            Vector4 expectedBackground = scene.BackgroundColor.Value.ToLinearPremultiplied();
            Vector4 actualCenter = ReadLinearPixel(bitmap, 16, 12);

            Assert.Multiple(() =>
            {
                Assert.That(rasterization.Bounds, Is.EqualTo(new Rect(0, 0, 32, 24)));
                Assert.That(renderer.LastExecutionStatistics.Synchronizations, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(1));
                Assert.That(bitmap.GetPixelSpan<ushort>().ToArray(), Has.Some.Not.Zero,
                    "The 3D-to-2D hand-off must publish a non-vacuous value.");
                Assert.That(actualCenter.X, Is.EqualTo(expectedBackground.Z).Within(0.003f),
                    "The downstream shader must swap blue into the red channel.");
                Assert.That(actualCenter.Y, Is.EqualTo(expectedBackground.Y).Within(0.003f),
                    "The downstream shader must preserve the green channel.");
                Assert.That(actualCenter.Z, Is.EqualTo(expectedBackground.X).Within(0.003f),
                    "The downstream shader must swap red into the blue channel.");
                Assert.That(actualCenter.W, Is.EqualTo(expectedBackground.W).Within(0.003f),
                    "The downstream shader must preserve alpha.");
            });
        });
    }

    private static Vector4 ReadLinearPixel(Bitmap bitmap, int x, int y)
    {
        int offset = ((y * bitmap.Width) + x) * 4;
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        return new Vector4(
            (float)BitConverter.UInt16BitsToHalf(pixels[offset]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 1]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 2]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]));
    }

    [Test]
    public void Scene3D_ReusesItsRendererAcrossRequests()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene3D();
            scene.RenderWidth.Value = 32;
            scene.RenderHeight.Value = 24;
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            using var sceneNode = new Scene3DRenderNode(resource);
            Renderer3D? firstRenderer;

            using (var renderer = new RenderNodeRenderer(
                       sceneNode,
                       new RenderNodeRenderRequest
                       {
                           Intent = RenderIntent.Preview,
                           TargetDomain = new Rect(0, 0, 32, 24),
                           OutputScale = 1,
                           MaxWorkingScale = 1,
                           CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                       }))
            {
                Assert.That(resource.Renderer, Is.Null);
                using (renderer.Rasterize())
                {
                }

                firstRenderer = resource.Renderer;
                Assert.That(firstRenderer, Is.Not.Null);

                using (renderer.Rasterize())
                {
                }

                Assert.That(resource.Renderer, Is.SameAs(firstRenderer));
            }

            Assert.That(
                resource.Renderer,
                Is.SameAs(firstRenderer),
                "Request and RenderNodeRenderer cleanup must not dispose the scene-owned renderer.");
        });
    }

    [TestCase(MaterialTextureDependency.BasicDiffuseMap)]
    [TestCase(MaterialTextureDependency.PbrAlbedoMap)]
    [TestCase(MaterialTextureDependency.PbrNormalMap)]
    [TestCase(MaterialTextureDependency.PbrMetallicRoughnessMap)]
    [TestCase(MaterialTextureDependency.PbrEmissiveMap)]
    [TestCase(MaterialTextureDependency.PbrAOMap)]
    [TestCase(MaterialTextureDependency.TransparentColorMap)]
    public void Scene3D_DrawableTextureConsumesItsPlannedNestedTarget(
        MaterialTextureDependency dependency)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new RectShape();
            drawable.Width.Value = 12;
            drawable.Height.Value = 8;
            drawable.Fill.Value = new SolidColorBrush(new Color(255, 192, 0, 0));
            var texture = new DrawableTextureSource();
            texture.Drawable.Value = drawable;
            texture.TextureWidth.Value = 12;
            texture.TextureHeight.Value = 8;
            Material3D material = CreateMaterial(dependency, texture);
            using var resource = CreateSceneResource(material);
            using var sceneNode = new Scene3DRenderNode(resource);

            var targetDomain = new Rect(0, 0, 48, 36);
            using CompiledRenderRequest compiled = Compile(
                sceneNode,
                targetDomain,
                outputScale: 1.75f,
                maxWorkingScale: 0.75f);
            CompiledRenderRequest nested = compiled.NestedRequests.Single();
            NestedRenderTargetBinding binding = nested.Request.Options.TargetBinding
                ?? throw new AssertionException("The drawable texture has no planned target binding.");
            Assert.Multiple(() =>
            {
                Assert.That(nested.Request.Options.TargetDomain, Is.EqualTo(new Rect(0, 0, 12, 8)));
                Assert.That(nested.Request.Options.OutputScale, Is.EqualTo(0.75f));
                Assert.That(nested.Request.Options.MaxWorkingScale, Is.EqualTo(0.75f));
                Assert.That(binding.GetTexture(new Rect(0, 0, 12, 8), 0.75f), Is.Null);
            });

            using var registry = new RenderTargetPool(factory: null);
            (ushort[] renderedPixels, RenderExecutionStatistics statistics) = ExecuteIntoPool(registry, compiled);

            ushort[] controlPixels = RenderScene(CreateMaterial(dependency, texture: null));
            double maximumRgbDifference = MaximumRgbDifference(renderedPixels, controlPixels);
            Assert.Multiple(() =>
            {
                Assert.That(statistics.IntermediateTargetAcquisitions, Is.GreaterThanOrEqualTo(1));
                Assert.That(maximumRgbDifference, Is.GreaterThan(0.01),
                    "The textured scene must differ from the no-texture control in RGB content.");
                Assert.That(binding.Density, Is.EqualTo(0.75f),
                    "The planned drawable target must match the 3D surface density passed to GetTexture.");
                Assert.That(
                    PixelRect.FromRect(binding.LogicalBounds, binding.Density),
                    Is.EqualTo(new PixelRect(0, 0, 9, 6)));
                Assert.That(binding.IsDisposed, Is.True);
                Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
            });
        });
    }

    private static Scene3D.Resource CreateSceneResource(Material3D material)
    {
        var cube = new Cube3D();
        cube.Material.Value = material;
        var light = new DirectionalLight3D();
        light.Direction.Value = new Vector3(0, 0, 1);
        light.Intensity.Value = 1;
        light.IsEnabled.Value = true;

        var scene = new Scene3D();
        scene.RenderWidth.Value = 48;
        scene.RenderHeight.Value = 36;
        scene.BackgroundColor.Value = Colors.Black;
        scene.AmbientColor.Value = Colors.White;
        // Full white ambient saturates the clamped output, hiding every additive/specular
        // contribution and zeroing the emissive/normal/metallic-roughness RGB differences.
        scene.AmbientIntensity.Value = 0.2f;
        var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        resource.Objects.Add((Object3D.Resource)cube.ToResource(CompositionContext.Default));
        resource.Lights.Add((Light3D.Resource)light.ToResource(CompositionContext.Default));
        return resource;
    }

    private static ushort[] RenderScene(Material3D material)
    {
        using var resource = CreateSceneResource(material);
        using var sceneNode = new Scene3DRenderNode(resource);
        var targetDomain = new Rect(0, 0, 48, 36);
        using CompiledRenderRequest compiled = Compile(
            sceneNode,
            targetDomain,
            outputScale: 1.75f,
            maxWorkingScale: 0.75f);
        using var registry = new RenderTargetPool(factory: null);
        return ExecuteIntoPool(registry, compiled).Pixels;
    }

    // Executes a scene compiled at the 1.75 output scale under the 0.75 working-scale ceiling.
    private static (ushort[] Pixels, RenderExecutionStatistics Statistics) ExecuteIntoPool(
        RenderTargetPool registry, CompiledRenderRequest compiled)
        => Graphics3DTestHelpers.ExecuteIntoPool(
            registry,
            compiled,
            compiled.ExecutionTargetBounds,
            compiled.ExecutionTargetBounds,
            density: 1.75f,
            maxWorkingScale: 0.75f,
            translateToTarget: true);

    private static double MaximumRgbDifference(ushort[] textured, ushort[] control)
    {
        Assert.That(textured, Has.Length.EqualTo(control.Length));
        double maximum = 0;
        for (int offset = 0; offset < textured.Length; offset += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float texturedValue = (float)BitConverter.UInt16BitsToHalf(textured[offset + channel]);
                float controlValue = (float)BitConverter.UInt16BitsToHalf(control[offset + channel]);
                Assert.That(float.IsFinite(texturedValue), Is.True,
                    $"Textured output contains a non-finite value at component {offset + channel}.");
                Assert.That(float.IsFinite(controlValue), Is.True,
                    $"Control output contains a non-finite value at component {offset + channel}.");
                maximum = Math.Max(maximum, Math.Abs(texturedValue - controlValue));
            }
        }

        return maximum;
    }

    private static Material3D CreateMaterial(
        MaterialTextureDependency dependency,
        TextureSource? texture)
    {
        if (dependency == MaterialTextureDependency.BasicDiffuseMap)
        {
            var material = new BasicMaterial();
            material.DiffuseMap.Value = texture;
            return material;
        }

        if (dependency == MaterialTextureDependency.TransparentColorMap)
        {
            var material = new TransparentMaterial();
            material.ColorMap.Value = texture;
            return material;
        }

        var pbrMaterial = new PBRMaterial();
        if (dependency == MaterialTextureDependency.PbrEmissiveMap)
            pbrMaterial.Emissive.Value = Colors.White;
        switch (dependency)
        {
            case MaterialTextureDependency.PbrAlbedoMap:
                pbrMaterial.AlbedoMap.Value = texture;
                break;
            case MaterialTextureDependency.PbrNormalMap:
                pbrMaterial.NormalMap.Value = texture;
                break;
            case MaterialTextureDependency.PbrMetallicRoughnessMap:
                // The map multiplies the base factors, so zero bases would make it unobservable.
                pbrMaterial.Metallic.Value = 1f;
                pbrMaterial.Roughness.Value = 1f;
                pbrMaterial.MetallicRoughnessMap.Value = texture;
                break;
            case MaterialTextureDependency.PbrEmissiveMap:
                pbrMaterial.EmissiveMap.Value = texture;
                break;
            case MaterialTextureDependency.PbrAOMap:
                pbrMaterial.AOMap.Value = texture;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dependency), dependency, null);
        }

        return pbrMaterial;
    }

    private static CompiledRenderRequest Compile(RenderNode root)
        => Compile(root, new Rect(0, 0, 32, 24));

    private static CompiledRenderRequest Compile(
        RenderNode root,
        Rect targetDomain,
        float outputScale = 1,
        float maxWorkingScale = 1)
        => Graphics3DTestHelpers.Compile(root, new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: targetDomain,
            outputScale: outputScale,
            maxWorkingScale: maxWorkingScale,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled));

    private sealed class DownstreamShaderNode(RenderNode sceneNode) : RenderNode
    {
        private static readonly ShaderDescription s_shader = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.bgr, color.a); }");

        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.RecordSubtree(sceneNode))
                context.Publish(context.Shader(input, s_shader));
        }
    }

    public enum MaterialTextureDependency
    {
        BasicDiffuseMap,
        PbrAlbedoMap,
        PbrNormalMap,
        PbrMetallicRoughnessMap,
        PbrEmissiveMap,
        PbrAOMap,
        TransparentColorMap,
    }
}
