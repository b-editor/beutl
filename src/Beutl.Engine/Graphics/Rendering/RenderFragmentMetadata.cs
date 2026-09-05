namespace Beutl.Graphics.Rendering;

/// <summary>Describes concrete recording-time metadata for a render fragment.</summary>
/// <param name="Bounds">The fragment's conservative logical value or query bounds.</param>
/// <param name="EffectiveScale">The density at which the fragment can supply materializable values.</param>
public readonly record struct RenderFragmentMetadata(Rect Bounds, EffectiveScale EffectiveScale);
