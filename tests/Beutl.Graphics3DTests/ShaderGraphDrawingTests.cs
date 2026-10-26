using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Rendering.Requests;
using static Beutl.Graphics3DTests.ShaderGraphRenderingTests;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphDrawingTests
{
    private static readonly ShaderSource s_vertex = new("""
        #version 450
        layout(push_constant) uniform Values { vec4 color; vec4 depth; } values;
        void main() {
            vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
            gl_Position = vec4(p * 2 - 1, values.depth.x, 1);
        }
        """);
    private static readonly ShaderSource s_fragment = new("""
        #version 450
        layout(push_constant) uniform Values { vec4 color; vec4 depth; } values;
        layout(location = 0) out vec4 color;
        void main() { color = values.color; }
        """);
    private static readonly ShaderProgram s_color = ShaderProgram.Graphics(s_vertex, s_fragment);
    private static readonly ShaderProgram s_depth = ShaderProgram.Graphics(s_vertex, new("#version 450\nvoid main() {}"));

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DrawValues(Vector4 Color, Vector4 Depth);

    [TestCase(false)]
    [TestCase(true)]
    public void ComputeProducedCommandsDriveIndirectDispatchAndDrawAfterBufferCopy(bool indexed)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription Record(float red) => ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                int count = indexed ? 9 : 8;
                ShaderBuffer generated = builder.Buffer<uint>("generated", count);
                ShaderBuffer commands = builder.Buffer<uint>("commands", count);
                ShaderBuffer color = builder.Buffer<Vector4>("color", 1);
                builder.Clear("initialize", generated);
                builder.Compute("generate", ShaderProgram.Compute(new ShaderSource($$"""
                    #version 450
                    layout(local_size_x = 1) in;
                    layout(binding = 0, std430) buffer Commands { uint words[]; } commands;
                    void main() {
                        commands.words[0] = 1; commands.words[1] = 1; commands.words[2] = 1;
                        commands.words[4] = 3; commands.words[5] = 1;
                    }
                    """)), pass => { pass.StorageBuffer(0, generated, ResourceAccess.ReadWrite); pass.Dispatch(1); });
                builder.Copy("copy-commands", generated, commands);
                builder.Clear("discard-original", generated);
                builder.Compute("dispatch-indirect", ShaderProgram.Compute(new ShaderSource("""
                    #version 450
                    layout(local_size_x = 1) in;
                    layout(binding = 0, std430) buffer Color { vec4 value; } color;
                    layout(push_constant) uniform Values { vec4 color; } values;
                    void main() { color.value = values.color; }
                    """)), pass =>
                {
                    pass.StorageBuffer(0, color, ResourceAccess.Write);
                    pass.PushConstants(new Vector4(red, 0, 0, 1));
                    pass.DispatchIndirect(commands);
                });
                ShaderImage output = builder.Image2D("output", builder.Source);
                ShaderBuffer indices = builder.Buffer<ushort>("indices", new ushort[] { 0, 1, 2 });
                builder.Render("draw-indirect", ShaderProgram.Fullscreen(new ShaderSource("""
                    #version 450
                    layout(binding = 0, std430) readonly buffer Color { vec4 value; } color;
                    layout(location = 0) out vec4 result;
                    void main() { result = color.value; }
                    """)), pass =>
                {
                    pass.StorageBuffer(0, color, ResourceAccess.Read);
                    pass.ColorAttachment(0, output);
                    if (indexed)
                    {
                        pass.IndexBuffer(indices, IndexFormat.UInt16);
                        pass.DrawIndexedIndirect(commands, offset: 16);
                    }
                    else pass.DrawIndirect(commands, offset: 16);
                });
                return builder.Output(output);
            });
            using var cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            float[] first = Render(Record(0.25f), SourceBounds, cache: cache, sharedPool: pool);
            long creations = cache.Statistics.Creations;
            float[] second = Render(Record(0.75f), SourceBounds, cache: cache, sharedPool: pool);
            Assert.That(first[0], Is.EqualTo(0.25f).Within(0.003));
            Assert.That(second[0], Is.EqualTo(0.75f).Within(0.003));
            Assert.That(second[3], Is.EqualTo(1));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(creations));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void IndirectDrawCountIsDynamicAndFeatureChecked(int count)
    {
        OnDevice(capabilities =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderBuffer commands = builder.Buffer<DrawCommand>("commands", new DrawCommand[] { new(3), new(3) });
                ShaderImage output = builder.Image2D("output", builder.Source);
                builder.Render("draw", ShaderGraphTests.ColorProgram, pass =>
                {
                    pass.ColorAttachment(0, output);
                    pass.PushConstants(new Vector4(0.5f, 0, 0, 1));
                    pass.DrawIndirect(commands, drawCount: count);
                });
                return builder.Output(output);
            });
            if (count > 1 && !capabilities.MultipleIndirectDraws)
                Assert.That(() => Render(graph, SourceBounds), Throws.Exception.With.Message.Contains("indirect draw count"));
            else
            {
                float[] pixels = Render(graph, SourceBounds);
                Assert.That(pixels[0], Is.EqualTo(count == 0 ? 0 : 0.5f));
                Assert.That(pixels[3], Is.EqualTo(count == 0 ? 0 : 1));
            }
        });
    }

    [Test]
    public void IndirectRangesAndInitializationAreValidatedWithoutReadingCommandContents()
    {
        Assert.That(Marshal.SizeOf<DrawCommand>(), Is.EqualTo(16));
        Assert.That(Marshal.SizeOf<IndexedDrawCommand>(), Is.EqualTo(20));
        Assert.That(Marshal.SizeOf<DispatchCommand>(), Is.EqualTo(12));
        foreach ((long offset, int count, int stride) in new[] { (2L, 1, 16), (0L, 2, 12), (16L, 1, 16) })
            Assert.That(() => ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderBuffer commands = builder.Buffer<DrawCommand>("commands", new DrawCommand[] { new(3) });
                ShaderImage output = builder.Image2D("output", builder.Source);
                builder.Render("draw", ShaderGraphTests.ColorProgram, pass =>
                {
                    pass.ColorAttachment(0, output);
                    pass.DrawIndirect(commands, offset, count, stride);
                });
                return builder.Output(output);
            }), Throws.ArgumentException);
        Assert.That(() => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            ShaderBuffer commands = builder.Buffer<DrawCommand>("unwritten", 1);
            ShaderImage output = builder.Image2D("output", builder.Source);
            builder.Render("draw", ShaderGraphTests.ColorProgram, pass =>
            {
                pass.ColorAttachment(0, output);
                pass.DrawIndirect(commands);
            });
            return builder.Output(output);
        }), Throws.InvalidOperationException.With.Message.Contains("uninitialized"));
    }

    [TestCase(CompareFunction.Less, 1f, 0.25f, 0.75f)]
    [TestCase(CompareFunction.Greater, 0f, 0.75f, 0.25f)]
    public void DepthWritesAndComparisonsSurvivePassBoundaries(CompareFunction compare, float clear, float near, float far)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage output = builder.Image2D("output", builder.Source);
                ShaderImage depth = builder.Image2D("depth", builder.Source, TextureFormat.Depth32Float);
                Draw("near", 0.25f, near, AttachmentLoadOp.Clear);
                Draw("far", 0.75f, far, AttachmentLoadOp.Load);
                return builder.Output(output);

                void Draw(string name, float red, float z, AttachmentLoadOp load) => builder.Fullscreen(name, s_color, pass =>
                {
                    pass.ColorAttachment(0, output, load);
                    pass.DepthAttachment(depth, load, clear);
                    pass.DepthStencil(new(true, true, compare));
                    pass.PushConstants(new DrawValues(new(red, 0, 0, 1), new(z)));
                });
            });
            AssertRed(graph, 0.25f);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DepthOnlyPassCanBeSampledWithOrWithoutComparison(bool comparison)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage depth = builder.Image2D("depth", builder.Source, TextureFormat.Depth32Float);
                builder.Fullscreen("depth-only", s_depth, pass =>
                {
                    pass.DepthAttachment(depth);
                    pass.DepthStencil(new(true, true));
                    pass.PushConstants(new DrawValues(default, new(0.5f)));
                });
                ShaderImage copy = builder.Image2D("copied-depth", builder.Source, TextureFormat.Depth32Float);
                builder.Copy("copy", depth, copy);
                builder.ClearDepth("clear-original", depth, 0);
                return builder.Output(Sample(builder, copy, comparison ? "sampler2DShadow" : "sampler2D",
                    comparison ? "vec3(uv, 0.25)" : "uv",
                    comparison ? "vec4(vec3(texture(image, COORD)), 1)" : "vec4(vec3(texture(image, COORD).r), 1)",
                    SamplerState.NearestClamp with { Compare = comparison ? CompareFunction.LessOrEqual : null }));
            });
            AssertRed(graph, comparison ? 1f : 0.5f);
        });
    }

    [Test]
    public void StencilOperationsAndLoadPreserveTheMask()
    {
        OnDevice(capabilities =>
        {
            TextureFormat format = capabilities.SupportsImage(new(ImageKind.Image2D, 4, 4, TextureFormat.Depth24Stencil8),
                ImageUsage.DepthStencilAttachment) ? TextureFormat.Depth24Stencil8 : TextureFormat.Depth32FloatStencil8;
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage depth = builder.Image2D("depth-stencil", builder.Source, format);
                ShaderImage output = builder.Image2D("output", builder.Source);
                var replace = new StencilFaceState(CompareFunction.Always, StencilOperation.Keep,
                    StencilOperation.Keep, StencilOperation.Replace, Reference: 5);
                builder.Fullscreen("stencil-mask", s_depth, pass =>
                {
                    pass.DepthAttachment(depth, clearStencil: 2);
                    pass.DepthStencil(new(false, false, StencilEnabled: true, Front: replace, Back: replace));
                    pass.PushConstants(new DrawValues(default, default));
                });
                var equal = replace with { Compare = CompareFunction.Equal, Pass = StencilOperation.Keep, WriteMask = 0 };
                builder.Fullscreen("stencil-test", s_color, pass =>
                {
                    pass.ColorAttachment(0, output);
                    pass.DepthAttachment(depth, AttachmentLoadOp.Load);
                    pass.DepthStencil(new(false, false, StencilEnabled: true, Front: equal, Back: equal));
                    pass.PushConstants(new DrawValues(new(0.75f, 0, 0, 1), default));
                });
                return builder.Output(output);
            });
            AssertRed(graph, 0.75f);
        });
    }

    [Test]
    public void CullWindingChangesThePipelineAndVisibleFace()
    {
        OnDevice(_ =>
        {
            using var cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            var alphas = new List<float>();
            foreach (FrontFace winding in new[] { FrontFace.CounterClockwise, FrontFace.Clockwise })
            {
                ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
                {
                    ShaderImage output = builder.Image2D("output", builder.Source);
                    builder.Fullscreen("cull", ShaderGraphTests.ColorProgram, pass =>
                    {
                        pass.ColorAttachment(0, output);
                        pass.PushConstants(Vector4.One);
                        pass.Cull(CullMode.Back, winding);
                    });
                    return builder.Output(output);
                });
                alphas.Add(Render(graph, SourceBounds, cache: cache, sharedPool: pool)[3]);
            }
            Assert.That(alphas, Is.EquivalentTo(new[] { 0f, 1f }));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(3)); // two raster states and the shared output conversion
        });
    }

    [Test]
    public void DepthWritesDoNotInitializeStencilOrAComparisonThatNeverPasses()
    {
        foreach (bool stencil in new[] { false, true })
            Assert.That(() => ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage depth = builder.Image2D("depth", builder.Source, TextureFormat.Depth32FloatStencil8);
                builder.Fullscreen("write", s_depth, pass =>
                {
                    pass.DepthAttachment(depth, AttachmentLoadOp.DontCare);
                    pass.DepthStencil(new(true, true, stencil ? CompareFunction.Always : CompareFunction.Never));
                });
                if (stencil)
                {
                    var equal = new StencilFaceState(CompareFunction.Equal, StencilOperation.Keep,
                        StencilOperation.Keep, StencilOperation.Keep);
                    builder.Fullscreen("read-stencil", s_depth, pass =>
                    {
                        pass.DepthAttachment(depth, AttachmentLoadOp.Load);
                        pass.DepthStencil(new(false, false, StencilEnabled: true, Front: equal, Back: equal));
                    });
                }
                return builder.Output(Sample(builder, depth));
            }), Throws.InvalidOperationException.With.Message.Contains("uninitialized"));
    }

    [Test]
    public void DiscardedDepthCanBeWrittenWithAlwaysButCannotBeCompared()
    {
        ShaderGraphDescription Record(CompareFunction comparison) => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            ShaderImage depth = builder.Image2D("depth", builder.Source, TextureFormat.Depth32Float);
            builder.Fullscreen("depth", s_depth, pass =>
            {
                pass.DepthAttachment(depth, AttachmentLoadOp.DontCare);
                pass.DepthStencil(new(true, true, comparison));
                pass.PushConstants(new DrawValues(default, new(0.5f)));
            });
            return builder.Output(Sample(builder, depth, expression: "vec4(vec3(texture(image, COORD).r), 1)"));
        });
        Assert.That(() => Record(CompareFunction.Less), Throws.InvalidOperationException.With.Message.Contains("undefined"));
        OnDevice(_ => AssertRed(Record(CompareFunction.Always), 0.5f));
    }
}
