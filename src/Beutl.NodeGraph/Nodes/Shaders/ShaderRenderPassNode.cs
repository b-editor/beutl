using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Draws geometry a vertex shader produces or reads from buffers.</summary>
public partial class ShaderRenderPassNode : ShaderGraphicsPassNode
{
    internal const string DefaultVertexCode = """
        #version 450
        layout(location = 0) out vec2 uv;

        void main() {
            uv = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
            gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    internal const string DefaultFragmentCode = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;

        void main() {
            color = vec4(uv, 0.0, 1.0);
        }
        """;

    public ShaderRenderPassNode()
    {
        ScanProperties<ShaderRenderPassNode>();
        DepthOutput = CreateOutput<ShaderGraphValue<ShaderImage>>("DepthOutput", ShaderPortDisplays.Get("DepthOutput"));
        Items.Add(DepthOutput);
        AddCodeInput(Vertex);
        AddCodeInput(Fragment);
        AddExtraStagePorts();
        AddFixedPorts(Format, Targets);
        InsertDefaultPorts(["color"], []);
    }

    /// <summary>Adds the ports of stages beyond vertex and fragment.</summary>
    private protected virtual void AddExtraStagePorts()
    {
    }

    [DataType(CustomDataTypes.Code)]
    [Display(Name = nameof(NodeGraphStrings.ShaderPort_VertexCode), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<string> Vertex { get; } = new ValueOnlyProperty<string>(DefaultVertexCode);

    [DataType(CustomDataTypes.Code)]
    [Display(Name = nameof(NodeGraphStrings.ShaderPort_FragmentCode), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<string> Fragment { get; } = new ValueOnlyProperty<string>(DefaultFragmentCode);

    /// <summary>The depth image after this pass, for a pass that reads what this one wrote.</summary>
    public OutputPort<ShaderGraphValue<ShaderImage>> DepthOutput { get; }

    private protected override string PassLabel => string.IsNullOrWhiteSpace(Name.Value) ? "Render" : Name.Value;

    private protected override bool ReadsAcrossItsOutput => false;

    private protected override IReadOnlyList<(ShaderStage Stage, string Code)> GetStages()
        => [(ShaderStage.Vertex, Vertex.Value), (ShaderStage.Fragment, Fragment.Value)];

    private protected override ShaderProgram CreateProgram(IReadOnlyList<(ShaderStage Stage, string Code)> stages)
    {
        ShaderSource? Stage(ShaderStage stage) => stages.FirstOrDefault(s => s.Stage == stage) is { Code: { } code } ? new(code) : null;
        return ShaderProgram.Graphics(Stage(ShaderStage.Vertex)!, Stage(ShaderStage.Fragment)!, Stage(ShaderStage.Geometry),
            Stage(ShaderStage.TessellationControl), Stage(ShaderStage.TessellationEvaluation));
    }

    private protected override void RecordPass(ShaderGraphBuilder builder, string name, ShaderProgram program, Action<ShaderPassBuilder> record)
        => builder.Render(name, program, record);

    private protected override void RecordDefaultWork(ShaderPassBuilder pass, ShaderImage workImage)
        => pass.Draw(3);

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            base.Update(context);
            ShaderGraphValue<ShaderPassResult> pass = Result!;
            DepthOutput = new(recording => recording.Evaluate(pass).Depth
                ?? throw new InvalidOperationException(NodeGraphStrings.ShaderError_NoDepthAttachment));
        }
    }
}

/// <summary>A render pass with geometry and tessellation stages; an empty one is left out.</summary>
public partial class ShaderExtendedRenderPassNode : ShaderRenderPassNode
{
    [DataType(CustomDataTypes.Code)]
    [Display(Name = nameof(NodeGraphStrings.ShaderPort_TessellationControlCode), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<string> TessellationControl { get; } = new ValueOnlyProperty<string>("");

    [DataType(CustomDataTypes.Code)]
    [Display(Name = nameof(NodeGraphStrings.ShaderPort_TessellationEvaluationCode), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<string> TessellationEvaluation { get; } = new ValueOnlyProperty<string>("");

    [DataType(CustomDataTypes.Code)]
    [Display(Name = nameof(NodeGraphStrings.ShaderPort_GeometryCode), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<string> Geometry { get; } = new ValueOnlyProperty<string>("");

    // Runs inside the base constructor, ahead of this class's own, and a port takes its name from the scan.
    private protected override void AddExtraStagePorts()
    {
        ScanProperties<ShaderExtendedRenderPassNode>();
        AddCodeInput(TessellationControl);
        AddCodeInput(TessellationEvaluation);
        AddCodeInput(Geometry);
    }

    private protected override IReadOnlyList<(ShaderStage Stage, string Code)> GetStages()
    {
        var stages = new List<(ShaderStage Stage, string Code)>(base.GetStages());
        if (!string.IsNullOrWhiteSpace(TessellationControl.Value))
            stages.Add((ShaderStage.TessellationControl, TessellationControl.Value));
        if (!string.IsNullOrWhiteSpace(TessellationEvaluation.Value))
            stages.Add((ShaderStage.TessellationEvaluation, TessellationEvaluation.Value));
        if (!string.IsNullOrWhiteSpace(Geometry.Value))
            stages.Add((ShaderStage.Geometry, Geometry.Value));
        return stages;
    }
}
