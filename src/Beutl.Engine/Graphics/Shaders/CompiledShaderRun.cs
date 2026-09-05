using System.Collections.Immutable;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Shaders;

/// <summary>An immutable Shader-run topology retained by the structural plan cache.</summary>
/// <remarks>
/// Fragment indices and the merged program are structural. The current graph supplies descriptions and
/// binding callbacks so a cached run does not retain request-scoped fragments or resources.
/// </remarks>
internal sealed class CompiledShaderRun
{
    public CompiledShaderRun(
        ImmutableArray<int> stageFragmentIndices,
        SkslMergedProgram program)
    {
        if (stageFragmentIndices.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A compiled Shader run must contain at least one stage.",
                nameof(stageFragmentIndices));
        }
        foreach (int fragmentIndex in stageFragmentIndices)
            ArgumentOutOfRangeException.ThrowIfNegative(fragmentIndex, nameof(stageFragmentIndices));

        ArgumentNullException.ThrowIfNull(program);
        if (program.RequiresStandaloneExecution)
        {
            throw new ArgumentException(
                "A backend-overflowing program must remain a compatibility boundary.",
                nameof(program));
        }
        if (program.StageCount != stageFragmentIndices.Length)
            throw new ArgumentException("The merged program and semantic stage counts must match.", nameof(program));

        StageFragmentIndices = stageFragmentIndices;
        Program = program;
    }

    public ImmutableArray<int> StageFragmentIndices { get; }

    public SkslMergedProgram Program { get; }

    public bool IsFused => StageFragmentIndices.Length > 1;

    public RenderFragmentReference GetInput(RecordedRenderGraph graph)
    {
        ImmutableArray<RenderFragmentReference> inputs = GetStage(graph, 0).Inputs;
        if (inputs.Length != 1)
            throw new InvalidOperationException("A compiled Shader run requires one direct input.");
        return inputs[0];
    }

    public RenderFragmentReference GetOutput(RecordedRenderGraph graph)
        => GetStage(graph, StageFragmentIndices.Length - 1);

    public RenderFragmentReference GetStage(RecordedRenderGraph graph, int stageIndex)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.Fragments[StageFragmentIndices[stageIndex]];
    }

    public ShaderDescription GetDescription(RecordedRenderGraph graph, int stageIndex)
    {
        RenderFragmentReference stage = GetStage(graph, stageIndex);
        return stage.Payload switch
        {
            ShaderRenderFragmentPayload shader => shader.Description,
            OpacityRenderFragmentPayload opacity => opacity.FusionDescription,
            _ => throw new InvalidOperationException("A compiled Shader run contains a non-Shader stage."),
        };
    }

    public ShaderDescription? GetWholeSourceHead(RecordedRenderGraph graph)
    {
        ShaderDescription head = GetDescription(graph, 0);
        return head.Kind == ShaderDescriptionKind.WholeSource ? head : null;
    }
}
