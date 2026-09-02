namespace Beutl.Graphics.Shaders;

internal sealed record SkslMergedStageLayout(
    int StageIndex,
    string Prefix,
    SkslCoverageBehavior CoverageBehavior);
