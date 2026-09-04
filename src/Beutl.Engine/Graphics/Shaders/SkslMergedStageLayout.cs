namespace Beutl.Graphics.Shaders;

internal readonly record struct SkslMergedStageLayout(
    int StageIndex,
    string Prefix,
    SkslCoverageBehavior CoverageBehavior);
