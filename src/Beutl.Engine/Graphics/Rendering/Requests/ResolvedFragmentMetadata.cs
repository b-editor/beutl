namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct ResolvedFragmentMetadata(
    Rect Bounds,
    Rect QueryBounds,
    EffectiveScale EffectiveScale);
