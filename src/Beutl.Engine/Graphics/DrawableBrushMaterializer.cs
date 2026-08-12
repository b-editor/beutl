using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics;

/// <param name="Image">The rasterized content, <paramref name="ContentBounds"/> sized at the request density.</param>
/// <param name="ContentBounds">
/// The drawable's own logical bounds. A tile brush stretches and tiles against these, so they must
/// describe the content itself rather than the destination the brush was asked to fill.
/// </param>
public readonly record struct MaterializedDrawableBrush(SKImage Image, Rect ContentBounds);

/// <summary>Rasterizes the content of a <see cref="DrawableBrush"/> so a tile shader can sample it.</summary>
/// <param name="brush">The brush whose drawable content to rasterize.</param>
/// <param name="bounds">The logical frame the brush was asked to fill.</param>
/// <param name="scale">Device pixels per logical unit to rasterize at.</param>
/// <returns>The materialized content, or <see langword="null"/> when there is nothing to draw.</returns>
/// <remarks>
/// A <see cref="BrushConstructor"/> built by <see cref="ImmediateCanvas.CreateBrushConstructor"/> inherits the
/// canvas's materializer. One constructed directly has none until the caller supplies it, and a
/// <see cref="DrawableBrush"/> painted without one degrades to transparent.
/// </remarks>
public delegate MaterializedDrawableBrush? DrawableBrushMaterializer(
    DrawableBrush.Resource brush,
    Rect bounds,
    float scale);
