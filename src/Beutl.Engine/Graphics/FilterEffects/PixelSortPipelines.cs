namespace Beutl.Graphics.Effects;

internal readonly record struct PixelSortPipelines<TPipeline>(
    TPipeline Prepare,
    TPipeline Rank,
    TPipeline Gather)
    where TPipeline : class;
