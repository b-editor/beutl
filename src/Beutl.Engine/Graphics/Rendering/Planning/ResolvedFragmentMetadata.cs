namespace Beutl.Graphics.Rendering;

internal readonly record struct ResolvedFragmentMetadata(
    Rect Bounds,
    Rect QueryBounds,
    EffectiveScale EffectiveScale);
