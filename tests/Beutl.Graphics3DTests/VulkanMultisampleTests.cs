using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using static Beutl.Graphics3DTests.Graphics3DTestHelpers;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class VulkanMultisampleTests
{
    private const int Size = 32;
    private const int SampleCount = 4;
    private const float TriangleDepth = 0.25f;
    private const TextureFormat ColorFormat = TextureFormat.RGBA16Float;
    private const TextureFormat DepthFormat = TextureFormat.Depth32Float;

    [TestCase(TextureFormat.RGBA16Float)]
    [TestCase(TextureFormat.RGBA8Unorm)]
    public void ShaderResolve_PreservesPartialCoverageAlongADiagonal(TextureFormat colorFormat)
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        var (singleSample, multisampled) = GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            IShaderCompiler compiler = context.CreateShaderCompiler();
            using IDisposable? compilerLifetime = compiler as IDisposable;
            return (
                RenderResolvedTriangle(context, compiler, colorFormat, 1),
                RenderResolvedTriangle(context, compiler, colorFormat, SampleCount));
        });

        float[] singleEdge = GetEdgeAlphas(singleSample);
        float[] multisampledEdge = GetEdgeAlphas(multisampled);
        Assert.That(singleEdge.Distinct(), Is.EquivalentTo(new[] { 0f, 1f }));
        Assert.That(multisampledEdge.Where(alpha => alpha is > 0f and < 1f).Distinct().Count(),
            Is.GreaterThan(2), "The diagonal must contain at least three distinct partial alpha values.");

        foreach (byte[] image in new[] { singleSample, multisampled })
        {
            Assert.That(ReadPixel(image, 8, 8), Is.EqualTo(Vector4.One), "inside the triangle");
            Assert.That(ReadPixel(image, 26, 22), Is.EqualTo(Vector4.Zero), "outside the triangle");
        }
    }

    [Test]
    public void LoadAndResume_PreserveColorAndDepthForShaderResolve()
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        var (beforeLoad, afterLoad, depthPixels) = GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            IShaderCompiler compiler = context.CreateShaderCompiler();
            using IDisposable? compilerLifetime = compiler as IDisposable;
            using ITexture2D color = context.CreateTexture2D(Size, Size, ColorFormat, SampleCount);
            using ITexture2D depth = context.CreateTexture2D(Size, Size, DepthFormat, SampleCount);
            DrawTriangle(context, compiler, color, depth, SampleCount);
            byte[] before = ResolveColor(context, compiler, color);

            // A buffer copy splits the LOAD pass, so the resumed instance must also load all samples.
            // The second triangle is transparent and behind the first, so every earlier sample must survive.
            using IBuffer source = context.CreateBuffer(4, BufferUsage.TransferSource,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
            using IBuffer destination = context.CreateBuffer(4, BufferUsage.TransferDestination,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
            source.Upload<byte>([1, 2, 3, 4]);
            DrawTriangle(context, compiler, color, depth, SampleCount, AttachmentLoadOp.Load, 0.75f,
                () => context.CopyBuffer(source, destination, 4));

            return (before, ResolveColor(context, compiler, color),
                Resolve(context, compiler, depth, ResolveDepthShader, depthOnly: true));
        });

        Assert.That(afterLoad, Is.EqualTo(beforeLoad), "Both LOAD instances must preserve the covered samples.");
        Assert.That(ReadPixel(afterLoad, 8, 8), Is.EqualTo(Vector4.One));
        Assert.That(ReadPixel(afterLoad, 26, 22), Is.EqualTo(Vector4.Zero));

        ReadOnlySpan<float> resolvedDepth = MemoryMarshal.Cast<byte, float>(depthPixels);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // Even if only one sample was covered, the depth resolve must keep that nearer sample.
                float expected = ReadPixel(afterLoad, x, y).W > 0f ? TriangleDepth : 1f;
                Assert.That(resolvedDepth[y * Size + x], Is.EqualTo(expected), $"depth at ({x}, {y})");
            }
        }
    }

    [Test]
    public void DeviceQuery_ReturnsACommonSupportedSampleCount()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            TextureFormat[] formats = [ColorFormat, TextureFormat.RGBA8Unorm, DepthFormat];
            Assert.That(context.GetSupportedSampleCount(1, formats), Is.EqualTo(1));
            Assert.That(context.GetSupportedSampleCount(4, formats), Is.EqualTo(SampleCount));
            Assert.That(context.GetSupportedSampleCount(4, []), Is.EqualTo(1));

            foreach (int requested in new[] { 2, 3, 5, 8, 16, 32, 64, int.MaxValue })
            {
                int supported = context.GetSupportedSampleCount(requested, formats);
                Assert.That(BitOperations.IsPow2(supported), Is.True, $"request {requested}");
                Assert.That(supported, Is.LessThanOrEqualTo(requested));
                int[] perFormat = formats.Select(format => context.GetSupportedSampleCount(supported, [format])).ToArray();
                Assert.That(perFormat, Is.All.EqualTo(supported), "Every format must support the returned count.");
            }

            using ITexture2D texture = context.CreateTexture2D(Size, Size, ColorFormat, SampleCount);
            using IRenderPass3D pass = context.CreateRenderPass3D([ColorFormat], DepthFormat,
                AttachmentLoadOp.Clear, AttachmentLoadOp.Clear, SampleCount);
            Assert.That(texture.SampleCount, Is.EqualTo(SampleCount));
            Assert.That(pass.SampleCount, Is.EqualTo(SampleCount));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Framebuffer_RejectsSampleCountMismatch(bool mismatchedDepth)
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IRenderPass3D pass = context.CreateRenderPass3D([ColorFormat], DepthFormat,
                AttachmentLoadOp.Clear, AttachmentLoadOp.Clear, SampleCount);
            using ITexture2D color = context.CreateTexture2D(Size, Size, ColorFormat, mismatchedDepth ? SampleCount : 1);
            using ITexture2D depth = context.CreateTexture2D(Size, Size, DepthFormat, mismatchedDepth ? 1 : SampleCount);
            var error = Assert.Throws<ArgumentException>(() =>
            {
                using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(pass, [color], depth);
            });
            Assert.That(error!.ParamName, Is.EqualTo(mismatchedDepth ? "depthTexture" : "colorTextures"));
            Assert.That(error.Message, Does.Contain("sample count"));
        });
    }

    [Test]
    public void UnsupportedOperations_RefuseMultisampledTexturesBeforeRecording()
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ITexture2D multisampled = context.CreateTexture2D(Size, Size, ColorFormat, SampleCount);
            using ITexture2D singleSample = context.CreateTexture2D(Size, Size, ColorFormat);
            using ITextureCube cube = context.CreateTextureCube(Size, ColorFormat);
            using ITextureArray array = context.CreateTextureArray(Size, Size, 1, ColorFormat);
            using ITextureCubeArray cubeArray = context.CreateTextureCubeArray(Size, 1, ColorFormat);
            IShaderCompiler compiler = context.CreateShaderCompiler();
            using IDisposable? compilerLifetime = compiler as IDisposable;
            using IRenderPass3D pass = context.CreateRenderPass3D([ColorFormat], null);
            using IPipeline3D pipeline = context.CreatePipeline3D(pass,
                compiler.CompileToSpirv(FullscreenVertexShader, ShaderStage.Vertex),
                compiler.CompileToSpirv(TriangleFragmentShader, ShaderStage.Fragment),
                [new DescriptorBinding(0, DescriptorType.StorageImage, 1, ShaderStage.Fragment)],
                VertexInputDescription.Empty, PipelineOptions.Fullscreen);
            using var descriptors = (VulkanDescriptorSet)context.CreateDescriptorSet(pipeline,
                [new DescriptorPoolSize(DescriptorType.StorageImage, 1)]);

            context.WaitIdle();
            var events = new List<VulkanCommandPoolEvent>();
            using (VulkanCommandPool.Observe(events.Add))
            {
                AssertRefused(() => context.CopyTexture(multisampled, singleSample));
                AssertRefused(() => context.CopyTexture(singleSample, multisampled));
                AssertRefused(() => context.CopyTextureToCubeFace(multisampled, cube, 0));
                AssertRefused(() => context.CopyTextureToArrayLayer(multisampled, array, 0));
                AssertRefused(() => context.CopyTextureToCubeArrayFace(multisampled, cubeArray, 0, 0));
                AssertRefused(() => multisampled.DownloadPixels());
                AssertRefused(() => multisampled.Upload(new byte[Size * Size * 8]));
                AssertRefused(() => multisampled.CreateSkiaSurface());
                AssertRefused(multisampled.PrepareForSkiaRendering);
                AssertRefused(() => multisampled.PrepareForSkiaSampling(requireCompletion: true));
                AssertRefused(() => descriptors.UpdateStorageImage(0, multisampled));
                AssertRefused(((ITransparentClearableTexture)multisampled).ClearToTransparent);
                context.WaitIdle();
            }

            Assert.That(events, Is.Empty, "None of the refusals may leave commands to submit.");
            Assert.That(multisampled.RequiresSkiaFlushForBackendInterop, Is.False);
        });
    }

    private static void AssertRefused(Action operation)
        => Assert.That(() => operation(), Throws.TypeOf<NotSupportedException>().With.Message.Contains("multisampled"));

    private static byte[] RenderResolvedTriangle(
        IGraphicsContext context, IShaderCompiler compiler, TextureFormat format, int sampleCount)
    {
        using ITexture2D color = context.CreateTexture2D(Size, Size, format, sampleCount);
        using ITexture2D depth = context.CreateTexture2D(Size, Size, DepthFormat, sampleCount);
        DrawTriangle(context, compiler, color, depth, sampleCount);
        return ResolveColor(context, compiler, color);
    }

    private static void DrawTriangle(
        IGraphicsContext context, IShaderCompiler compiler, ITexture2D color, ITexture2D depth, int sampleCount,
        AttachmentLoadOp loadOp = AttachmentLoadOp.Clear, float drawDepth = TriangleDepth, Action? transfer = null)
    {
        using IRenderPass3D pass = context.CreateRenderPass3D([color.Format], depth.Format, loadOp, loadOp, sampleCount);
        using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(pass, [color], depth);
        PipelineOptions options = PipelineOptions.Fullscreen;
        options.DepthTestEnabled = true;
        options.DepthWriteEnabled = true;
        options.DepthCompare = CompareFunction.Less;
        using IPipeline3D pipeline = context.CreatePipeline3D(pass,
            compiler.CompileToSpirv(TriangleVertexShader, ShaderStage.Vertex),
            compiler.CompileToSpirv(TriangleFragmentShader, ShaderStage.Fragment),
            [], VertexInputDescription.Empty, options);

        pass.Begin(framebuffer, [Colors.Transparent]);
        pass.BindPipeline(pipeline);
        pass.SetPushConstants(drawDepth);
        transfer?.Invoke();
        pass.Draw(3);
        pass.End();
    }

    private static byte[] ResolveColor(IGraphicsContext context, IShaderCompiler compiler, ITexture2D source)
        => Resolve(context, compiler, source,
            source.SampleCount == 1 ? SingleSampleColorShader : ResolveColorShader, depthOnly: false);

    private static byte[] Resolve(
        IGraphicsContext context, IShaderCompiler compiler, ITexture2D source, string fragmentShader, bool depthOnly)
    {
        using ITexture2D output = context.CreateTexture2D(Size, Size, depthOnly ? DepthFormat : ColorFormat, 1);
        using IRenderPass3D pass = context.CreateRenderPass3D(depthOnly ? [] : [ColorFormat], depthOnly ? DepthFormat : null);
        using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(pass, depthOnly ? [] : [output], depthOnly ? output : null);
        PipelineOptions options = PipelineOptions.Fullscreen;
        if (depthOnly)
        {
            options.DepthTestEnabled = true;
            options.DepthWriteEnabled = true;
            options.DepthCompare = CompareFunction.Always;
        }

        using IPipeline3D pipeline = context.CreatePipeline3D(pass,
            compiler.CompileToSpirv(FullscreenVertexShader, ShaderStage.Vertex),
            compiler.CompileToSpirv(fragmentShader, ShaderStage.Fragment),
            [new DescriptorBinding(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)],
            VertexInputDescription.Empty, options);
        using ISampler sampler = context.CreateSampler(SamplerFilter.Nearest, SamplerFilter.Nearest);
        using IDescriptorSet descriptors = context.CreateDescriptorSet(pipeline,
            [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1)]);
        source.PrepareForSampling();
        descriptors.UpdateTexture(0, source, sampler);

        pass.Begin(framebuffer, []);
        pass.BindPipeline(pipeline);
        pass.BindDescriptorSet(pipeline, descriptors);
        pass.Draw(3);
        pass.End();
        return output.DownloadPixels();
    }

    private static float[] GetEdgeAlphas(byte[] image)
    {
        var alphas = new List<float>();
        for (int y = 6; y < 22; y++)
        {
            // Three columns around the diagonal from (29, 4) to (3, 23).
            int edgeX = (int)(3 + (23 - (y + 0.5f)) * 26 / 19);
            for (int x = edgeX - 1; x <= edgeX + 1; x++)
                alphas.Add(ReadPixel(image, x, y).W);
        }

        return [.. alphas];
    }

    private static Vector4 ReadPixel(byte[] image, int x, int y)
    {
        ReadOnlySpan<Half> pixel = MemoryMarshal.Cast<byte, Half>(image.AsSpan((y * Size + x) * 8, 8));
        return new Vector4((float)pixel[0], (float)pixel[1], (float)pixel[2], (float)pixel[3]);
    }

    private const string TriangleVertexShader = """
        #version 450
        layout(push_constant) uniform Draw { float depth; } draw;
        void main() {
            vec2 vertices[3] = vec2[](vec2(3, 4), vec2(29, 4), vec2(3, 23));
            vec2 position = vertices[gl_VertexIndex] / 16.0 - 1.0;
            gl_Position = vec4(position, draw.depth, 1.0);
        }
        """;

    private const string TriangleFragmentShader = """
        #version 450
        layout(location = 0) out vec4 outColor;
        void main() {
            outColor = vec4(gl_FragCoord.z < 0.5 ? 1.0 : 0.0);
        }
        """;

    private const string SingleSampleColorShader = """
        #version 450
        layout(binding = 0) uniform sampler2D sourceTexture;
        layout(location = 0) out vec4 outColor;
        void main() {
            outColor = texelFetch(sourceTexture, ivec2(gl_FragCoord.xy), 0);
        }
        """;

    private const string ResolveColorShader = """
        #version 450
        layout(binding = 0) uniform sampler2DMS sourceTexture;
        layout(location = 0) out vec4 outColor;
        void main() {
            ivec2 pixel = ivec2(gl_FragCoord.xy);
            int count = textureSamples(sourceTexture);
            vec4 sum = vec4(0.0);
            for (int sampleIndex = 0; sampleIndex < count; sampleIndex++) {
                sum += texelFetch(sourceTexture, pixel, sampleIndex);
            }
            outColor = sum / float(count);
        }
        """;

    private const string ResolveDepthShader = """
        #version 450
        layout(binding = 0) uniform sampler2DMS sourceDepth;
        void main() {
            ivec2 pixel = ivec2(gl_FragCoord.xy);
            float nearestDepth = 1.0;
            for (int sampleIndex = 0; sampleIndex < textureSamples(sourceDepth); sampleIndex++) {
                nearestDepth = min(nearestDepth, texelFetch(sourceDepth, pixel, sampleIndex).r);
            }
            gl_FragDepth = nearestDepth;
        }
        """;
}
