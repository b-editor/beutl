using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using static Beutl.Graphics3DTests.ShaderGraphRenderingTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphVertexInputTests
{
    private static readonly ShaderSource s_fragment = new("""
        #version 450
        layout(location = 0) flat in vec4 color;
        layout(location = 0) out vec4 result;
        void main() { result = color; }
        """);

    private static readonly ShaderProgram s_program = ShaderProgram.Graphics(new ShaderSource("""
        #version 450
        layout(location = 0) in vec2 position;
        layout(location = 1) in vec4 tint;
        layout(location = 0) flat out vec4 color;
        void main() { gl_Position = vec4(position, 0, 1); color = tint; }
        """), s_fragment);

    private static VertexInputDescription Layout() => new()
    {
        Bindings = [new() { Binding = 0, Stride = 8 }, new() { Binding = 3, Stride = 16, InputRate = VertexInputRate.Instance }],
        Attributes = [new() { Location = 0, Binding = 0, Format = VertexFormat.Float2 },
            new() { Location = 1, Binding = 3, Format = VertexFormat.Float4 }],
    };

    private static ShaderGraphDescription Triangle(bool indexed, IndexFormat format = IndexFormat.UInt32,
        int firstInstance = 1, bool generated = false)
        => ShaderGraphBuilder.Build(0, (graph, _) =>
        {
            Vector2[] positions = [default, default, new(-1, -1), new(3, -1), new(-1, 3)];
            ShaderBuffer vertices = generated ? graph.Buffer<Vector2>("vertices", 5) : graph.Buffer<Vector2>("vertices", positions);
            ShaderBuffer indices = format == IndexFormat.UInt16
                ? graph.Buffer<ushort>("indices", new ushort[] { ushort.MaxValue, ushort.MaxValue, 2, 3, 4 })
                : graph.Buffer<uint>("indices", generated ? new uint[] { uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue }
                    : new uint[] { uint.MaxValue, uint.MaxValue, 2, 3, 4 });
            ShaderBuffer instances = graph.Buffer<Vector4>("instances",
                new Vector4[] { default, default, new(0.25f, 0.5f, 0.125f, 1), new(0.75f, 0.5f, 0.125f, 1) });
            ShaderImage output = graph.Image2D("output", graph.Source);
            if (generated)
            {
                graph.Compute("generate", ShaderProgram.Compute(new ShaderSource("""
                    #version 450
                    layout(local_size_x = 1) in;
                    layout(binding = 0, std430) buffer Vertices { vec2 values[]; } vertices;
                    layout(binding = 1, std430) buffer Indices { uint values[]; } indices;
                    void main() {
                        vertices.values[0] = vec2(0); vertices.values[1] = vec2(0);
                        vertices.values[2] = vec2(-1,-1); vertices.values[3] = vec2(3,-1); vertices.values[4] = vec2(-1,3);
                        indices.values[2] = 2; indices.values[3] = 3; indices.values[4] = 4;
                    }
                    """)), pass =>
                {
                    pass.StorageBuffer(0, vertices, ResourceAccess.Write);
                    pass.StorageBuffer(1, indices, ResourceAccess.Write);
                    pass.Dispatch(1);
                });
            }
            graph.Render("draw", s_program, pass =>
            {
                pass.VertexInput(Layout());
                pass.VertexBuffer(0, vertices, 8);
                pass.VertexBuffer(3, instances, 16);
                pass.ColorAttachment(0, output);
                if (indexed)
                {
                    pass.IndexBuffer(indices, format, format == IndexFormat.UInt16 ? 2 : 4);
                    pass.DrawIndexed(3, firstIndex: 1, vertexOffset: -1, firstInstance: firstInstance);
                }
                else
                    pass.Draw(3, 1, 1, firstInstance);
            });
            return graph.Output(output);
        });

    [Test]
    public void DirectAndIndexedRangesSharePipelinesAndReadInstanceOffsets()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            float[] direct = Render(Triangle(false), SourceBounds, cache: cache, sharedPool: pool);
            long creations = cache.Statistics.Creations;
            float[] shortIndices = Render(Triangle(true, IndexFormat.UInt16), SourceBounds, cache: cache, sharedPool: pool);
            float[] longIndices = Render(Triangle(true, firstInstance: 2), SourceBounds, cache: cache, sharedPool: pool);
            Assert.Multiple(() =>
            {
                Assert.That(shortIndices, Is.EqualTo(direct));
                Assert.That(direct.Where((_, i) => i % 4 == 0), Has.All.EqualTo(0.25f).Within(0.002));
                Assert.That(longIndices.Where((_, i) => i % 4 == 0), Has.All.EqualTo(0.75f).Within(0.002));
                Assert.That(direct.Where((_, i) => i % 4 == 3), Has.All.EqualTo(1));
                Assert.That(cache.Statistics.Creations, Is.EqualTo(creations));
            });
        });
    }

    [Test]
    public void ComputeGeneratedVerticesAndIndicesSurviveUntilDrawing()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription generated = Triangle(true, generated: true);
            Assert.That(generated.Passes[1].Draw.ReadBuffers.Select(b => generated.LastUses[b.Id]), Has.All.EqualTo(1));
            Assert.That(Render(generated, SourceBounds), Is.EqualTo(Render(Triangle(true), SourceBounds)));
        });
    }

    [TestCase(PrimitiveTopology.TriangleList)]
    [TestCase(PrimitiveTopology.TriangleStrip)]
    [TestCase(PrimitiveTopology.TriangleFan)]
    [TestCase(PrimitiveTopology.LineList)]
    [TestCase(PrimitiveTopology.LineStrip)]
    [TestCase(PrimitiveTopology.PointList)]
    public void PrimitiveAssemblyRenders(PrimitiveTopology topology)
    {
        var context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Vector2[] positions = topology switch
            {
                PrimitiveTopology.TriangleList => [new(-1, -1), new(3, -1), new(-1, 3)],
                PrimitiveTopology.TriangleStrip => [new(-1, -1), new(1, -1), new(-1, 1), new(1, 1)],
                PrimitiveTopology.TriangleFan => [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)],
                PrimitiveTopology.PointList => [Vector2.Zero],
                _ => [new(-1, 0), new(1, 0)],
            };
            var graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                var vertices = builder.Buffer<Vector2>("vertices", positions);
                var output = builder.Image2D("output", builder.Source);
                var program = ShaderProgram.Graphics(new ShaderSource("""
                    #version 450
                    layout(location = 0) in vec4 position;
                    layout(location = 0) flat out vec4 color;
                    void main() { gl_Position = position; gl_PointSize = 1; color = vec4(1); }
                    """), s_fragment);
                builder.Render("primitive", program, pass =>
                {
                    // Float2 supplies the missing z=0 and w=1 components of a vec4 input.
                    pass.VertexInput(new()
                    {
                        Bindings = [new() { Stride = 8 }],
                        Attributes = [new() { Format = VertexFormat.Float2 }]
                    });
                    pass.VertexBuffer(0, vertices);
                    pass.Topology(topology);
                    pass.ColorAttachment(0, output);
                    pass.Draw(positions.Length);
                });
                return builder.Output(output);
            });
            if (topology == PrimitiveTopology.TriangleFan && !GLSLFilterPipeline.TryResolveVulkan(context)!.SupportsTriangleFans)
                Assert.That(() => Render(graph, SourceBounds), Throws.Exception.With.Message.Contains("triangle fans"));
            else
            {
                float[] alpha = Render(graph, SourceBounds).Where((_, i) => i % 4 == 3).ToArray();
                if (topology <= PrimitiveTopology.TriangleFan)
                    Assert.That(alpha, Has.All.EqualTo(1));
                else
                    Assert.That(alpha, Has.Some.EqualTo(1));
            }
        });
    }

    [TestCase("vec4", VertexFormat.Float2, true)]
    [TestCase("ivec2", VertexFormat.Int4, true)]
    [TestCase("uvec3", VertexFormat.UInt, true)]
    [TestCase("vec2", VertexFormat.Int2, false)]
    [TestCase("ivec2", VertexFormat.UInt2, false)]
    [TestCase("dvec2", VertexFormat.Float2, false)]
    public void VertexInputsCheckNumericTypesButAllowDefaultComponents(string type, VertexFormat format, bool valid)
    {
        using var compiler = new VulkanShaderCompiler();
        var shader = new ShaderInterface(compiler.CompileToSpirv($$"""
            #version 450
            layout(location = 0) in {{type}} position;
            void main() { gl_Position = vec4(position.xy, 0, 1); }
            """, ShaderStage.Vertex), ShaderStage.Vertex);
        var pass = ShaderGraphTests.ColorGraph(Vector4.One).Passes[0] with
        {
            Draw = ShaderDrawDescription.Empty with { Attributes = [new() { Format = format }] },
        };
        if (valid)
            Assert.That(() => shader.Validate(pass, ShaderStage.Vertex), Throws.Nothing);
        else
            Assert.That(() => shader.Validate(pass, ShaderStage.Vertex), Throws.ArgumentException.With.Message.Contains("numeric type"));
    }

    [Test]
    public void MatrixArrayAndComponentPackedInputsUseTheirDeclaredLocations()
    {
        using var compiler = new VulkanShaderCompiler();
        var shader = new ShaderInterface(compiler.CompileToSpirv("""
            #version 450
            layout(location = 0) in mat2 transform;
            layout(location = 2) in vec2 positions[2];
            layout(location = 4, component = 0) in vec2 offset;
            layout(location = 4, component = 2) in vec2 scale;
            void main() { gl_Position = vec4(transform * (positions[0] + positions[1]) * scale + offset, 0, 1); }
            """, ShaderStage.Vertex), ShaderStage.Vertex);
        var draw = ShaderDrawDescription.Empty with
        {
            Attributes = [.. Enumerable.Range(0, 5).Select(i => new VertexAttributeDescription
                { Location = (uint)i, Format = VertexFormat.Float4 })],
        };
        var pass = ShaderGraphTests.ColorGraph(Vector4.One).Passes[0] with { Draw = draw };
        Assert.That(() => shader.Validate(pass, ShaderStage.Vertex), Throws.Nothing);
        Assert.That(() => shader.Validate(pass with { Draw = draw with { Attributes = draw.Attributes.RemoveAt(3) } }, ShaderStage.Vertex),
            Throws.ArgumentException.With.Message.Contains("location 3"));
    }

    [Test]
    public void DrawStateSeparatesPipelineAndOutputIdentities()
    {
        ShaderGraphDescription graph = Triangle(true);
        ShaderPassDescription pass = graph.Passes[0];
        ShaderPassDescription dynamic = pass with
        {
            Draw = pass.Draw with
            {
                FirstElement = 0,
                VertexOffset = 0,
                FirstInstance = 2,
                Indices = pass.Draw.Indices! with { Format = IndexFormat.UInt16, Offset = 0 }
            },
            Work = (1, 2, 1),
        };
        Assert.That(ShaderGraphDescription.GetPipelineIdentity(dynamic), Is.EqualTo(ShaderGraphDescription.GetPipelineIdentity(pass)));
        Assert.That(new ShaderGraphDescription(graph.Resources, [dynamic], graph.Output).OutputIdentity, Is.Not.EqualTo(graph.OutputIdentity));
        foreach (ShaderDrawDescription draw in new[] {
                     pass.Draw with { Topology = PrimitiveTopology.TriangleStrip },
                     pass.Draw with { Attributes = [new() { Format = VertexFormat.Float4 }] },
                     pass.Draw with { Bindings = [new() { Stride = 16 }] } })
            Assert.That(ShaderGraphDescription.GetPipelineIdentity(pass with { Draw = draw }),
                Is.Not.EqualTo(ShaderGraphDescription.GetPipelineIdentity(pass)));
    }

    [Test]
    public void VertexLayoutsAreCopiedWhenRecorded()
    {
        var layout = Layout();
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            var data = builder.Buffer<Vector4>("data", new Vector4[4]);
            var output = builder.Image2D("output", builder.Source);
            builder.Render("draw", s_program, pass =>
            {
                pass.VertexInput(layout);
                layout.Bindings[0].Stride = uint.MaxValue;
                layout.Attributes[0].Format = VertexFormat.UInt;
                pass.VertexBuffer(0, data);
                pass.VertexBuffer(3, data);
                pass.ColorAttachment(0, output);
                pass.Draw(3);
            });
            return builder.Output(output);
        });
        Assert.That(graph.Passes[0].Draw.Bindings[0].Stride, Is.EqualTo(8));
        Assert.That(graph.Passes[0].Draw.Attributes[0].Format, Is.EqualTo(VertexFormat.Float2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void VertexAndIndexReadsRequireAnEarlierWrite(bool indexed)
    {
        Assert.That(() => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            var data = builder.Buffer<uint>("unwritten", 3);
            var output = builder.Image2D("output", builder.Source);
            builder.Render("read", s_program, pass =>
            {
                pass.ColorAttachment(0, output);
                if (indexed) { pass.IndexBuffer(data); pass.DrawIndexed(3); }
                else { pass.VertexBuffer(0, data); pass.Draw(3); }
            });
            return builder.Output(output);
        }), Throws.InvalidOperationException.With.Message.Contains("uninitialized resource 'unwritten'"));
    }

    [Test]
    public void SwitchingBackToDirectDrawingDoesNotReadTheIndexBuffer()
    {
        var graph = ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            var indices = builder.Buffer<uint>("unused", 1);
            var output = builder.Image2D("output", builder.Source);
            builder.Render("direct", ShaderGraphTests.ColorProgram, pass =>
            {
                pass.IndexBuffer(indices);
                pass.DrawIndexed(3);
                pass.Draw(3);
                pass.PushConstants(Vector4.One);
                pass.ColorAttachment(0, output);
            });
            return builder.Output(output);
        });
        Assert.That(graph.Passes[0].Draw.Indices, Is.Null);
        Assert.That(graph.LastUses[1], Is.EqualTo(-1));
    }

    [TestCase("missing binding")]
    [TestCase("missing buffer")]
    [TestCase("duplicate location")]
    [TestCase("duplicate binding")]
    [TestCase("index alignment")]
    public void InvalidVertexDeclarationsFailDuringRecording(string mistake)
    {
        Assert.That(() => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            var data = builder.Buffer<Vector4>("data", new Vector4[4]);
            var output = builder.Image2D("output", builder.Source);
            builder.Render("invalid", s_program, pass =>
            {
                var layout = Layout();
                if (mistake == "missing binding") layout.Bindings = [layout.Bindings[0]];
                if (mistake == "duplicate binding") layout.Bindings = [.. layout.Bindings, layout.Bindings[0]];
                if (mistake == "duplicate location") layout.Attributes = [.. layout.Attributes, layout.Attributes[0]];
                pass.VertexInput(layout);
                pass.VertexBuffer(0, data);
                if (mistake != "missing buffer") pass.VertexBuffer(3, data);
                if (mistake == "index alignment") pass.IndexBuffer(data, IndexFormat.UInt16, 1);
                pass.Draw(3);
                pass.ColorAttachment(0, output);
            });
            return builder.Output(output);
        }), Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(8u, 1, false)]
    [TestCase(8u, 0, true)]
    [TestCase(6u, 1, false)]
    [TestCase(6u, 2, true)]
    [TestCase(5u, 1, true)]
    public void IndexedAlignmentChecksOnlyIndexIndependentFailures(uint stride, long offset, bool valid)
    {
        ShaderGraphDescription Record() => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            var vertices = builder.Buffer<byte>("vertices", new byte[64]);
            var indices = builder.Buffer<uint>("indices", new[] { stride % 2 == 1 ? 3u : 1u });
            var output = builder.Image2D("output", builder.Source);
            builder.Render("indexed", s_program, pass =>
            {
                pass.VertexInput(new()
                {
                    Bindings = [new() { Stride = stride }],
                    Attributes = [new() { Format = VertexFormat.Float2 }]
                });
                pass.VertexBuffer(0, vertices, offset);
                pass.IndexBuffer(indices);
                pass.DrawIndexed(1);
                pass.ColorAttachment(0, output);
            });
            return builder.Output(output);
        });
        if (valid)
            Assert.That(Record, Throws.Nothing);
        else
            Assert.That(Record, Throws.ArgumentException.With.Message.Contains("aligned"));
    }

    [Test]
    public void DrawRangesAreValidatedAgainOnCacheHits()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            ShaderGraphDescription graph = Triangle(true);
            float[] expected = Render(graph, SourceBounds, cache: cache, sharedPool: pool);
            long creations = cache.Statistics.Creations;
            foreach (ShaderDrawDescription draw in new[] {
                         graph.Passes[0].Draw with { FirstElement = 2 },
                         graph.Passes[0].Draw with { FirstInstance = 3 },
                         graph.Passes[0].Draw with { Indices = null, FirstElement = 2 } })
            {
                var invalid = new ShaderGraphDescription(graph.Resources, [graph.Passes[0] with { Draw = draw }], graph.Output);
                Assert.That(() => Render(invalid, SourceBounds, cache: cache, sharedPool: pool), Throws.ArgumentException.With.Message.Contains("draw exceeds"));
            }
            Assert.That(Render(graph, SourceBounds, cache: cache, sharedPool: pool), Is.EqualTo(expected));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(creations));
        });
    }
}
