using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using static Beutl.Graphics3DTests.ShaderGraphRenderingTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphStorageImageTests
{
    private static ShaderProgram Writer(string format) => ShaderProgram.Compute(new ShaderSource($$"""
        #version 450
        layout(local_size_x = 8, local_size_y = 8) in;
        layout(binding = 0, {{format}}) uniform image2D target;
        layout(push_constant) uniform Values { vec4 color; } values;
        void main() {
            ivec2 p = ivec2(gl_GlobalInvocationID.xy);
            if (any(greaterThanEqual(p, imageSize(target)))) return;
            imageStore(target, p, values.color);
        }
        """));

    private static ShaderImage Generate(ShaderGraphBuilder graph, string format = "rgba16f",
        TextureFormat textureFormat = TextureFormat.RGBA16Float, float value = 0.25f)
    {
        ShaderImage image = graph.Image2D("generated", graph.Source, textureFormat);
        graph.Compute("generate", Writer(format), pass =>
        {
            pass.StorageImage(0, image, ResourceAccess.Write);
            pass.PushConstants(new Vector4(value, 0, 0, 1));
            pass.DispatchThreads(64, 64);
        });
        return image;
    }

    [TestCase(ResourceAccess.Read)]
    [TestCase(ResourceAccess.ReadWrite)]
    public void StorageReadsNeedInitializationAndSourceCannotBeWritten(ResourceAccess access)
    {
        Assert.That(() => ShaderGraphBuilder.Build(0, (graph, _) =>
        {
            ShaderImage image = graph.Image2D("unwritten", graph.Source);
            graph.Compute("read", Writer("rgba16f"), pass =>
            {
                pass.StorageImage(0, image, access);
                pass.Dispatch(1);
            });
            return graph.Output(graph.Source);
        }), Throws.InvalidOperationException.With.Message.Contains("uninitialized"));
        Assert.That(() => ShaderGraphBuilder.Build(0, (graph, _) =>
        {
            graph.Compute("write-source", Writer("rgba16f"), pass =>
                pass.StorageImage(0, graph.Source, access | ResourceAccess.Write));
            return graph.Output(graph.Source);
        }), Throws.ArgumentException.With.Message.Contains("source image is read-only"));
    }

    [TestCase("rgba32f", TextureFormat.RGBA32Float)]
    [TestCase("rgba16f", TextureFormat.RGBA16Float)]
    [TestCase("rgba8", TextureFormat.RGBA8Unorm)]
    [TestCase("r32f", TextureFormat.R32Float)]
    [TestCase("r16f", TextureFormat.R16Float)]
    [TestCase("r8", TextureFormat.R8Unorm)]
    public void StorageFormatsAreMatchedToTheViewNotTheTexelSize(string format, TextureFormat textureFormat)
    {
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0,
            (builder, _) => builder.Output(Generate(builder, format, textureFormat)));
        ShaderPassDescription pass = graph.Passes[0];
        using var compiler = new VulkanShaderCompiler();
        var shader = new ShaderInterface(compiler.CompileToSpirv(
            pass.Program!.Stages[ShaderStage.Compute].Text, ShaderStage.Compute), ShaderStage.Compute);
        Assert.That(() => shader.Validate(pass, ShaderStage.Compute), Throws.Nothing);
        var wrongImage = new ShaderImage(graph, -1, "wrong-format", TextureFormat.BGRA8Unorm, null, null, 0, 1);
        Assert.That(() => shader.Validate(pass with
        {
            Bindings = [pass.Bindings[0] with { Resource = wrongImage }],
        }, ShaderStage.Compute), Throws.ArgumentException.With.Message.Contains("shader image format"));
    }

    [TestCase("readonly", ResourceAccess.Read, true)]
    [TestCase("readonly", ResourceAccess.Write, false)]
    [TestCase("readonly", ResourceAccess.ReadWrite, false)]
    [TestCase("writeonly", ResourceAccess.Read, false)]
    [TestCase("writeonly", ResourceAccess.Write, true)]
    [TestCase("", ResourceAccess.Write, true)]
    [TestCase("", ResourceAccess.ReadWrite, true)]
    public void StorageAccessAgreesWithShaderQualifiers(string qualifier, ResourceAccess access, bool valid)
    {
        var program = ShaderProgram.Compute(new ShaderSource($$"""
            #version 450
            layout(local_size_x = 1) in;
            layout(binding = 0, rgba16f) {{qualifier}} uniform image2D image;
            layout(binding = 1, std430) buffer Result { vec4 value; } result;
            void main() {
                {{(qualifier == "readonly" ? "result.value = imageLoad(image, ivec2(0));" : "imageStore(image, ivec2(0), vec4(1)); result.value = vec4(1);")}}
            }
            """));
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            ShaderImage image = Generate(builder);
            ShaderBuffer result = builder.Buffer<Vector4>("result", 1);
            builder.Compute("access", program, pass =>
            {
                pass.StorageImage(0, image, access);
                pass.StorageBuffer(1, result, ResourceAccess.Write);
                pass.Dispatch(1);
            });
            return builder.Output(image);
        });
        using var compiler = new VulkanShaderCompiler();
        var shader = new ShaderInterface(compiler.CompileToSpirv(
            program.Stages[ShaderStage.Compute].Text, ShaderStage.Compute), ShaderStage.Compute);
        Assert.That(() => shader.Validate(graph.Passes[1], ShaderStage.Compute),
            valid ? Throws.Nothing : Throws.ArgumentException);
    }

    [Test]
    public void ComputeAndGraphicsShareImagesAcrossLayoutsAndReusePrograms()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            static ShaderGraphDescription Build(float value) => ShaderGraphBuilder.Build(value, (graph, amount) =>
            {
                ShaderImage image = Generate(graph, value: amount);
                var modify = ShaderProgram.Compute(new ShaderSource("""
                    #version 450
                    layout(local_size_x = 8, local_size_y = 8) in;
                    layout(binding = 0, rgba16f) uniform image2D image;
                    void main() {
                        ivec2 p = ivec2(gl_GlobalInvocationID.xy);
                        if (any(greaterThanEqual(p, imageSize(image)))) return;
                        imageStore(image, p, imageLoad(image, p) * vec4(2, 1, 1, 1));
                    }
                    """));
                graph.Compute("modify", modify, pass =>
                {
                    pass.StorageImage(0, image, ResourceAccess.ReadWrite);
                    pass.DispatchThreads(64, 64);
                });
                ShaderImage raster = graph.Image2D("raster", graph.Source);
                var sample = ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(binding = 0, rgba16f) readonly uniform image2D image;
                    layout(binding = 1) uniform sampler2D sampled;
                    layout(location = 0) out vec4 color;
                    void main() {
                        ivec2 p = ivec2(gl_FragCoord.xy);
                        color = (imageLoad(image, p) + texelFetch(sampled, p, 0)) * 0.5;
                    }
                    """));
                graph.Fullscreen("sample", sample, pass =>
                {
                    pass.StorageImage(0, image, ResourceAccess.Read);
                    pass.SampledImage(1, image);
                    pass.ColorAttachment(0, raster);
                });
                ShaderImage result = graph.Image2D("result", graph.Source);
                var copy = ShaderProgram.Compute(new ShaderSource("""
                    #version 450
                    layout(local_size_x = 8, local_size_y = 8) in;
                    layout(binding = 0, rgba16f) readonly uniform image2D inputImage;
                    layout(binding = 1, rgba16f) writeonly uniform image2D outputImage;
                    void main() {
                        ivec2 p = ivec2(gl_GlobalInvocationID.xy);
                        if (any(greaterThanEqual(p, imageSize(outputImage)))) return;
                        imageStore(outputImage, p, imageLoad(inputImage, p));
                    }
                    """));
                graph.Compute("copy", copy, pass =>
                {
                    pass.StorageImage(0, raster, ResourceAccess.Read);
                    pass.StorageImage(1, result, ResourceAccess.Write);
                    pass.DispatchThreads(64, 64);
                });
                return graph.Output(result);
            });
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            float[] first = Render(Build(0.125f), SourceBounds, cache: cache, sharedPool: pool);
            long creations = cache.Statistics.Creations;
            float[] second = Render(Build(0.375f), SourceBounds, scale: 2, cache: cache, sharedPool: pool);
            Assert.Multiple(() =>
            {
                Assert.That(first.Where((_, i) => i % 4 == 0), Has.All.EqualTo(0.25f).Within(0.002));
                Assert.That(second.Where((_, i) => i % 4 == 0), Has.All.EqualTo(0.75f).Within(0.002));
                Assert.That(second.Where((_, i) => i % 4 == 3), Has.All.EqualTo(1));
                Assert.That(cache.Statistics.Creations, Is.EqualTo(creations));
            });
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void StorageReadsOfRendererInputPreserveCoordinatesAndPartialOutput(float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage output = builder.Image2D("output", builder.Source);
                var program = ShaderProgram.Compute(new ShaderSource("""
                    #version 450
                    layout(local_size_x = 8, local_size_y = 8) in;
                    layout(binding = 0, rgba16f) readonly uniform image2D source;
                    layout(binding = 1, rgba16f) writeonly uniform image2D outputImage;
                    layout(binding = 2, std140) uniform SourceInfo { vec4 logical; vec4 raster; vec4 size; } src;
                    layout(binding = 3, std140) uniform OutputInfo { vec4 logical; vec4 raster; vec4 size; } dst;
                    void main() {
                        ivec2 p = ivec2(gl_GlobalInvocationID.xy);
                        if (any(greaterThanEqual(p, imageSize(outputImage)))) return;
                        vec2 position = dst.raster.xy + (vec2(p) + 0.5) / dst.size.z;
                        ivec2 inputPixel = ivec2(floor((position - src.raster.xy) * src.size.z));
                        imageStore(outputImage, p, imageLoad(source, inputPixel));
                    }
                    """));
                builder.Compute("read-source", program, pass =>
                {
                    pass.StorageImage(0, builder.Source, ResourceAccess.Read);
                    pass.StorageImage(1, output, ResourceAccess.Write);
                    pass.ImageParameters(2, builder.Source);
                    pass.ImageParameters(3, output);
                    pass.DispatchThreads(64, 64);
                });
                return builder.Output(output);
            });
            var identity = ShaderGraphBuilder.Build(0, static (builder, _) => builder.Output(builder.Source));
            var region = new Rect(6, 8, 4, 4);
            Assert.That(Render(graph, SourceBounds, scale), Is.EqualTo(Render(identity, SourceBounds, scale)).Within(0.002));
            Assert.That(Render(graph, SourceBounds, scale, region),
                Is.EqualTo(Render(identity, SourceBounds, scale, region)).Within(0.002));
        });
    }

    [TestCase("r32f", TextureFormat.R32Float)]
    [TestCase("r16f", TextureFormat.R16Float)]
    [TestCase("r8", TextureFormat.R8Unorm)]
    public void ScalarStorageImagesUseDeviceFormatSupport(string format, TextureFormat textureFormat)
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            context.Vk.GetPhysicalDeviceFormatProperties(context.PhysicalDevice, textureFormat.ToVulkanFormat(), out var properties);
            var graph = ShaderGraphBuilder.Build(0, (builder, _) => builder.Output(Generate(builder, format, textureFormat)));
            if ((properties.OptimalTilingFeatures & Silk.NET.Vulkan.FormatFeatureFlags.StorageImageBit) == 0)
                Assert.That(() => Render(graph, SourceBounds), Throws.Exception.With.Message.Contains("StorageImage"));
            else
            {
                float[] pixels = Render(graph, SourceBounds);
                Assert.That(pixels.Take(4), Is.EqualTo(new[] { 0.25f, 0, 0, 1 }).Within(0.002));
            }
        });
    }

    [TestCase(ShaderStage.Vertex)]
    [TestCase(ShaderStage.Fragment)]
    public void GraphicsStoresUseTheEnabledStageFeature(ShaderStage writer)
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Rect bounds = new(SourceBounds.X, SourceBounds.Y, 1, 1);
            var graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage output = builder.Image2D("stored", bounds);
                ShaderImage attachment = builder.Image2D("attachment", bounds);
                var program = ShaderProgram.Graphics(new ShaderSource($$"""
                    #version 450
                    layout(binding = 0, rgba16f) {{(writer == ShaderStage.Vertex ? "writeonly" : "readonly")}} uniform image2D image;
                    void main() {
                        vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                        gl_Position = vec4(p * 2 - 1, float(imageSize(image).x - 1), 1);
                        {{(writer == ShaderStage.Vertex ? "if (gl_VertexIndex == 0) imageStore(image, ivec2(0), vec4(0.25, 0, 0, 1));" : "")}}
                    }
                    """), new ShaderSource($$"""
                    #version 450
                    layout(binding = 0, rgba16f) {{(writer == ShaderStage.Fragment ? "writeonly" : "readonly")}} uniform image2D image;
                    layout(location = 0) out vec4 color;
                    void main() {
                        color = vec4(imageSize(image), 0, 1);
                        {{(writer == ShaderStage.Fragment ? "imageStore(image, ivec2(gl_FragCoord.xy), vec4(0.25, 0, 0, 1));" : "")}}
                    }
                    """));
                builder.Render("store", program, pass =>
                {
                    pass.StorageImage(0, output, ResourceAccess.Write);
                    pass.ColorAttachment(0, attachment);
                    pass.Draw(3);
                });
                return builder.Output(output);
            });
            bool supported = writer == ShaderStage.Vertex
                ? context.EnabledFeatures.VertexPipelineStoresAndAtomics : context.EnabledFeatures.FragmentStoresAndAtomics;
            if (supported)
                Assert.That(Render(graph, bounds), Is.EqualTo(new[] { 0.25f, 0, 0, 1 }).Within(0.002));
            else
                Assert.That(() => Render(graph, bounds), Throws.Exception.With.Message.Contains($"storage writes in the {writer} stage"));
        });
    }

    [Test]
    public void CachedProgramsRevalidateImageFormatsBeforeExecutionAndRecover()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            var valid = ShaderGraphBuilder.Build(0,
                (builder, _) => builder.Output(Generate(builder, "r32f", TextureFormat.R32Float)));
            var invalid = ShaderGraphBuilder.Build(0,
                (builder, _) => builder.Output(Generate(builder, "r32f", TextureFormat.R16Float)));
            float[] expected = Render(valid, SourceBounds, cache: cache, sharedPool: pool);
            long creations = cache.Statistics.Creations;
            Assert.That(() => Render(invalid, SourceBounds, cache: cache, sharedPool: pool),
                Throws.Exception.With.Message.Contains("shader image format"));
            Assert.That(Render(valid, SourceBounds, cache: cache, sharedPool: pool), Is.EqualTo(expected));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(creations));
        });
    }

    [Test]
    public void RetiredAttachmentsWithoutStorageUsageAreNotReusedForStorageWrites()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage previous = builder.Image2D("previous", builder.Source);
                builder.Fullscreen("previous", ShaderGraphTests.ColorProgram, pass =>
                {
                    pass.PushConstants(Vector4.One);
                    pass.ColorAttachment(0, previous);
                });
                return builder.Output(Generate(builder));
            });
            Assert.That(Render(graph, SourceBounds).Where((_, i) => i % 4 == 0),
                Has.All.EqualTo(0.25f).Within(0.002));
        });
    }
}
