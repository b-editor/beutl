using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Textures;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class GpuPassFusion3DBoundaryTests
{
    [Test]
    [Category("GpuPassFusionGpu")]
    public void Scene3D_MaterializesOneBackendBoundary_ThenResumesTwoDimensionalWork()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 32;
            scene.RenderHeight.CurrentValue = 24;
            scene.BackgroundColor.CurrentValue = new Color(255, 32, 64, 96);
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            using var sceneNode = new Scene3DRenderNode(resource);
            using var root = new DownstreamShaderNode(sceneNode);

            using (CompiledRenderRequest compiled = Compile(root))
            {
                CompiledShaderRun[] shaderRuns = compiled.ExecutionPlan.ShaderRuns.ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(compiled.ExecutionPlan.Boundaries.Count(static item =>
                        item.Reason == ExecutionIslandBoundaryReason.ThreeD), Is.EqualTo(1));
                    Assert.That(compiled.ExecutionPlan.Boundaries.Count(static item =>
                        item.Reason == ExecutionIslandBoundaryReason.BackendTransition), Is.EqualTo(1));
                    Assert.That(shaderRuns, Has.Length.EqualTo(1));
                });
                Assert.That(shaderRuns.Single().Stages, Has.Length.EqualTo(1));
            }

            using var renderer = new RenderNodeRenderer(
                root,
                new RenderNodeRendererOptions
                {
                    TargetDomain = new Rect(0, 0, 32, 24),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    UseRenderCache = false,
                });
            using RenderNodeRasterization rasterization = renderer.Rasterize();

            Assert.Multiple(() =>
            {
                Assert.That(rasterization.Bitmap, Is.Not.Null);
                Assert.That(rasterization.Bounds, Is.EqualTo(new Rect(0, 0, 32, 24)));
                Assert.That(renderer.LastExecutionStatistics.Synchronizations, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(1));
                Assert.That(rasterization.Bitmap!.GetPixelSpan<ushort>().ToArray(), Has.Some.Not.Zero,
                    "The 3D-to-2D hand-off must publish a non-vacuous value.");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void Scene3D_ReusesItsRendererAcrossRequests()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 32;
            scene.RenderHeight.CurrentValue = 24;
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            using var sceneNode = new Scene3DRenderNode(resource);
            Renderer3D? firstRenderer;

            using (var renderer = new RenderNodeRenderer(
                       sceneNode,
                       new RenderNodeRendererOptions
                       {
                           TargetDomain = new Rect(0, 0, 32, 24),
                           OutputScale = 1,
                           MaxWorkingScale = 1,
                           UseRenderCache = false,
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
    [Category("GpuPassFusionGpu")]
    public void Scene3D_DrawableTextureConsumesItsPlannedNestedTarget(
        MaterialTextureDependency dependency)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new RectShape();
            drawable.Width.CurrentValue = 12;
            drawable.Height.CurrentValue = 8;
            drawable.Fill.CurrentValue = Brushes.Red;
            var texture = new DrawableTextureSource();
            texture.Drawable.CurrentValue = drawable;
            texture.TextureWidth.CurrentValue = 12;
            texture.TextureHeight.CurrentValue = 8;
            Material3D material = CreateMaterial(dependency, texture);
            var cube = new Cube3D();
            cube.Material.CurrentValue = material;

            var scene = new Scene3D();
            scene.RenderWidth.CurrentValue = 48;
            scene.RenderHeight.CurrentValue = 36;
            scene.BackgroundColor.CurrentValue = Colors.Black;
            scene.AmbientColor.CurrentValue = Colors.White;
            scene.AmbientIntensity.CurrentValue = 1;
            using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
            resource.Objects.Add((Object3D.Resource)cube.ToResource(CompositionContext.Default));
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
                Assert.That(binding.IsReady, Is.False);
            });

            ushort[] renderedPixels;
            RenderExecutionStatistics statistics;
            using var registry = new RenderTargetLeaseRegistry(factory: null);
            using (RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview))
            using (RenderTargetLease root = targets.Acquire(
                       PixelRect.FromRect(compiled.ExecutionTargetBounds, 1.75f).Size))
            using (var canvas = new ImmediateCanvas(
                       root.Target,
                       density: 1.75f,
                       maxWorkingScale: 0.75f,
                       logicalSize: compiled.ExecutionTargetBounds.Size))
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       -compiled.ExecutionTargetBounds.X,
                       -compiled.ExecutionTargetBounds.Y)))
            {
                canvas.Clear();
                var executor = new RenderRequestExecutor(targets);
                executor.Execute(
                    compiled,
                    canvas,
                    replayBounds: compiled.ExecutionTargetBounds);
                statistics = executor.Statistics;
                using Bitmap rendered = root.Target.Snapshot();
                renderedPixels = rendered.GetPixelSpan<ushort>().ToArray();
            }

            Assert.Multiple(() =>
            {
                Assert.That(statistics.IntermediateTargetAcquisitions, Is.GreaterThanOrEqualTo(1));
                Assert.That(renderedPixels, Has.Some.Not.Zero,
                    "The textured scene must consume the prepared drawable target and publish pixels.");
                Assert.That(binding.Density, Is.EqualTo(0.75f),
                    "The planned drawable target must match the 3D surface density passed to GetTexture.");
                Assert.That(binding.DeviceBounds, Is.EqualTo(new PixelRect(0, 0, 9, 6)));
                Assert.That(binding.IsDisposed, Is.True);
                Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
            });
        });
    }

    private static Material3D CreateMaterial(
        MaterialTextureDependency dependency,
        TextureSource texture)
    {
        if (dependency == MaterialTextureDependency.BasicDiffuseMap)
        {
            var material = new BasicMaterial();
            material.DiffuseMap.CurrentValue = texture;
            return material;
        }

        if (dependency == MaterialTextureDependency.TransparentColorMap)
        {
            var material = new TransparentMaterial();
            material.ColorMap.CurrentValue = texture;
            return material;
        }

        var pbrMaterial = new PBRMaterial();
        switch (dependency)
        {
            case MaterialTextureDependency.PbrAlbedoMap:
                pbrMaterial.AlbedoMap.CurrentValue = texture;
                break;
            case MaterialTextureDependency.PbrNormalMap:
                pbrMaterial.NormalMap.CurrentValue = texture;
                break;
            case MaterialTextureDependency.PbrMetallicRoughnessMap:
                pbrMaterial.MetallicRoughnessMap.CurrentValue = texture;
                break;
            case MaterialTextureDependency.PbrEmissiveMap:
                pbrMaterial.EmissiveMap.CurrentValue = texture;
                break;
            case MaterialTextureDependency.PbrAOMap:
                pbrMaterial.AOMap.CurrentValue = texture;
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
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: targetDomain,
            outputScale: outputScale,
            maxWorkingScale: maxWorkingScale,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
            return new RenderRequestCompiler().Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

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
