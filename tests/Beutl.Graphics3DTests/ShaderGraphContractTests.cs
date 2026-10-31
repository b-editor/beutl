using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphContractTests
{
    [TestCase(ColorEncoding.LinearPremultiplied)]
    [TestCase(ColorEncoding.LinearStraight)]
    [TestCase(ColorEncoding.SrgbPremultiplied)]
    [TestCase(ColorEncoding.SrgbStraight)]
    public void ExplicitColorBoundariesRoundTripHdrAndTransparentPixels(ColorEncoding encoding)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(encoding, static (builder, color) =>
                builder.Output(builder.Input("input", color), color));
            foreach (Vector4 value in new[] { new Vector4(-0.125f, 0.25f, 1.5f, 0.5f), Vector4.Zero })
            {
                var result = Execute(graph, new(0, 0, 4, 2), sourceColor: value);
                Assert.That(result.Pixels.Take(4), Is.EqualTo(new[] { value.X, value.Y, value.Z, value.W }).Within(0.004));
            }
        });
    }

    [Test]
    public void IntermediateEncodingIsNotConvertedImplicitly()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
                builder.Output(builder.Input("encoded-data", ColorEncoding.SrgbStraight)));
            float[] pixels = Execute(graph, new(0, 0, 4, 2), sourceColor: new(0.125f, 0, 0, 0.5f)).Pixels;
            Assert.That(pixels[0], Is.EqualTo(0.537099f).Within(0.002));
            Assert.That(pixels[3], Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void OutputColorConversionPrecedesResampling()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage image = builder.Image("encoded", new(ImageKind.Image2D, 2, 1));
                ShaderGraphOutput selected = builder.Output(image, ColorEncoding.SrgbStraight);
                builder.Fullscreen("black-white", ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(location = 0) out vec4 color;
                    void main() { color = vec4(vec3(gl_FragCoord.x < 1 ? 0 : 1), 1); }
                    """)), pass => pass.ColorAttachment(0, image));
                return selected;
            });
            float[] pixels = Execute(graph, new(0, 0, 2, 1), scale: 4).Pixels;
            Assert.That(pixels[3 * 4], Is.EqualTo(0.375f).Within(0.002),
                "The linear values must be interpolated after decoding each encoded texel.");
        });
    }

    [Test]
    public void ConversionRetainsAnInputsIndependentRasterDensity()
    {
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            builder.Output(builder.Input("input", ColorEncoding.SrgbStraight), ColorEncoding.SrgbStraight));
        var source = new ShaderImageFrame(new(3, 5, 8, 4), new(2, 4, 10, 6), new(5, 3), 0.5f);
        var regions = new ShaderGraphRegions(graph, source, 2, default, source.Bounds);
        Assert.That(regions.Frames[graph.Output.RenderingImage.Id], Is.EqualTo(source));
    }

    [Test]
    public void ReplayRecomputesSeeksAndResetsWithoutPreviewState()
    {
        OnDevice(_ =>
        {
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            foreach ((double time, double reset, float expected) in new[]
                     { (0.75, 0.0, 0.75f), (0.25, 0.0, 0.25f), (0.75, 0.0, 0.75f), (0.75, 0.5, 0.25f), (0.25, 0.5, 0f) })
            {
                ShaderGraphDescription graph = History(time, reset);
                Assert.That(Execute(graph, new(0, 0, 4, 2), cache: cache).Pixels[0], Is.EqualTo(expected).Within(0.002));
                Assert.That(History(time, reset).OutputIdentity, Is.EqualTo(graph.OutputIdentity));
            }
            Assert.That(cache.Statistics.Creations, Is.EqualTo(3), "Update, display and output programs are reused across seeks.");
            Assert.That(History(0.75).OutputIdentity, Is.Not.EqualTo(History(0.5).OutputIdentity));
            Assert.That(History(0.75).OutputIdentity, Is.Not.EqualTo(History(0.75, 0.5).OutputIdentity));
        });
    }

    [Test]
    public void ReplayRecordsExactStepTimesIncludingTheFinalShortStep()
    {
        var steps = new List<ShaderTimeStep>();
        int initialized = 0;
        ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            builder.Replay(TimeSpan.FromMilliseconds(1350), TimeSpan.FromMilliseconds(100),
                _ => { initialized++; return 0; }, (_, state, step) => { steps.Add(step); return state + 1; },
                TimeSpan.FromSeconds(1));
            return builder.Output(builder.Source);
        });
        Assert.That(initialized, Is.EqualTo(1));
        Assert.That(steps.Select(s => s.Time.TotalMilliseconds), Is.EqualTo(new[] { 1100, 1200, 1300, 1350 }));
        Assert.That(steps.Select(s => s.Delta.TotalMilliseconds), Is.EqualTo(new[] { 100, 100, 100, 50 }));
        Assert.That(steps.Select(s => s.Index), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    internal static ShaderGraphDescription History(double seconds, double reset = 0)
        => ShaderGraphBuilder.Build(seconds, (builder, time) =>
        {
            ShaderBuffer state = builder.Replay(TimeSpan.FromSeconds(time), TimeSpan.FromSeconds(0.5),
                graph => graph.Buffer<Vector4>("initial", new[] { new Vector4(0, 0, 0, 1) }),
                (graph, previous, step) =>
                {
                    ShaderBuffer next = graph.Buffer<Vector4>($"state-{step.Index}", 1);
                    graph.Compute($"update-{step.Index}", s_update, pass =>
                    {
                        pass.StorageBuffer(0, previous, ResourceAccess.Read);
                        pass.StorageBuffer(1, next, ResourceAccess.Write);
                        pass.PushConstants(new Vector4((float)step.Delta.TotalSeconds, 0, 0, 0));
                        pass.Dispatch(1);
                    });
                    return next;
                }, TimeSpan.FromSeconds(reset));
            ShaderImage output = builder.Image2D("output", builder.Source);
            builder.Fullscreen("display", Display, pass =>
            {
                pass.StorageBuffer(0, state, ResourceAccess.Read);
                pass.ColorAttachment(0, output);
            });
            return builder.Output(output);
        });

    private static readonly ShaderProgram s_update = ShaderProgram.Compute(new ShaderSource("""
        #version 450
        layout(local_size_x = 1) in;
        layout(binding = 0, std430) readonly buffer Input { vec4 value; } inputData;
        layout(binding = 1, std430) writeonly buffer Output { vec4 value; } outputData;
        layout(push_constant) uniform Step { vec4 delta; } step;
        void main() { outputData.value = inputData.value + step.delta; }
        """));

    internal static readonly ShaderProgram Display = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0, std430) readonly buffer Data { vec4 value; } data;
        layout(location = 0) out vec4 color;
        void main() { color = data.value; }
        """));

    internal static (float[] Pixels, ShaderGraphExecutionStatistics Statistics) Execute(ShaderGraphDescription graph,
        Rect sourceBounds, float scale = 1, Rect? region = null, Vector4? sourceColor = null,
        ProgramCache<ShaderGraphPipeline>? cache = null, Func<int, int, Vector4>? sourcePixel = null)
    {
        var context = GpuTestEnvironment.ResolveVulkan(GraphicsContextFactory.SharedContext!);
        PixelRect sourcePixels = PixelRect.FromRect(sourceBounds, scale);
        PixelRect outputPixels = PixelRect.FromRect(graph.TransformBounds(sourceBounds), scale);
        using ITexture2D source = context.CreateTexture2D(sourcePixels.Width, sourcePixels.Height, TextureFormat.RGBA16Float);
        using ITexture2D output = context.CreateTexture2D(outputPixels.Width, outputPixels.Height, TextureFormat.RGBA16Float);
        var data = new Half[sourcePixels.Width * sourcePixels.Height * 4];
        for (int y = 0; y < sourcePixels.Height; y++)
            for (int x = 0; x < sourcePixels.Width; x++)
            {
                Vector4 color = sourceColor ?? sourcePixel?.Invoke(x, y)
                    ?? new Vector4((x + 0.5f) / sourcePixels.Width, (y + 0.5f) / sourcePixels.Height, 0, 1);
                int offset = (y * sourcePixels.Width + x) * 4;
                data[offset] = (Half)color.X; data[offset + 1] = (Half)color.Y;
                data[offset + 2] = (Half)color.Z; data[offset + 3] = (Half)color.W;
            }
        source.Upload(MemoryMarshal.AsBytes(data.AsSpan()));
        using ProgramCache<ShaderGraphPipeline>? ownedCache = cache is null ? ShaderGraphPipeline.CreateCache() : null;
        ProgramCache<ShaderGraphPipeline> programs = cache ?? ownedCache!;
        var frame = new ShaderImageFrame(sourceBounds,
            new(sourcePixels.X / scale, sourcePixels.Y / scale, sourcePixels.Width / scale, sourcePixels.Height / scale),
            sourcePixels.Size, scale);
        ShaderGraphExecutionStatistics statistics = ShaderGraphExecutor.Execute(graph, source, frame, output, scale, default,
            (device, pass, identity) => ShaderGraphPipeline.Acquire(programs, new(context, context, "test", "test"), device, pass, identity), region);
        float[] pixels = MemoryMarshal.Cast<byte, Half>(output.DownloadPixels()).ToArray().Select(v => (float)v).ToArray();
        return (pixels, statistics);
    }
}
