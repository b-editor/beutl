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
            if (any(greaterThanEqual(pixel, imageSize(result)))) return;
            imageStore(result, pixel, texelFetch(source, pixel, 0));
        }
        """;

    public ShaderComputePassNode()
    {
        ScanProperties<ShaderComputePassNode>();
        var outResult = CreateOutput<ShaderGraphValue<ShaderImage>>("Out.result", new DisplayAttribute { Name = "result" });
        var source = CreateInput<ShaderGraphValue<ShaderImage>>("In.source", new DisplayAttribute { Name = "source" });
        var inResult = CreateInput<ShaderGraphValue<ShaderImage>>("In.result", new DisplayAttribute { Name = "result" });
        AddInput(this, Code);
        Items.Add(Error);
        Items.Add(Padding);
        Items.Add(ResolutionScale);
        Items.Add(Settings);
        SealFixedPorts(Error);
        Items.Insert(0, outResult);
        Items.Insert(3, source);
        Items.Insert(4, inResult);
        Code.ValueChanged += (_, _) => OnCodeChanged();
    }

    [DataType(DataType.MultilineText)]
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
