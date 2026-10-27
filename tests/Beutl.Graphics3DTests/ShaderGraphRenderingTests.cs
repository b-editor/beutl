using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphRenderingTests
{
    private static readonly Rect s_bounds = new(4, 6, 12, 8);
    internal static Rect SourceBounds => s_bounds;

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void NormalizedSourceCoordinatesExcludeTheRasterApron(float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription identity = ShaderGraphBuilder.Build(0,
                static (builder, _) => builder.Output(builder.Source));
            float[] pixels = Render(identity, s_bounds, scale);
            Assert.That(pixels.Where((_, index) => index % 4 == 3), Has.All.EqualTo(1f));
        });
    }

    [Test]
    public void ComputeBufferFeedsVertexStageAndRequestValuesDoNotRecompile()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            float[] first = Render(ComputeGraph(0.25f), s_bounds, cache: cache, sharedPool: pool);
            long creations = cache.Statistics.Creations;
            float[] second = Render(ComputeGraph(0.75f), s_bounds, cache: cache, sharedPool: pool);
            Assert.Multiple(() =>
            {
                Assert.That(first[0], Is.EqualTo(0.25f).Within(0.002));
                Assert.That(second[0], Is.EqualTo(0.75f).Within(0.002));
                Assert.That(first[3], Is.EqualTo(1));
                Assert.That(second[3], Is.EqualTo(1));
                Assert.That(cache.Statistics.Creations, Is.EqualTo(creations));
                Assert.That(cache.Statistics.Hits, Is.GreaterThanOrEqualTo(3));
            });
        });
    }

    [Test]
    public void MultipleAttachmentsWithDifferentFormatsFeedACompositePass()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage bright = builder.Image2D("bright", builder.Source);
                ShaderImage mask = builder.Image2D("mask", builder.Source, TextureFormat.R16Float);
                ShaderImage processedBright = builder.Image2D("processed-bright", builder.Source);
                ShaderImage processedMask = builder.Image2D("processed-mask", builder.Source, TextureFormat.R16Float);
                ShaderImage result = builder.Image2D("result", builder.Source);
                var extract = ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(location = 0) out vec4 bright;
                    layout(location = 1) out float mask;
                    void main() { bright = vec4(0.5, 0.25, 0.125, 1); mask = 0.5; }
                    """));
                builder.Fullscreen("extract", extract, pass =>
                {
                    pass.ColorAttachment(0, bright);
                    pass.ColorAttachment(1, mask);
                });
                var process = ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(location = 0) in vec2 uv;
                    layout(location = 0) out vec4 color;
                    layout(binding = 0) uniform sampler2D image;
                    layout(push_constant) uniform Values { float factor; } parameters;
                    void main() { color = texture(image, uv) * parameters.factor; }
                    """));
                builder.Fullscreen("process-bright", process, pass =>
                {
                    pass.SampledImage(0, bright);
                    pass.PushConstants(2f);
                    pass.ColorAttachment(0, processedBright);
                });
                builder.Fullscreen("process-mask", process, pass =>
                {
                    pass.SampledImage(0, mask);
                    pass.PushConstants(0.5f);
                    pass.ColorAttachment(0, processedMask);
                });
                var composite = ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(location = 0) in vec2 uv;
                    layout(location = 0) out vec4 color;
                    layout(binding = 0) uniform sampler2D bright;
                    layout(binding = 1) uniform sampler2D mask;
                    void main() { color = texture(bright, uv) * texture(mask, uv).r; }
                    """));
                builder.Fullscreen("composite", composite, pass =>
                {
                    pass.SampledImage(0, processedBright);
                    pass.SampledImage(1, processedMask);
                    pass.ColorAttachment(0, result);
                });
                return builder.Output(result);
            });
            float[] actual = Render(graph, s_bounds);
            Assert.That(actual.Take(4), Is.EqualTo(new[] { 0.25f, 0.125f, 0.0625f, 0.5f }).Within(0.002));
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void ExpandedBoundsAndPartialRenderingUseTheSameImageCoordinates(float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage result = builder.Image2D("expanded", builder.Source, padding: 2);
                var program = ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(binding = 0) uniform sampler2D image;
                    layout(binding = 1, std140) uniform SourceInfo { vec4 logical; vec4 raster; vec4 size; } src;
                    layout(binding = 2, std140) uniform OutputInfo { vec4 logical; vec4 raster; vec4 size; } dst;
                    layout(location = 0) out vec4 color;
                    void main() {
                        vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
                        color = texture(image, (position - src.raster.xy) / src.raster.zw);
                    }
                    """));
                builder.Fullscreen("expand", program, pass =>
                {
                    pass.SampledImage(0, builder.Source, SamplerState.LinearTransparent);
                    pass.ImageParameters(1, builder.Source);
                    pass.ImageParameters(2, result);
                    pass.ColorAttachment(0, result);
                });
                return builder.Output(result);
            });
            Rect bounds = s_bounds.Inflate(2);
            Rect region = new(bounds.X + 2, bounds.Y + 2, 4, 4);
            float[] full = Render(graph, bounds, scale);
            float[] partial = Render(graph, bounds, scale, region);
            int width = PixelRect.FromRect(bounds, scale).Width;
            PixelRect selected = PixelRect.FromRect(region.Translate(-bounds.Position), scale);
            for (int y = selected.Y; y < selected.Bottom; y++)
                for (int x = selected.X; x < selected.Right; x++)
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int index = (y * width + x) * 4 + channel;
                        Assert.That(partial[index], Is.EqualTo(full[index]).Within(0.002));
                    }
            Assert.That(full, Has.Some.GreaterThan(0.5f));
            Assert.That(full[3], Is.Zero, "The padding must sample a transparent border.");
        });
    }

    [Test]
    public void InvalidLaterShaderFailsExplicitlyAndAValidRequestStillRenders()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription invalid = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage output = builder.Image2D("output", builder.Source);
                builder.Fullscreen("valid-prefix", ShaderGraphTests.ColorProgram, pass =>
                {
                    pass.ColorAttachment(0, output);
                    pass.PushConstants(Vector4.One);
                });
                builder.Fullscreen("broken-pass", ShaderProgram.Fullscreen(new ShaderSource("#version 450\ninvalid")),
                    pass => pass.ColorAttachment(0, output));
                return builder.Output(output);
            });
            Assert.That(() => Render(invalid, s_bounds), Throws.Exception.With.Message.Contains("broken-pass"));
            float[] valid = Render(ShaderGraphTests.ColorGraph(Vector4.One), s_bounds);
            Assert.That(valid, Has.All.EqualTo(1f));
        });
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void LowerResolutionOutputPreservesCompositionCoordinates(float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage output = builder.Image2D("half-size", builder.Source, padding: 1, resolutionScale: 0.5f);
                var program = ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(binding = 0, std140) uniform Info { vec4 logical; vec4 raster; vec4 size; } info;
                    layout(location = 0) out vec4 color;
                    void main() {
                        vec2 position = info.raster.xy + gl_FragCoord.xy / info.size.z;
                        color = vec4(position / 32, 0, 1);
                    }
                    """));
                builder.Fullscreen("coordinates", program, pass =>
                {
                    pass.ImageParameters(0, output);
                    pass.ColorAttachment(0, output);
                });
                return builder.Output(output);
            });
            // The requested target starts on a different grid phase than the graph's output.
            Rect bounds = s_bounds.Inflate(2);
            PixelRect pixels = PixelRect.FromRect(bounds, scale);
            float[] actual = Render(graph, bounds, scale);
            // Exclude the outer low-resolution texel, where clamp-to-edge correctly holds its center value.
            int margin = (int)(2 * scale);
            for (int y = margin; y < pixels.Height - margin; y++)
                for (int x = margin; x < pixels.Width - margin; x++)
                {
                    int offset = (y * pixels.Width + x) * 4;
                    Assert.That(actual[offset], Is.EqualTo((bounds.X + (x + 0.5f) / scale) / 32).Within(0.002));
                    Assert.That(actual[offset + 1], Is.EqualTo((bounds.Y + (y + 0.5f) / scale) / 32).Within(0.002));
                }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GraphAfterUnknownCustomEffectPreservesMovedSourceAndReusesPrograms(bool nested)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage output = builder.Image2D("returned", s_bounds);
                var program = ShaderProgram.Fullscreen(new ShaderSource(Graphics3DTestHelpers.PassthroughFragmentShader));
                builder.Fullscreen("return-source", program, pass =>
                {
                    pass.SampledImage(0, builder.Source);
                    pass.ColorAttachment(0, output);
                });
                return builder.Output(output);
            });
            static void MoveOutsideDomain(FilterEffectContext context)
                => context.CustomEffect(new Graphics.Vector(100, 100), static (offset, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                        target.Bounds = target.Bounds.Translate(offset);
                });

            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            float[] first = Render(graph, s_bounds, cache: cache, sharedPool: pool,
                beforeGraph: MoveOutsideDomain, nestedGraph: nested);
            long creations = cache.Statistics.Creations;
            float[] second = Render(graph, s_bounds, cache: cache, sharedPool: pool,
                beforeGraph: MoveOutsideDomain, nestedGraph: nested);
            Assert.Multiple(() =>
            {
                Assert.That(first.Where((_, index) => index % 4 == 3), Has.All.EqualTo(1f));
                Assert.That(first[0], Is.GreaterThan(first[1]));
                Assert.That(second, Is.EqualTo(first));
                Assert.That(creations, Is.EqualTo(2));
                Assert.That(cache.Statistics.Creations, Is.EqualTo(creations));
                Assert.That(cache.Statistics.Hits, Is.GreaterThanOrEqualTo(2));
            });
        });
    }

    private static ShaderGraphDescription ComputeGraph(float value)
        => ShaderGraphBuilder.Build(value, static (builder, amount) =>
        {
            ShaderBuffer data = builder.Buffer<Vector4>("data", 1);
            ShaderImage output = builder.Image2D("output", builder.Source);
            var generate = ShaderProgram.Compute(new ShaderSource("""
                #version 450
                layout(local_size_x = 8) in;
                layout(binding = 0, std430) buffer Data { vec4 values[]; } data;
                layout(push_constant) uniform Values { float amount; } parameters;
                void main() {
                    if (gl_GlobalInvocationID.x >= 1) return;
                    data.values[0] = vec4(parameters.amount, 0.5, 0.125, 1);
                }
                """));
            builder.Compute("generate", generate, pass =>
            {
                pass.StorageBuffer(0, data, ResourceAccess.Write);
                pass.PushConstants(amount);
                pass.DispatchThreads(1);
            });
            var draw = ShaderProgram.Graphics(new ShaderSource("""
                #version 450
                layout(binding = 0, std430) readonly buffer Data { vec4 values[]; } data;
                layout(location = 0) out vec4 color;
                void main() {
                    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                    gl_Position = vec4(p * 2 - 1, 0, 1);
                    color = data.values[gl_InstanceIndex];
                }
                """), new ShaderSource("""
                #version 450
                layout(location = 0) in vec4 color;
                layout(location = 0) out vec4 result;
                void main() { result = color; }
                """));
            builder.Render("draw", draw, pass =>
            {
                pass.StorageBuffer(0, data, ResourceAccess.Read);
                pass.ColorAttachment(0, output);
                pass.Draw(3);
            });
            return builder.Output(output);
        });

    internal static float[] Render(ShaderGraphDescription graph, Rect targetBounds, float scale = 1,
        Rect? requestedRegion = null, ProgramCache<ShaderGraphPipeline>? cache = null,
        RenderTargetPool? sharedPool = null, Action<FilterEffectContext>? beforeGraph = null, bool nestedGraph = false)
    {
        var effect = new GraphEffect(graph, beforeGraph, nestedGraph);
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var brush = (Brush.Resource)new SolidColorBrush(Color.FromArgb(255, 128, 64, 32)).ToResource(CompositionContext.Default);
        using var source = new RectangleRenderNode(s_bounds, brush, null);
        using var root = new FilterEffectRenderNode(resource);
        root.AddChild(source);
        using CompiledRenderRequest compiled = Graphics3DTestHelpers.Compile(root, new RenderRequestOptions(
            RenderIntent.Preview, RenderRequestPurpose.Frame, targetDomain: targetBounds,
            requestedRegion: requestedRegion, outputScale: scale, maxWorkingScale: scale,
            cachePolicy: RenderCacheOptions.Disabled));
        if (beforeGraph is null && !nestedGraph)
        {
            Assert.That(compiled.Graph.Fragments.Any(f => f.Kind == RenderFragmentKind.ShaderGraph), Is.True);
            Assert.That(compiled.Graph.Fragments.Any(f => f.Kind == RenderFragmentKind.FilterEffectSegment), Is.False);
        }
        using RenderTargetPool? ownedPool = sharedPool is null ? new RenderTargetPool(factory: null) : null;
        RenderTargetPool pool = sharedPool ?? ownedPool!;
        var result = Graphics3DTestHelpers.ExecuteIntoPool(pool, compiled, targetBounds, requestedRegion ?? targetBounds,
            scale, scale, translateToTarget: true,
            createExecutor: targets => new RenderRequestExecutor(targets, graphProgramCache: cache));
        return result.Pixels.Select(v => (float)BitConverter.UInt16BitsToHalf(v)).ToArray();
    }

    private sealed class GraphEffect(ShaderGraphDescription graph, Action<FilterEffectContext>? beforeGraph, bool nested) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            beforeGraph?.Invoke(context);
            if (nested)
            {
                context.CustomEffect(graph, static (description, execution) =>
                {
                    using var child = new FilterEffectContext(execution.Targets.CalculateBounds());
                    child.ShaderGraph(description);
                    using var builder = new SKImageFilterBuilder();
                    using FilterEffectExecutor executor = execution.CreateExecutor(execution.Targets, builder);
                    executor.Apply(child);
                });
            }
            else
                context.ShaderGraph(graph);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool changed = true;
            resource.Reconcile(this, context, ref changed);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }
}
