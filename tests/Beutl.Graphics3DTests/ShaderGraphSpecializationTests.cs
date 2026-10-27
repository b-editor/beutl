using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Shaders;
using static Beutl.Graphics3DTests.ShaderGraphContractTests;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphSpecializationTests
{
    private static readonly ShaderProgram s_program = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(constant_id = 0) const float red = 0.125;
        layout(constant_id = 1) const bool green = false;
        layout(location = 0) out vec4 color;
        void main() { color = vec4(red, green ? 1 : 0, 0, 1); }
        """));

    internal static ShaderGraphDescription Color(float red) => Graph(s_program.Specialize([
        SpecializationConstant.Create(0, red, ShaderStage.Fragment)]));

    private static ShaderGraphDescription Graph(ShaderProgram program)
        => ShaderGraphBuilder.Build(program, static (graph, shader) =>
        {
            ShaderImage output = graph.Image2D("output", graph.Source);
            graph.Fullscreen("color", shader, pass => pass.ColorAttachment(0, output));
            return graph.Output(output);
        });

    [Test]
    public void ValuesAreSnapshotsAndPipelineIdentityIsIndependentOfDeclarationOrder()
    {
        SpecializationConstant[] values = [
            SpecializationConstant.Create(0, 0.25f, ShaderStage.Fragment),
            SpecializationConstant.Create(1, true, ShaderStage.Fragment)];
        ShaderProgram program = s_program.Specialize(values);
        ShaderGraphDescription first = Graph(program);
        Array.Reverse(values);
        ShaderGraphDescription reordered = Graph(s_program.Specialize(values));
        Assert.That(reordered.OutputIdentity, Is.EqualTo(first.OutputIdentity));
        Assert.That(ShaderGraphDescription.GetPipelineIdentity(reordered.Passes[0]),
            Is.EqualTo(ShaderGraphDescription.GetPipelineIdentity(first.Passes[0])));
        values[1] = SpecializationConstant.Create(0, 0.75f, ShaderStage.Fragment);
        ShaderGraphDescription changed = Graph(s_program.Specialize(values));
        Assert.That(changed.OutputIdentity, Is.Not.EqualTo(first.OutputIdentity));
        Assert.That(ShaderGraphDescription.GetPipelineIdentity(changed.Passes[0]),
            Is.Not.EqualTo(ShaderGraphDescription.GetPipelineIdentity(first.Passes[0])));
        Assert.That(Graph(program).OutputIdentity, Is.EqualTo(first.OutputIdentity));
        Assert.That(s_program.SpecializationConstants, Is.Empty);
    }

    [Test]
    public void DefaultsAndSpecializedVariantsRenderAndReuseTheirOwnPipelines()
    {
        OnDevice(_ =>
        {
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            foreach (float? red in new float?[] { null, 0.25f, 0.75f, 0.25f, null })
            {
                ShaderProgram shader = red.HasValue ? s_program.Specialize([
                    SpecializationConstant.Create(0, red.Value, ShaderStage.Fragment),
                    SpecializationConstant.Create(1, true, ShaderStage.Fragment)]) : s_program;
                float[] pixels = Execute(Graph(shader), new(0, 0, 4, 4), cache: cache).Pixels;
                Assert.That(pixels.Take(4), Is.EqualTo(new[] { red ?? 0.125f, red.HasValue ? 1f : 0, 0, 1 }));
            }
            Assert.That(cache.Statistics.Creations, Is.EqualTo(4)); // Three variants plus the boundary copy.
        });
    }

    [Test]
    public void TheSameIdCanHaveDifferentValuesInVertexAndFragmentStages()
    {
        var vertex = new ShaderSource("""
            #version 450
            layout(constant_id = 0) const float value = 1;
            layout(location = 0) out float tint;
            void main() {
                vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                gl_Position = vec4(p * 2 - 1, 0, 1);
                tint = value;
            }
            """);
        var fragment = new ShaderSource("""
            #version 450
            layout(constant_id = 0) const float value = 1;
            layout(location = 0) in float tint;
            layout(location = 0) out vec4 color;
            void main() { color = vec4(tint, value, 0, 1); }
            """);
        ShaderProgram program = ShaderProgram.Graphics(vertex, fragment).Specialize([
            SpecializationConstant.Create(0, 0.25f, ShaderStage.Vertex),
            SpecializationConstant.Create(0, 0.75f, ShaderStage.Fragment)]);
        OnDevice(_ => Assert.That(Execute(Graph(program), new(0, 0, 4, 4)).Pixels.Take(4),
            Is.EqualTo(new[] { 0.25f, 0.75f, 0, 1 })));
    }

    [Test]
    public void ComputeDispatchAndBufferValidationUseSpecializedSizes()
    {
        ShaderProgram compute = ShaderProgram.Compute(new ShaderSource("""
            #version 450
            layout(local_size_x_id = 0) in;
            layout(constant_id = 1) const uint count = 1;
            layout(constant_id = 2) const float red = 0.125;
            layout(binding = 0, std430) writeonly buffer Data { vec4 values[count]; } data;
            void main() {
                uint index = gl_GlobalInvocationID.x;
                if (index < count) data.values[index] = vec4(red, 0, 0, 1);
            }
            """)).Specialize([
                SpecializationConstant.Create(0, 4u, ShaderStage.Compute),
                SpecializationConstant.Create(1, 8u, ShaderStage.Compute),
                SpecializationConstant.Create(2, 0.5f, ShaderStage.Compute)]);
        ShaderProgram display = ShaderProgram.Fullscreen(new ShaderSource("""
            #version 450
            layout(binding = 0, std430) readonly buffer Data { vec4 values[]; } data;
            layout(location = 0) out vec4 color;
            void main() { color = data.values[7]; }
            """));
        ShaderGraphDescription Build(int count) => ShaderGraphBuilder.Build(count, (graph, length) =>
        {
            ShaderBuffer data = graph.Buffer<Vector4>("data", length);
            graph.Compute("fill", compute, pass =>
            {
                pass.StorageBuffer(0, data, ResourceAccess.Write);
                pass.DispatchThreads(8, 1, 1);
            });
            ShaderImage output = graph.Image2D("output", graph.Source);
            graph.Fullscreen("display", display, pass =>
            {
                pass.StorageBuffer(0, data, ResourceAccess.Read);
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        });
        using var compiler = new VulkanShaderCompiler();
        var shader = new ShaderInterface(compiler.CompileToSpirv(compute.Stages[ShaderStage.Compute].Text, ShaderStage.Compute),
            ShaderStage.Compute, compute.SpecializationConstants);
        Assert.That(shader.LocalSize, Is.EqualTo((4u, 1u, 1u)));
        Assert.That(shader.Bindings.Single().MinimumSize, Is.EqualTo(8 * 16));
        Assert.That(() => shader.Validate(Build(1).Passes[0], ShaderStage.Compute), Throws.ArgumentException);
        OnDevice(_ => Assert.That(Execute(Build(8), new(0, 0, 4, 4)).Pixels.Take(4),
            Is.EqualTo(new[] { 0.5f, 0, 0, 1 })));
    }

    [Test]
    public void InvalidStageDuplicatesAndScalarWidthsFailBeforePipelineCreation()
    {
        Assert.That(() => s_program.Specialize([SpecializationConstant.Create(0, 1, ShaderStage.Compute)]), Throws.ArgumentException);
        Assert.That(() => s_program.Specialize([
            SpecializationConstant.Create(0, 1, ShaderStage.Fragment),
            SpecializationConstant.Create(0, 2, ShaderStage.Vertex | ShaderStage.Fragment)]), Throws.ArgumentException);
        using var compiler = new VulkanShaderCompiler();
        byte[] spirv = compiler.CompileToSpirv(s_program.Stages[ShaderStage.Fragment].Text, ShaderStage.Fragment);
        Assert.That(() => new ShaderInterface(spirv, ShaderStage.Fragment,
            [SpecializationConstant.Create(0, 1d, ShaderStage.Fragment)]), Throws.ArgumentException.With.Message.Contains("requires 4 bytes"));
    }
}
