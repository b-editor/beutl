namespace Beutl.Graphics.Rendering;

public readonly record struct RenderNodeMeasurement(
    Rect OutputBounds,
    Rect QueryBounds,
    EffectiveScale EffectiveScale,
    RenderValueCardinality ValueCardinality,
    bool HasFragments,
    bool HasContributingValues,
    bool HasTargetEffects);
