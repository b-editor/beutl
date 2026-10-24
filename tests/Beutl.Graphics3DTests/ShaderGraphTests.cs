using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics3DTests;

[TestFixture]
public sealed class ShaderGraphTests
{
    internal static readonly ShaderProgram ColorProgram = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) out vec4 color;
        layout(push_constant) uniform Values { vec4 color; } values;
        void main() { color = values.color; }
        """));

    internal static ShaderGraphDescription ColorGraph(Vector4 color)
        => ShaderGraphBuilder.Build(color, static (graph, value) =>
        {
            ShaderImage output = graph.Image2D("output", graph.Source);
            graph.Fullscreen("color", ColorProgram, pass =>
            {
                pass.PushConstants(value);
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        });

    [Test]
    public void ParametersChangeOutputIdentityWithoutRecompilingProgram()
    {
        ShaderGraphDescription first = ColorGraph(new(0.25f, 0, 0, 1));
        ShaderGraphDescription second = ColorGraph(new(0.75f, 0, 0, 1));
        Assert.Multiple(() =>
        {
            Assert.That(first.StructuralIdentity, Is.EqualTo(second.StructuralIdentity));
            Assert.That(first.OutputIdentity, Is.Not.EqualTo(second.OutputIdentity));
            Assert.That(ShaderGraphDescription.GetPipelineIdentity(first.Passes[0]),
                Is.EqualTo(ShaderGraphDescription.GetPipelineIdentity(second.Passes[0])));
        });
    }

    [Test]
    public void BufferContentsParticipateInOutputIdentityButNotProgramIdentity()
    {
        static ShaderGraphDescription Record(float value) => ShaderGraphBuilder.Build(value, static (builder, amount) =>
        {
            builder.Buffer<float>("values", new[] { amount });
            return builder.Output(builder.Source);
        });

        ShaderGraphDescription first = Record(1);
        ShaderGraphDescription same = Record(1);
        ShaderGraphDescription changed = Record(2);
        Assert.Multiple(() =>
        {
            Assert.That(first.OutputIdentity, Is.EqualTo(same.OutputIdentity));
            Assert.That(first.OutputIdentity.GetHashCode(), Is.EqualTo(same.OutputIdentity.GetHashCode()));
            Assert.That(first.OutputIdentity, Is.Not.EqualTo(changed.OutputIdentity));
            Assert.That(first.StructuralIdentity, Is.EqualTo(changed.StructuralIdentity));
        });
    }

    [Test]
    public void FailedAuthoringDoesNotAppendAnEffectItem()
    {
        using var context = new FilterEffectContext(new Rect(10, 20, 40, 30));
        Assert.Throws<InvalidOperationException>(() => context.ShaderGraph(0, static (graph, _) =>
        {
            ShaderImage output = graph.Image2D("unwritten", graph.Source);
            return graph.Output(output);
        }));
        Assert.That(context._items, Is.Empty);
        Assert.That(context.Bounds, Is.EqualTo(context.OriginalBounds));
    }

    [Test]
    public void ForwardReadsAndAttachmentFeedbackAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ShaderGraphBuilder.Build(0, static (graph, _) =>
        {
            ShaderImage output = graph.Image2D("output", graph.Source);
            ShaderImage missing = graph.Image2D("missing", graph.Source);
            graph.Fullscreen("read-before-write", ColorProgram, pass =>
            {
                pass.SampledImage(0, missing);
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        }));
        Assert.Throws<InvalidOperationException>(() => ShaderGraphBuilder.Build(0, static (graph, _) =>
        {
            ShaderImage output = graph.Image2D("output", graph.Source);
            graph.Fullscreen("feedback", ColorProgram, pass =>
            {
                pass.SampledImage(0, output);
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        }));
    }

    [Test]
    public void BuildersCannotEscapeRecordingAndResourcesCannotCrossGraphs()
    {
        ShaderGraphBuilder? escapedGraph = null;
        ShaderPassBuilder? escapedPass = null;
        ShaderGraphDescription first = ShaderGraphBuilder.Build(0, (graph, _) =>
        {
            escapedGraph = graph;
            ShaderImage output = graph.Image2D("output", graph.Source);
            graph.Fullscreen("color", ColorProgram, pass =>
            {
                escapedPass = pass;
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        });
        Assert.Throws<ObjectDisposedException>(() => escapedGraph!.Image2D("late", escapedGraph.Source));
        Assert.Throws<ObjectDisposedException>(() => escapedPass!.Draw(6));
        Assert.Throws<ArgumentException>(() => ShaderGraphBuilder.Build(0,
            (graph, _) => graph.Output(first.Output.Image)));
    }

    [Test]
    public void InitialBufferDataIsCopiedAndBoundsRemainSymbolic()
    {
        Vector4[] data = [new(1, 2, 3, 4)];
        ShaderBuffer? buffer = null;
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            buffer = builder.Buffer<Vector4>("values", data);
            ShaderImage output = builder.Image2D("expanded", builder.Source, padding: 3);
            builder.Fullscreen("color", ColorProgram, pass =>
            {
                pass.StorageBuffer(0, buffer, ResourceAccess.Read);
                pass.ImageParameters(1, output);
                pass.ColorAttachment(0, output);
            });
            return builder.Output(output);
        });
        data[0] = default;
        Assert.That(BitConverter.ToSingle(buffer!.InitialData.AsSpan()), Is.EqualTo(1));
        Assert.That(graph.TransformBounds(new Rect(10, 20, 30, 40)), Is.EqualTo(new Rect(7, 17, 36, 46)));
        Assert.That(graph.TransformBounds(new Rect(-5, 4, 8, 9)), Is.EqualTo(new Rect(-8, 1, 14, 15)));
    }

    [Test]
    public void CompiledInterfaceRejectsMissingConstantsAndWrongDescriptorType()
    {
        using var compiler = new VulkanShaderCompiler();
        byte[] code = compiler.CompileToSpirv(ColorProgram.Stages[ShaderStage.Fragment].Text, ShaderStage.Fragment);
        var shader = new ShaderInterface(code, ShaderStage.Fragment);
        ShaderPassDescription pass = ColorGraph(Vector4.One).Passes[0];
        Assert.That(() => shader.Validate(pass, ShaderStage.Fragment), Throws.Nothing);
        Assert.Throws<ArgumentException>(() => shader.Validate(pass with { Constants = [] }, ShaderStage.Fragment));

        byte[] textured = compiler.CompileToSpirv(Graphics3DTestHelpers.PassthroughFragmentShader, ShaderStage.Fragment);
        var textureInterface = new ShaderInterface(textured, ShaderStage.Fragment);
        Assert.Throws<ArgumentException>(() => textureInterface.Validate(pass, ShaderStage.Fragment));
    }

    [Test]
    public void CompiledStorageBufferLayoutAndWriteAccessAreValidated()
    {
        using var compiler = new VulkanShaderCompiler();
        const string code = """
            #version 450
            layout(local_size_x = 8) in;
            layout(set = 0, binding = 0, std430) buffer Data { vec4 values[]; } data;
            void main() { data.values[gl_GlobalInvocationID.x] = vec4(1); }
            """;
        var shader = new ShaderInterface(compiler.CompileToSpirv(code, ShaderStage.Compute), ShaderStage.Compute);
        ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderBuffer buffer = builder.Buffer<float>("wrong-stride", 8);
            builder.Compute("generate", ShaderProgram.Compute(new ShaderSource(code)), pass =>
            {
                pass.StorageBuffer(0, buffer, ResourceAccess.Write);
                pass.Dispatch(1);
            });
            return builder.Output(builder.Source);
        });
        Assert.That(shader.LocalSize, Is.EqualTo((8u, 1u, 1u)));
        Assert.Throws<ArgumentException>(() => shader.Validate(graph.Passes[0], ShaderStage.Compute));
    }

    [TestCase("isampler2D", "vec2(0)")]
    [TestCase("usampler2D", "vec2(0)")]
    [TestCase("sampler2DShadow", "vec3(0)")]
    public void IncompatibleSampledImageTypesAreRejectedBeforePipelineCreation(string sampler, string coordinate)
    {
        using var compiler = new VulkanShaderCompiler();
        byte[] bytes = compiler.CompileToSpirv($$"""
            #version 450
            layout(binding = 0) uniform {{sampler}} image;
            layout(location = 0) out vec4 color;
            void main() { color = vec4(texture(image, {{coordinate}})); }
            """, ShaderStage.Fragment);
        Assert.Throws<NotSupportedException>(() => new ShaderInterface(bytes, ShaderStage.Fragment));
    }

    [TestCase("ivec4", "ivec4(1)")]
    [TestCase("uvec4", "uvec4(1)")]
    [TestCase("vec4[2]", "vec4[2](vec4(1), vec4(1))")]
    public void UnsupportedColorOutputShapesAreRejectedBeforePipelineCreation(string type, string value)
    {
        using var compiler = new VulkanShaderCompiler();
        byte[] bytes = compiler.CompileToSpirv($$"""
            #version 450
            layout(location = 0) out {{type}} color;
            void main() { color = {{value}}; }
            """, ShaderStage.Fragment);
        Assert.Throws<NotSupportedException>(() => new ShaderInterface(bytes, ShaderStage.Fragment));
    }
}
