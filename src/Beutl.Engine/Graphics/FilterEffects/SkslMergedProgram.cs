using System.Collections.ObjectModel;

namespace Beutl.Graphics.Effects;

internal sealed class SkslMergedProgram
{
    internal SkslMergedProgram(
        string source,
        IReadOnlyList<SkslMergedStageLayout> stages,
        IReadOnlyList<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget,
        int uniformVectorCount,
        int samplerCount,
        int childCount,
        int sourceByteCount,
        int programTokenCount,
        IReadOnlyList<SkslBackendLimit> overflowReasons)
    {
        Source = source;
        Stages = new ReadOnlyCollection<SkslMergedStageLayout>(stages.ToArray());
        Bindings = new ReadOnlyCollection<SkslMergedBindingLayout>(bindings.ToArray());
        Budget = budget;
        UniformVectorCount = uniformVectorCount;
        SamplerCount = samplerCount;
        ChildCount = childCount;
        SourceByteCount = sourceByteCount;
        ProgramTokenCount = programTokenCount;
        OverflowReasons = new ReadOnlyCollection<SkslBackendLimit>(overflowReasons.ToArray());
        Identity = ShaderProgramIdentity.CreateSksl(Source, Bindings, Budget);
        IsPremultipliedCoverageHomogeneous = true;
        for (int index = 0; index < Stages.Count && IsPremultipliedCoverageHomogeneous; index++)
        {
            IsPremultipliedCoverageHomogeneous =
                Stages[index].CoverageBehavior == SkslCoverageBehavior.PremultipliedCoverageHomogeneous;
        }
    }

    public string Source { get; }

    public IReadOnlyList<SkslMergedStageLayout> Stages { get; }

    public IReadOnlyList<SkslMergedBindingLayout> Bindings { get; }

    public SkslBackendBudget Budget { get; }

    public ShaderProgramIdentity Identity { get; }

    public int StageCount => Stages.Count;

    public int UniformVectorCount { get; }

    public int SamplerCount { get; }

    public int ChildCount { get; }

    public int SourceByteCount { get; }

    public int ProgramTokenCount { get; }

    public IReadOnlyList<SkslBackendLimit> OverflowReasons { get; }

    public bool RequiresStandaloneExecution => OverflowReasons.Count != 0;

    public bool IsPremultipliedCoverageHomogeneous { get; }

    public bool RequiresResolvedCoverage => !IsPremultipliedCoverageHomogeneous;
}
