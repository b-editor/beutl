using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Runs a fragment shader over its whole output, once per pixel.</summary>
public partial class ShaderFullscreenPassNode : ShaderGraphicsPassNode
{
    internal const string DefaultCode = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D source;

        void main() {
            color = texture(source, uv);
        }
        """;

    public ShaderFullscreenPassNode()
    {
        ScanProperties<ShaderFullscreenPassNode>();
        AddCodeInput(Code);
        AddFixedPorts(Format, Targets);
        InsertDefaultPorts(["color"], ["source"]);
    }

    [DataType(DataType.MultilineText)]
    [Display(Name = nameof(NodeGraphStrings.ShaderPort_Code), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<string> Code { get; } = new ValueOnlyProperty<string>(DefaultCode);

    private protected override string PassLabel => string.IsNullOrWhiteSpace(Name.Value) ? "Fullscreen" : Name.Value;

    private protected override IReadOnlyList<(ShaderStage Stage, string Code)> GetStages()
        => [(ShaderStage.Fragment, Code.Value)];

    private protected override ShaderProgram CreateProgram(IReadOnlyList<(ShaderStage Stage, string Code)> stages)
        => ShaderProgram.Fullscreen(new ShaderSource(stages[0].Code));

    private protected override void RecordPass(ShaderGraphBuilder builder, string name, ShaderProgram program, Action<ShaderPassBuilder> record)
        => builder.Fullscreen(name, program, record);

    private protected override void RecordDefaultWork(ShaderPassBuilder pass, ShaderImage workImage)
    {
    }
}
