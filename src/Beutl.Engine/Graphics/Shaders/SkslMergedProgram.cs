using System.Collections.Immutable;

namespace Beutl.Graphics.Shaders;

internal sealed class SkslMergedProgram
{
    internal SkslMergedProgram(
        string source,
        int firstStageIndex,
        int stageCount,
        ImmutableArray<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget,
        int uniformVectorCount,
        int samplerCount,
        int childCount,
        int sourceByteCount,
        int programTokenCount,
        ImmutableArray<SkslBackendLimit> overflowReasons)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentOutOfRangeException.ThrowIfNegative(firstStageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stageCount);
        if (bindings.IsDefault)
            throw new ArgumentException("The binding layout array must be initialized.", nameof(bindings));
        if (overflowReasons.IsDefault)
            throw new ArgumentException("The overflow-reason array must be initialized.", nameof(overflowReasons));

        FirstStageIndex = firstStageIndex;
        StageCount = stageCount;
        UniformVectorCount = uniformVectorCount;
        SamplerCount = samplerCount;
        ChildCount = childCount;
        SourceByteCount = sourceByteCount;
        ProgramTokenCount = programTokenCount;
        OverflowReasons = overflowReasons;
        Identity = ShaderProgramIdentity.CreateSksl(source, bindings, budget);
    }

    public string Source => Identity.Source;

    public int FirstStageIndex { get; }

    public int StageCount { get; }

    public ImmutableArray<SkslMergedBindingLayout> Bindings => Identity.Bindings;

    public SkslBackendBudget Budget => Identity.Budget;

    public ShaderProgramIdentity Identity { get; }

    public int UniformVectorCount { get; }

    public int SamplerCount { get; }

    public int ChildCount { get; }

    public int SourceByteCount { get; }

    public int ProgramTokenCount { get; }

    public ImmutableArray<SkslBackendLimit> OverflowReasons { get; }

    public bool RequiresStandaloneExecution => !OverflowReasons.IsEmpty;

}
