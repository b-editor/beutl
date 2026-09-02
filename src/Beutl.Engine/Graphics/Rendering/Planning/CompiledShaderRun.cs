using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Shaders;

internal sealed class CompiledShaderRun
{
    public CompiledShaderRun(
        CompiledShaderRunId id,
        RenderFragmentReference input,
        RenderFragmentReference output,
        ImmutableArray<CompiledShaderStage> stages,
        SkslMergedProgram program,
        ShaderRunCoverageSource coverageSource)
    {
        if (id.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (stages.IsDefaultOrEmpty)
            throw new ArgumentException("A compiled Shader run must contain at least one stage.", nameof(stages));
        ArgumentNullException.ThrowIfNull(program);
        if (program.RequiresStandaloneExecution)
        {
            throw new ArgumentException(
                "A backend-overflowing program must remain a compatibility boundary.",
                nameof(program));
        }
        if (program.StageCount != stages.Length)
            throw new ArgumentException("The merged program and semantic stage counts must match.", nameof(program));
        if (!Enum.IsDefined(coverageSource))
            throw new ArgumentOutOfRangeException(nameof(coverageSource));

        ShaderDescription? wholeSourceHead = stages[0].Description.Kind == ShaderDescriptionKind.WholeSource
            ? stages[0].Description
            : null;
        for (int index = wholeSourceHead is null ? 0 : 1; index < stages.Length; index++)
        {
            if (stages[index].Description.Kind != ShaderDescriptionKind.WholeSource)
                continue;

            throw new ArgumentException(
                "A WholeSource shader can appear only at the head of a compiled Shader run.",
                nameof(stages));
        }
        if (wholeSourceHead is not null
            && (!output.Bounds.Equals(stages[0].Fragment.Bounds)
                || !output.EffectiveScale.Equals(stages[0].Fragment.EffectiveScale)))
        {
            throw new ArgumentException(
                "A WholeSource-headed run must preserve the head stage's output bounds and effective scale.",
                nameof(output));
        }

        Id = id;
        Input = input;
        Output = output;
        Stages = stages;
        Program = program;
        CoverageSource = coverageSource;
        WholeSourceHead = wholeSourceHead;
    }

    public CompiledShaderRunId Id { get; }

    public RenderFragmentReference Input { get; }

    public RenderFragmentReference Output { get; }

    public ImmutableArray<CompiledShaderStage> Stages { get; }

    public SkslMergedProgram Program { get; }

    /// <summary>Gets the WholeSource head whose implicit source mapping governs the run input, if present.</summary>
    public ShaderDescription? WholeSourceHead { get; }

    /// <summary>
    /// Gets the compile-time witness for the run input's coverage provenance. The executor still
    /// consumes a materialized value for every run; this witness does not authorize bypassing that
    /// runtime materialization.
    /// </summary>
    public ShaderRunCoverageSource CoverageSource { get; }

    public bool IsFused => Stages.Length > 1;
}
