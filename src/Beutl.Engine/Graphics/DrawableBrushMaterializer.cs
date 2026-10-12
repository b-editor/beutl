using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics;

/// <param name="Image">
/// The rasterized content. The requesting <see cref="BrushConstructor"/> takes ownership and disposes it on every
/// path, so a materializer returns an image it does not retain, such as a fresh one per call, and never disposes it.
/// </param>
/// <param name="ContentBounds">
/// The drawable's own logical bounds. A tile brush stretches and tiles against these, so they must
/// describe the content itself rather than the destination the brush was asked to fill.
/// </param>
public readonly record struct MaterializedDrawableBrush(SKImage Image, Rect ContentBounds);

/// <summary>Rasterizes the content of a <see cref="DrawableBrush"/> so a tile shader can sample it.</summary>
/// <param name="brush">The brush whose drawable content to rasterize.</param>
/// <param name="bounds">The logical frame the brush was asked to fill.</param>
/// <param name="scale">Device pixels per logical unit to rasterize at.</param>
/// <returns>
/// The materialized content, or <see langword="null"/> when there is nothing to draw. The caller takes ownership of
/// <see cref="MaterializedDrawableBrush.Image"/> and disposes it once the tile shader is built or the fill fails.
/// </returns>
/// <remarks>See <see cref="ImmediateCanvas.DrawableBrushMaterializer"/> for how a canvas supplies one.</remarks>
public delegate MaterializedDrawableBrush? DrawableBrushMaterializer(
    DrawableBrush.Resource brush,
    Rect bounds,
    float scale);
