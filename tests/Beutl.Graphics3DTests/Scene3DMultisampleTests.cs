using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Nodes;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class Scene3DMultisampleTests
{
    private const int Size = 128; // The legacy linear flip then samples exact texel centers.

    [Test]
    public void DefaultScene_ResolvesOnlyTheSilhouetteAndKeepsTheNearestDepth()
    {
        GpuTestEnvironment.EnsureAvailable();
        (Frame multisampled, Frame singleSample) = GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Scene3D scene = CreateCubeScene();
            CompositionContext context = CompositionContext.Default;
            using Scene3D.Resource resource = scene.ToResource(context);
            using var node = new Scene3DRenderNode(resource);
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(0, 0, Size, Size),
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = RenderCacheOptions.Disabled,
            });

            Assert.That(scene.AntiAliasing.Value, Is.EqualTo(AntiAliasingMode.Msaa4x));
            Frame multisampled = Capture(renderer, resource);
            Renderer3D? sceneRenderer = resource.Renderer;

            scene.AntiAliasing.Value = AntiAliasingMode.Off;
            bool versionBumped = false;
            resource.Reconcile(scene, context, ref versionBumped);
            Assert.That(node.Update(resource), Is.True);
            Frame singleSample = Capture(renderer, resource);
            Assert.That(resource.Renderer, Is.SameAs(sceneRenderer));
            return (multisampled, singleSample);
        });

        ReadOnlySpan<Half> off = MemoryMarshal.Cast<byte, Half>(singleSample.Color);
        ReadOnlySpan<Half> msaa = MemoryMarshal.Cast<byte, Half>(multisampled.Color);
        var offAlphas = new HashSet<float>();
        var partialAlphas = new HashSet<float>();
        for (int i = 3; i < off.Length; i += 4)
        {
            offAlphas.Add((float)off[i]);
            float alpha = (float)msaa[i];
            if (alpha is > 0f and < 1f)
                partialAlphas.Add(alpha);
        }

        Assert.That(offAlphas, Is.EquivalentTo(new[] { 0f, 1f }));
        Assert.That(partialAlphas, Has.Count.GreaterThan(2), "4x must produce at least three distinct partial alpha values.");
        Assert.That(multisampled.Samples, Is.EqualTo(4));
        Assert.That(singleSample.Samples, Is.EqualTo(1));

        // The cube is rolled about the view axis, so its visible face has constant depth.
        float faceDepth = singleSample.Depth[(Size / 2) * Size + Size / 2];
        Assert.That(faceDepth, Is.LessThan(1f));
        int newlyCovered = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int pixel = y * Size + x;
                if (!HasCoverageChange(off, x, y))
                {
                    if (!multisampled.Color.AsSpan(pixel * 8, 8).SequenceEqual(singleSample.Color.AsSpan(pixel * 8, 8)))
                        Assert.Fail($"Color changed away from the silhouette at ({x}, {y}). Off: "
                            + string.Join(", ", off.Slice(pixel * 4, 4).ToArray()) + ". MSAA: "
                            + string.Join(", ", msaa.Slice(pixel * 4, 4).ToArray()));
                    if (multisampled.Depth[pixel] != singleSample.Depth[pixel])
                        Assert.Fail($"Depth changed away from the silhouette at ({x}, {y}).");
                }

                float alpha = (float)msaa[pixel * 4 + 3];
                if (alpha is > 0f and < 1f)
                {
                    Assert.That(multisampled.Depth[pixel], Is.EqualTo(faceDepth), $"minimum depth at ({x}, {y})");
                    if (off[pixel * 4 + 3] == (Half)0)
                        newlyCovered++;
                }
            }
        }

        Assert.That(newlyCovered, Is.Positive, "Depth must pick up samples in pixels missed by the 1x center sample.");
    }

    [Test]
    public void CoplanarMaterials_ResolveTheSeamWhenPositionAndNormalMatch()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        var (off, msaa, coverage) = GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            IShaderCompiler compiler = context.CreateShaderCompiler();
            using IDisposable? compilerLifetime = compiler as IDisposable;
            CompositionContext composition = CompositionContext.Default;
            using var camera = (PerspectiveCamera.Resource)new PerspectiveCamera
            {
                Position = { Value = new Vector3(0, 0, 5) },
                Up = { Value = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6)) },
                FieldOfView = { Value = 45 },
            }.ToResource(composition);
            using var red = (Cube3D.Resource)CreateCube(-1, Colors.Red).ToResource(composition);
            using var blue = (Cube3D.Resource)CreateCube(1, Colors.Blue).ToResource(composition);

            var (singleSample, _) = Render(1);
            var (multisampled, redCoverage) = Render(4);
            return (singleSample, multisampled, redCoverage);

            (byte[] Color, byte[] RedCoverage) Render(int samples)
            {
                using var geometry = new GeometryPass(context, compiler, samples);
                geometry.Initialize(Size, Size);
                geometry.Execute(composition, camera, [red, blue], 1, [], Colors.White, 0.5f);
                geometry.PrepareForSampling();

                byte[] redCoverage = samples > 1 ? ReadCoplanarCoverage(context, compiler, geometry) : [];
                using var lighting = new LightingPass(context, compiler, geometry.DepthTexture!);
                lighting.Initialize(Size, Size);
                lighting.BindGBuffer(geometry);
                lighting.Execute(camera, [], Colors.Transparent, Colors.White, 0.5f);
                lighting.PrepareForSampling();

                using var flip = new FlipPass(context, compiler);
                flip.SetInputTexture(lighting.OutputTexture!);
                flip.Initialize(Size, Size);
                flip.Execute();
                return (flip.OutputTexture!.DownloadPixels(), redCoverage);
            }
        });

        var coverages = new HashSet<float>();
        var offColors = new HashSet<Vector4>();
        var msaaColors = new HashSet<Vector4>();
        ReadOnlySpan<Half> redFractions = MemoryMarshal.Cast<byte, Half>(coverage);
        for (int pixel = 0; pixel < Size * Size; pixel++)
        {
            float fraction = (float)redFractions[pixel * 4];
            if (fraction is > 0f and < 1f)
            {
                coverages.Add(fraction);
                offColors.Add(ReadColor(off, pixel));
                msaaColors.Add(ReadColor(msaa, pixel));
            }
        }

        Assert.That(coverages, Is.EquivalentTo(new[] { 0.25f, 0.5f, 0.75f }),
            "The G-buffer must contain mixed-albedo samples with equal positions and normals at the seam.");
        Assert.That(offColors, Is.EquivalentTo(new[] { new Vector4(0.5f, 0, 0, 1), new Vector4(0, 0, 0.5f, 1) }));
        Assert.That(msaaColors, Has.Count.GreaterThan(2), "4x must blend the two face colors at the coplanar seam.");
        Assert.That(msaaColors, Is.EquivalentTo(coverages.Select(f => new Vector4(f * 0.5f, 0, (1f - f) * 0.5f, 1))));
    }

    private static Cube3D CreateCube(float x, Color albedo) => new()
    {
        Position = { Value = new Vector3(x, 0, 0) },
        Width = { Value = 2 },
        Height = { Value = 2 },
        Depth = { Value = 2 },
        Material = { Value = new PBRMaterial { Albedo = { Value = albedo } } },
    };

    private static Vector4 ReadColor(byte[] image, int pixel)
    {
        ReadOnlySpan<Half> rgba = MemoryMarshal.Cast<byte, Half>(image.AsSpan(pixel * 8, 8));
        return new Vector4((float)rgba[0], (float)rgba[1], (float)rgba[2], (float)rgba[3]);
    }

    private static byte[] ReadCoplanarCoverage(IGraphicsContext context, IShaderCompiler compiler, GeometryPass geometry)
    {
        using ITexture2D target = context.CreateTexture2D(Size, Size, TextureFormat.RGBA16Float);
        using IRenderPass3D pass = context.CreateRenderPass3D([target.Format], null);
        using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(pass, [target], null);
        using IPipeline3D pipeline = context.CreatePipeline3D(pass,
            compiler.CompileToSpirv(Graphics3DTestHelpers.FullscreenVertexShader, ShaderStage.Vertex),
            compiler.CompileToSpirv(CoplanarCoverageShader, ShaderStage.Fragment),
            [new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment),
                new(1, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment),
                new(2, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)],
            VertexInputDescription.Empty, PipelineOptions.Fullscreen);
        using ISampler sampler = context.CreateSampler(SamplerFilter.Nearest, SamplerFilter.Nearest);
        using IDescriptorSet descriptors = context.CreateDescriptorSet(pipeline, [new(DescriptorType.CombinedImageSampler, 3)]);
        descriptors.UpdateTexture(0, geometry.PositionTexture!, sampler);
        descriptors.UpdateTexture(1, geometry.NormalMetallicTexture!, sampler);
        descriptors.UpdateTexture(2, geometry.AlbedoRoughnessTexture!, sampler);

        pass.Begin(framebuffer, [Colors.Transparent]);
        pass.BindPipeline(pipeline);
        pass.BindDescriptorSet(pipeline, descriptors);
        pass.Draw(3);
        pass.End();
        return target.DownloadPixels();
    }

    private const string CoplanarCoverageShader = """
        #version 450
        layout(binding = 0) uniform sampler2DMS positions;
        layout(binding = 1) uniform sampler2DMS normals;
        layout(binding = 2) uniform sampler2DMS albedos;
        layout(location = 0) out vec4 outColor;

        void main() {
            ivec2 pixel = ivec2(gl_FragCoord.xy);
            // Match the final image's Y orientation.
            pixel.y = textureSize(positions).y - 1 - pixel.y;
            vec4 position = texelFetch(positions, pixel, 0);
            if (position.a < 0.5 || position.z != 1.0) discard;
            vec4 normal = texelFetch(normals, pixel, 0);
            int count = textureSamples(positions);
            int redCount = 0;
            for (int i = 0; i < count; i++) {
                if (texelFetch(positions, pixel, i) != position || texelFetch(normals, pixel, i) != normal) discard;
                vec3 albedo = texelFetch(albedos, pixel, i).rgb;
                if (albedo == vec3(1.0, 0.0, 0.0)) redCount++;
                else if (albedo != vec3(0.0, 0.0, 1.0)) discard;
            }
            outColor = vec4(float(redCount) / float(count), 0.0, 0.0, 1.0);
        }
        """;

    [Test]
    public void Requested8x_UsesTheDeviceAnswerWithoutAnAllocationWarning()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            int supported = context.GetSupportedSampleCount(8,
                [.. GeometryPass.GBufferFormats, TextureFormat.RGBA16Float, TextureFormat.Depth32Float]);
            using IRenderer3D renderer = IRenderer3D.Create(context, surfaceDensity: 1, sampleCount: 8);
            renderer.Initialize(32, 24);
            Assert.That(renderer.SampleCount, Is.EqualTo(supported));
            Assert.That(((Renderer3D)renderer).LastSampleAllocationFailure, Is.Null);
        });
    }

    private static Scene3D CreateCubeScene()
    {
        var scene = new Scene3D
        {
            RenderWidth = { Value = Size },
            RenderHeight = { Value = Size },
            // Keep the face color independent of the driver's fragment interpolation position.
            AmbientIntensity = { Value = 0.5f },
            Camera =
            {
                Value = new PerspectiveCamera
                {
                    Position = { Value = new Vector3(0, 0, 5) },
                    Target = { Value = Vector3.Zero },
                    Up = { Value = Vector3.UnitY },
                    FieldOfView = { Value = 45 },
                }
            }
        };
        scene.Objects.Add(new Cube3D
        {
            Width = { Value = 2 },
            Height = { Value = 2 },
            Depth = { Value = 2 },
            Rotation = { Value = new Vector3(0, 0, 30) },
            Material = { Value = new PBRMaterial { Albedo = { Value = new Color(255, 110, 173, 223) } } },
        });
        return scene;
    }

    private static Frame Capture(RenderNodeRenderer renderer, Scene3D.Resource resource)
    {
        using var raster = renderer.Rasterize();
        Assert.That(resource.Renderer, Is.Not.Null);
        var depth = new float[Size * Size];
        Assert.That(resource.TryCopyDepth(depth, out PixelSize size), Is.True);
        Assert.That(size, Is.EqualTo(new PixelSize(Size, Size)));
        return new Frame(resource.Renderer!.DownloadPixels(), depth, resource.Renderer.SampleCount);
    }

    private static bool HasCoverageChange(ReadOnlySpan<Half> pixels, int x, int y)
    {
        Half alpha = pixels[(y * Size + x) * 4 + 3];
        for (int row = Math.Max(0, y - 1); row <= Math.Min(Size - 1, y + 1); row++)
        {
            for (int column = Math.Max(0, x - 1); column <= Math.Min(Size - 1, x + 1); column++)
            {
                if (pixels[(row * Size + column) * 4 + 3] != alpha)
                    return true;
            }
        }

        return false;
    }

    private sealed record Frame(byte[] Color, float[] Depth, int Samples);
}
