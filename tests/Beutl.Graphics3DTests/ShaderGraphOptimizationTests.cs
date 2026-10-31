using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using static Beutl.Graphics3DTests.ShaderGraphContractTests;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;
using ImageUsageFlags = Silk.NET.Vulkan.ImageUsageFlags;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphOptimizationTests
{
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void DeclaredRegionsPreserveCoordinatesAndReduceMultiPassWork(float scale)
    {
        OnDevice(_ =>
        {
            Rect bounds = new(4.25f, 6.5f, 64, 32);
            Rect selected = new(30, 18, 6, 4);
            ShaderGraphDescription graph = Chain(3, partial: true);
            var full = Execute(graph, bounds, scale);
            var partial = Execute(graph, bounds, scale, selected);
            PixelRect pixels = PixelRect.FromRect(bounds, scale);
            PixelRect region = PixelRect.FromRect(selected, scale);
            for (int y = region.Y; y < region.Bottom; y++)
                for (int x = region.X; x < region.Right; x++)
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int index = ((y - pixels.Y) * pixels.Width + x - pixels.X) * 4 + channel;
                        Assert.That(partial.Pixels[index], Is.EqualTo(full.Pixels[index]).Within(0.002));
                    }
            Assert.That(full.Pixels, Has.Some.GreaterThan(0.25f));
            Assert.That(partial.Statistics.ScheduledPixels, Is.LessThan(full.Statistics.ScheduledPixels));
            Assert.That(partial.Statistics.ExecutedPasses, Is.EqualTo(3));
            TestContext.Out.WriteLine($"Scale {scale}: scheduled pixels {full.Statistics.ScheduledPixels} -> {partial.Statistics.ScheduledPixels}.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RequestPlanningAndFallbackPreserveDeclaredInputFootprints(bool fallback)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = Chain(2, partial: true);
            Rect bounds = ShaderGraphRenderingTests.SourceBounds;
            Rect selected = new(bounds.X + 3, bounds.Y + 2, 4, 3);
            float[] full = ShaderGraphRenderingTests.Render(graph, bounds, nestedGraph: fallback);
            float[] partial = ShaderGraphRenderingTests.Render(graph, bounds, requestedRegion: selected, nestedGraph: fallback);
            PixelRect region = PixelRect.FromRect(selected.Translate(-bounds.Position), 1);
            for (int y = region.Y; y < region.Bottom; y++)
                for (int x = region.X; x < region.Right; x++)
                {
                    int offset = (y * (int)bounds.Width + x) * 4;
                    Assert.That(partial.Skip(offset).Take(4), Is.EqualTo(full.Skip(offset).Take(4)).Within(0.002));
                }
        });
    }

    [Test]
    public void UndeclaredSamplingKeepsFullExecution()
    {
        OnDevice(_ =>
        {
            Rect bounds = new(0, 0, 64, 32);
            var result = Execute(Chain(3, partial: false), bounds, region: new(20, 10, 2, 2));
            Assert.That(result.Statistics.ScheduledPixels, Is.EqualTo(64 * 32 * 3));
        });
    }

    [Test]
    public void MultipassImagesAndGpuInitializedBuffersReuseRetiredAllocations()
    {
        OnDevice(_ =>
        {
            Rect bounds = new(0, 0, 64, 32);
            var shortChain = Execute(Chain(3, partial: true), bounds);
            var longChain = Execute(Chain(20, partial: true), bounds);
            Assert.That(longChain.Statistics.ImageAllocations, Is.EqualTo(2));
            Assert.That(longChain.Statistics.ResourceReuses, Is.EqualTo(18));
            Assert.That(longChain.Statistics.PeakResourceBytes, Is.EqualTo(shortChain.Statistics.PeakResourceBytes));
            var shortHistory = Execute(History(1), bounds);
            var longHistory = Execute(History(10), bounds);
            Assert.That(longHistory.Pixels[0], Is.EqualTo(10).Within(0.01));
            Assert.That(longHistory.Statistics.BufferAllocations, Is.EqualTo(2));
            Assert.That(longHistory.Statistics.PeakResourceBytes, Is.EqualTo(shortHistory.Statistics.PeakResourceBytes));
            Assert.That(longHistory.Statistics.ResourceReuses, Is.EqualTo(19));
            TestContext.Out.WriteLine($"20 image passes: {longChain.Statistics.ImageAllocations} allocations, {longChain.Statistics.PeakResourceBytes} peak bytes. "
                + $"20 history updates: {longHistory.Statistics.BufferAllocations} buffer allocations, {longHistory.Statistics.PeakResourceBytes} peak bytes.");
        });
    }

    [Test]
    public void SelectedSecondaryAttachmentDeterminesThePixelScissor()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage first = builder.Image2D("first", new Rect(0, 0, 64, 32));
                ShaderImage second = builder.Image2D("second", new Rect(0, 0, 64, 32));
                builder.Fullscreen("mrt", ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(location = 0) out vec4 first;
                    layout(location = 1) out vec4 second;
                    void main() { first = vec4(1); second = vec4(gl_FragCoord.xy / 64, 0, 1); }
                    """)), pass =>
                {
                    pass.PartialRendering();
                    pass.ColorAttachment(0, first);
                    pass.ColorAttachment(1, second);
                });
                return builder.Output(second);
            });
            var full = Execute(graph, new(0, 0, 4, 2));
            var partial = Execute(graph, new(0, 0, 4, 2), region: new(10, 10, 4, 4));
            for (int y = 10; y < 14; y++)
                for (int x = 10; x < 14; x++)
                {
                    int offset = (y * 64 + x) * 4;
                    Assert.That(partial.Pixels.Skip(offset).Take(4), Is.EqualTo(full.Pixels.Skip(offset).Take(4)));
                }
            Assert.That(partial.Statistics.ScheduledPixels, Is.LessThan(full.Statistics.ScheduledPixels));
        });
    }

    [Test]
    public void MetadataOnlyImagesRetainTheirCoordinatesWithoutReducingDensity()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage metadata = builder.Image2D("metadata", new Rect(0, 0, 32768, 32768));
                ShaderImage output = builder.Image2D("output", builder.Source);
                builder.Fullscreen("read-metadata", ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(binding = 0, std140) uniform Frame { vec4 logical; vec4 raster; vec4 size; } frame;
                    layout(location = 0) out vec4 color;
                    void main() { color = vec4(frame.size.xy / 32768, 1, 1); }
                    """)), pass =>
                {
                    pass.ImageParameters(0, metadata);
                    pass.ColorAttachment(0, output);
                });
                return builder.Output(output);
            });
            Rect bounds = new(0, 0, 4, 2);
            Assert.That(graph.ClampWorkingScale(bounds, 1, default), Is.EqualTo(1));
            var result = Execute(graph, bounds);
            Assert.That(result.Pixels, Has.All.EqualTo(1));
            Assert.That(result.Statistics.ImageAllocations, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WorkingScaleReservesOnlyTheSourceCopyTheExecutorNeeds(bool sourceSupportsStorage)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage image = builder.Image2D("read", builder.Source);
                builder.Fullscreen("read-source", ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(binding = 0, rgba16f) readonly uniform image2D source;
                    layout(location = 0) out vec4 color;
                    void main() { color = imageLoad(source, ivec2(0)); }
                    """)), pass =>
                {
                    pass.StorageImage(0, builder.Source, ResourceAccess.Read);
                    pass.ColorAttachment(0, image);
                });
                return builder.Output(Sample(builder, image));
            });
            Rect bounds = new(0, 0, 4096, 4096);
            PixelSize sourceSize = new(4096, 4096);
            ImageUsageFlags usage = VulkanTexture2D.ColorTextureUsage | (sourceSupportsStorage ? ImageUsageFlags.StorageBit : 0);
            float density = graph.ClampWorkingScale(bounds, 1, default, sourceSize: sourceSize, sourceUsage: usage);
            Assert.That(density, sourceSupportsStorage ? Is.InRange(0.96f, 0.98f) : Is.InRange(0.65f, 0.67f));
            Assert.That(graph.ClampWorkingScale(bounds, density, default, sourceSize: sourceSize, sourceUsage: usage),
                Is.EqualTo(density), "The native input copy must not shrink when the output density changes.");
            var context = GpuTestEnvironment.ResolveVulkan(GraphicsContextFactory.SharedContext!);
            PixelRect pixels = PixelRect.FromRect(bounds, density);
            using ITexture2D source = new VulkanTexture2D(context, sourceSize.Width, sourceSize.Height, TextureFormat.RGBA16Float, usage);
            using ITexture2D output = context.CreateTexture2D(pixels.Width, pixels.Height, TextureFormat.RGBA16Float);
            using (IRenderPass3D clear = context.CreateRenderPass3D([TextureFormat.RGBA16Float], null))
            using (IFramebuffer3D framebuffer = context.CreateFramebuffer3D(clear, [source], null))
            {
                clear.Begin(framebuffer, [Colors.White]);
                clear.End();
            }
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            ShaderGraphExecutionStatistics statistics = ShaderGraphExecutor.Execute(graph, source,
                new(bounds, bounds, sourceSize, 1), output, density, default,
                (device, pass, identity) => ShaderGraphPipeline.Acquire(cache, new(context, context, "test", "test"), device, pass, identity));
            Assert.That(statistics.PeakResourceBytes, Is.LessThanOrEqualTo(ShaderGraphExecutor.MaximumTransientBytes));
            Assert.That(statistics.ImageAllocations, Is.EqualTo(sourceSupportsStorage ? 2 : 3));
            ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(output.DownloadPixels());
            Assert.That((float)values[0], Is.EqualTo(1));
            Assert.That((float)values[^1], Is.EqualTo(1));
        });
    }

    [Test]
    public void WorkingScaleUsesConcurrentLifetimesInsteadOfSummingRetiredImages()
    {
        ShaderGraphDescription graph = Chain(20, partial: true);
        Rect bounds = new(0, 0, 3840, 2160);
        Assert.That(graph.ClampWorkingScale(bounds, 1, default), Is.EqualTo(1));
        float reduced = graph.ClampWorkingScale(bounds, 2, new(0.25f, -0.75f));
        Assert.That(reduced, Is.InRange(1f, 2f));
        Assert.That(graph.ClampWorkingScale(bounds, reduced, new(0.25f, -0.75f)), Is.EqualTo(reduced));
        Assert.That(graph.ClampWorkingScale(bounds, 0.5f, default), Is.EqualTo(0.5f));
    }

    [Test]
    public void BudgetFailureAfterRecordedWorkDoesNotPoisonTheNextExecution()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage retired = builder.Image2D("retired", builder.Source);
                ShaderImage output = builder.Image2D("output", builder.Source);
                builder.Clear("initialize", retired, ImageClearValue.Float(Vector4.One));
                builder.Copy("copy", retired, output);
                ShaderBuffer tooLarge = builder.Buffer<uint>("too-large", (int)(ShaderGraphExecutor.MaximumTransientBytes / 4 + 1));
                builder.Clear("over-budget", tooLarge);
                return builder.Output(output);
            });
            Assert.That(graph.ClampWorkingScale(new(0, 0, 4, 2), 1, default), Is.EqualTo(1),
                "Fixed-size buffers cannot be made smaller by lowering raster density.");
            Assert.That(() => Execute(graph, new(0, 0, 4, 2)),
                Throws.InvalidOperationException.With.Message.Contains("active allocation budget"));
            Assert.That(Execute(ShaderGraphTests.ColorGraph(Vector4.One), new(0, 0, 4, 2)).Pixels, Has.All.EqualTo(1));
        });
    }

    [Test]
    public void HostInitializedBuffersDoNotOverwriteEarlierQueuedReads()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage image = builder.Image2D("sum", builder.Source);
                builder.Repeat(8, image, (graph, output, index) =>
                {
                    ShaderBuffer data = graph.Buffer<Vector4>($"data-{index}", new[] { new Vector4((index + 1) / 64f, 0, 0, 0.125f) });
                    graph.Fullscreen($"add-{index}", Display, pass =>
                    {
                        pass.StorageBuffer(0, data, ResourceAccess.Read);
                        pass.ColorAttachment(0, output, index == 0 ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load);
                        pass.Blend(new(true, BlendFactor.One, BlendFactor.One, BlendOp.Add,
                            BlendFactor.One, BlendFactor.One, BlendOp.Add));
                    });
                    return output;
                });
                return builder.Output(image);
            });
            var result = Execute(graph, new(0, 0, 4, 2));
            Assert.That(result.Pixels[0], Is.EqualTo(36 / 64f));
            Assert.That(result.Pixels[3], Is.EqualTo(1));
            Assert.That(result.Statistics.BufferAllocations, Is.EqualTo(8));
        });
    }

    [Test]
    public void RegionValuesInvalidateResultsWithoutRecompilingPipelines()
    {
        ShaderGraphDescription first = Chain(1, partial: true, offset: 1);
        ShaderGraphDescription same = Chain(1, partial: true, offset: 1);
        ShaderGraphDescription moved = Chain(1, partial: true, offset: 2);
        Assert.That(first.OutputIdentity, Is.EqualTo(same.OutputIdentity));
        Assert.That(first.OutputIdentity, Is.Not.EqualTo(moved.OutputIdentity));
        Assert.That(first.StructuralIdentity, Is.EqualTo(moved.StructuralIdentity));
        Assert.That(ShaderGraphDescription.GetPipelineIdentity(first.Passes[0]),
            Is.EqualTo(ShaderGraphDescription.GetPipelineIdentity(moved.Passes[0])));
    }

    internal static ShaderGraphDescription Chain(int count, bool partial, float offset = 0)
        => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            ShaderImage result = builder.Repeat(count, builder.Source, (graph, input, index) =>
            {
                ShaderImage output = graph.Image2D($"image-{index}", graph.Source);
                graph.Fullscreen($"pass-{index}", s_neighborhood, pass =>
                {
                    pass.SampledImage(0, input, SamplerState.LinearClamp);
                    pass.ImageParameters(1, input);
                    pass.ImageParameters(2, output);
                    pass.PushConstants(new Vector4(offset, 0, 0, 0));
                    if (partial) pass.InputRegion(0, new(Matrix.CreateTranslation(offset, 0), 1));
                    pass.ColorAttachment(0, output);
                });
                return output;
            });
            return builder.Output(result);
        });

    private static readonly ShaderProgram s_neighborhood = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform InputInfo { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform OutputInfo { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Offset { vec4 value; } offset;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 p = dst.raster.xy + gl_FragCoord.xy / dst.size.z + offset.value.xy;
            vec2 uv = (p - src.raster.xy) / src.raster.zw;
            color = (texture(image, uv - vec2(1, 0) / src.raster.zw)
                + texture(image, uv + vec2(1, 0) / src.raster.zw)) * 0.5;
        }
        """));
}
