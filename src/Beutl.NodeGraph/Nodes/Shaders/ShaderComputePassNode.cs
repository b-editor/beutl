using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Runs a compute shader, by default over every pixel of the first image it writes.</summary>
public partial class ShaderComputePassNode : ShaderPassNode
{
    internal const string DefaultCode = """
        #version 450
        layout(local_size_x = 16, local_size_y = 16) in;
        layout(set = 0, binding = 0) uniform sampler2D source;
        layout(set = 0, binding = 1, rgba16f) uniform writeonly image2D result;

        void main() {
            ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
            ivec2 size = imageSize(result);
            if (any(greaterThanEqual(pixel, size))) return;
            imageStore(result, pixel, textureLod(source, (vec2(pixel) + 0.5) / vec2(size), 0.0));
        }
        """;

    public ShaderComputePassNode()
    {
        ScanProperties<ShaderComputePassNode>();
        AddCodeInput(Code);
        AddFixedPorts();
        InsertDefaultPorts(["result"], ["source", "result"]);
    }

    [DataType(CustomDataTypes.Code)]
    [Display(Name = nameof(NodeGraphStrings.ShaderPort_Code), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<string> Code { get; } = new ValueOnlyProperty<string>(DefaultCode);

    private protected override string PassLabel => string.IsNullOrWhiteSpace(Name.Value) ? "Compute" : Name.Value;

    private protected override IReadOnlyList<(ShaderStage Stage, string Code)> GetStages()
        => [(ShaderStage.Compute, Code.Value)];

    private protected override ShaderProgram CreateProgram(IReadOnlyList<(ShaderStage Stage, string Code)> stages)
        => ShaderProgram.Compute(new ShaderSource(stages[0].Code));

    private protected override void RecordPass(ShaderGraphBuilder builder, string name, ShaderProgram program, Action<ShaderPassBuilder> record)
        => builder.Compute(name, program, record);

    private protected override void RecordDefaultWork(ShaderPassBuilder pass, ShaderImage workImage)
        => pass.DispatchImage(workImage);
}
