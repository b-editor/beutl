using System.Collections.Immutable;

namespace Beutl.Graphics.Shaders;

internal sealed class SkslMergedProgram
{
    internal SkslMergedProgram(
        string source,
        ImmutableArray<SkslMergedStageLayout> stages,
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
        if (stages.IsDefault)
            throw new ArgumentException("The stage layout array must be initialized.", nameof(stages));
        if (bindings.IsDefault)
            throw new ArgumentException("The binding layout array must be initialized.", nameof(bindings));
        if (overflowReasons.IsDefault)
            throw new ArgumentException("The overflow-reason array must be initialized.", nameof(overflowReasons));

        Source = source;
        Stages = stages;
        Bindings = bindings;
        Budget = budget;
        UniformVectorCount = uniformVectorCount;
        SamplerCount = samplerCount;
        ChildCount = childCount;
        SourceByteCount = sourceByteCount;
        ProgramTokenCount = programTokenCount;
        OverflowReasons = overflowReasons;
        Identity = ShaderProgramIdentity.CreateSksl(Source, Bindings, Budget);
        IsPremultipliedCoverageHomogeneous = true;
        foreach (ref readonly SkslMergedStageLayout stage in Stages.AsSpan())
        {
            if (stage.CoverageBehavior != SkslCoverageBehavior.PremultipliedCoverageHomogeneous)
            {
                IsPremultipliedCoverageHomogeneous = false;
                break;
            }
        }
    }

    public string Source { get; }

    public ImmutableArray<SkslMergedStageLayout> Stages { get; }

    public ImmutableArray<SkslMergedBindingLayout> Bindings { get; }

    public SkslBackendBudget Budget { get; }

    public ShaderProgramIdentity Identity { get; }

    public int StageCount => Stages.Length;

    public int UniformVectorCount { get; }

    public int SamplerCount { get; }

    public int ChildCount { get; }

    public int SourceByteCount { get; }

    public int ProgramTokenCount { get; }

    public ImmutableArray<SkslBackendLimit> OverflowReasons { get; }

    public bool RequiresStandaloneExecution => !OverflowReasons.IsEmpty;

    public bool IsPremultipliedCoverageHomogeneous { get; }

    public bool RequiresResolvedCoverage => !IsPremultipliedCoverageHomogeneous;
}
