namespace Beutl.Graphics.Effects;

internal sealed record SkslMergedStageLayout(
    int StageIndex,
    string Prefix,
    SkslCoverageBehavior CoverageBehavior);
