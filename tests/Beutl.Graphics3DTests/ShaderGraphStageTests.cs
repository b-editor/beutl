using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Shaders;
using static Beutl.Graphics3DTests.ShaderGraphRenderingTests;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphStageTests
{
    private static readonly ShaderSource s_vertex = new("""
        #version 450
        layout(location = 0) out vec4 tint;
        void main() {
            vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
            gl_Position = vec4(p * 2 - 1, 0, 1);
            tint = vec4(0.5, 0.25, 0.125, 1);
        }
        """);
    private static readonly ShaderSource s_fragment = new("""
        #version 450
        layout(location = 0) in vec4 tint;
        layout(location = 0) out vec4 color;
        void main() { color = tint; }
        """);
    private static readonly ShaderSource s_geometry = new("""
        #version 450
        layout(triangles) in;
        layout(triangle_strip, max_vertices = 3) out;
        layout(location = 0) in vec4 inputTint[];
        layout(location = 0) out vec4 tint;
        void main() {
            for (int i = 0; i < 3; i++) {
                gl_Position = gl_in[i].gl_Position;
                tint = inputTint[i];
                EmitVertex();
            }
            EndPrimitive();
        }
        """);
    private static readonly ShaderSource s_control = new("""
        #version 450
        layout(vertices = 3) out;
        layout(location = 0) in vec4 inputTint[];
        layout(location = 0) out vec4 tint[];
        void main() {
            gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
            tint[gl_InvocationID] = inputTint[gl_InvocationID];
            gl_TessLevelOuter[0] = 1; gl_TessLevelOuter[1] = 1; gl_TessLevelOuter[2] = 1;
            gl_TessLevelInner[0] = 1;
        }
        """);
    private static readonly ShaderSource s_evaluation = new("""
        #version 450
        layout(triangles, equal_spacing, ccw) in;
        layout(location = 0) in vec4 inputTint[];
        layout(location = 0) out vec4 tint;
        void main() {
            gl_Position = gl_in[0].gl_Position * gl_TessCoord.x
                + gl_in[1].gl_Position * gl_TessCoord.y + gl_in[2].gl_Position * gl_TessCoord.z;
            tint = inputTint[0];
        }
        """);

    private static ShaderGraphDescription Graph(bool geometry, bool tessellation, int patchSize = 3)
        => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            ShaderImage output = builder.Image2D("output", builder.Source);
            builder.Render("stages", ShaderProgram.Graphics(s_vertex, s_fragment, geometry ? s_geometry : null,
                tessellation ? s_control : null, tessellation ? s_evaluation : null), pass =>
            {
                pass.ColorAttachment(0, output);
                if (tessellation) pass.PatchControlPoints(patchSize);
                pass.Draw(3);
            });
            return builder.Output(output);
        });

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void OptionalStagesRenderOrReportAnUnsupportedDevice(bool geometry, bool tessellation)
    {
        OnDevice(capabilities =>
        {
            TestContext.WriteLine($"Enabled stages: geometry={capabilities.GeometryShaders}, tessellation={capabilities.TessellationShaders}");
            ShaderGraphDescription graph = Graph(geometry, tessellation);
            if (geometry && !capabilities.GeometryShaders || tessellation && !capabilities.TessellationShaders)
                Assert.That(() => Render(graph, SourceBounds), Throws.Exception.With.Message.Contains("unsupported SPIR-V capability"));
            else
                AssertRed(graph, 0.5f);
        });
    }

    [Test]
    public void PerVertexArraysAreLinkedByElementTypeAcrossAllGraphicsStages()
    {
        using var compiler = new VulkanShaderCompiler();
        ShaderGraphDescription graph = Graph(true, true);
        ShaderInterface? previous = null;
        foreach (ShaderStage stage in new[] { ShaderStage.Vertex, ShaderStage.TessellationControl,
                     ShaderStage.TessellationEvaluation, ShaderStage.Geometry, ShaderStage.Fragment })
        {
            ShaderSource source = graph.Passes[0].Program!.Stages[stage];
            var current = new ShaderInterface(compiler.CompileToSpirv(source.Text, stage), stage);
            if (previous is not null) current.ValidateInputs(previous, "linked-stages");
            previous = current;
        }
    }

    [Test]
    public void TessellationRequiresBothStagesAndADeviceSizedPatch()
    {
        Assert.That(() => ShaderProgram.Graphics(s_vertex, s_fragment, tessellationControl: s_control), Throws.ArgumentException);
        OnDevice(capabilities =>
        {
            if (capabilities.TessellationShaders)
                Assert.That(() => Render(Graph(false, true, (int)capabilities.MaxPatchControlPoints + 1), SourceBounds),
                    Throws.Exception.With.Message.Contains("patch size"));
        });
    }
}
