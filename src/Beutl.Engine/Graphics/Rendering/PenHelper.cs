using Beutl.Media;
using Beutl.Utilities;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Computes the bounds convention the engine's own stroked render nodes follow.
/// </summary>
/// <remarks>
/// The built-in rectangle, geometry, image, and video render nodes size their declared output through these
/// helpers. A node authored outside the engine that declares its own stroked bounds has to use them too, otherwise
/// its bounds disagree with what the engine measures for the same rectangle and pen, and the surrounding pipeline
/// clips or over-allocates against a footprint the node never meant.
/// </remarks>
public static class PenHelper
{
    /// <summary>Inflates a fill rectangle to cover the stroke a pen paints around it.</summary>
    /// <param name="rect">The rectangle the fill occupies.</param>
    /// <param name="pen">The stroking pen, or <see langword="null"/> for an unstroked shape.</param>
    /// <returns>
    /// <paramref name="rect"/> inflated by the part of the stroke that falls outside the fill, plus the pen's
    /// offset when it is positive; <paramref name="rect"/> unchanged when <paramref name="pen"/> is
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The inflation follows <see cref="Pen.Resource.StrokeAlignment"/>: an inside-aligned stroke stays within the
    /// fill and adds nothing, a centred stroke adds half its thickness, and an outside-aligned stroke adds its full
    /// thickness. This is the value to pass as the output bounds of a painted source, and it deliberately ignores
    /// stroke caps — a shape whose stroke has open ends adds those separately with
    /// <see cref="CalculateBoundsWithStrokeCap(Rect, Pen.Resource)"/>.
    /// </remarks>
    public static Rect GetBounds(Rect rect, Pen.Resource? pen)
    {
        if (pen != null)
        {
            float thickness = pen.Thickness;
            rect = pen.StrokeAlignment switch
            {
                StrokeAlignment.Center => rect.Inflate(thickness / 2),
                StrokeAlignment.Outside => rect.Inflate(thickness),
                _ => rect,
            };

            if (pen.Offset > 0)
            {
                rect = rect.Inflate(pen.Offset);
            }
        }

        return rect;
    }

    /// <summary>Gets how far a stroke of the given thickness reaches outside the shape it outlines.</summary>
    /// <param name="align">Where the stroke sits relative to the fill edge.</param>
    /// <param name="thickness">The pen's thickness.</param>
    /// <returns>
    /// Zero for <see cref="StrokeAlignment.Inside"/>, half of <paramref name="thickness"/> for
    /// <see cref="StrokeAlignment.Center"/>, and all of it for <see cref="StrokeAlignment.Outside"/>.
    /// </returns>
    /// <remarks>
    /// This is the scalar behind <see cref="GetBounds(Rect, Pen.Resource)"/>. Hit testing uses it directly: a node
    /// that decides whether a point lands on its stroke inflates and deflates by this distance, so that its hit
    /// region agrees with the bounds it declared.
    /// </remarks>
    public static float GetRealThickness(StrokeAlignment align, float thickness)
    {
        return align switch
        {
            StrokeAlignment.Inside => 0,
            StrokeAlignment.Center => thickness / 2,
            StrokeAlignment.Outside => thickness,
            _ => 0,
        };
    }

    /// <summary>Inflates bounds to cover the caps a pen paints at the open ends of a stroke.</summary>
    /// <param name="rect">The bounds to extend.</param>
    /// <param name="pen">The stroking pen, or <see langword="null"/> for an unstroked shape.</param>
    /// <returns>
    /// <paramref name="rect"/> unchanged for <see cref="StrokeCap.Flat"/>, for a zero-thickness pen, and for no pen;
    /// inflated by half the thickness for <see cref="StrokeCap.Round"/> and by the full thickness for
    /// <see cref="StrokeCap.Square"/>.
    /// </returns>
    /// <remarks>
    /// Apply this on top of <see cref="GetBounds(Rect, Pen.Resource)"/> only for an open figure, whose stroke has
    /// ends to cap. A closed shape has none, so inflating it here would over-declare its footprint.
    /// </remarks>
    public static Rect CalculateBoundsWithStrokeCap(Rect rect, Pen.Resource? pen)
    {
        if (pen == null || MathUtilities.IsZero(pen.Thickness)) return rect;

        return pen.StrokeCap switch
        {
            StrokeCap.Flat => rect,
            StrokeCap.Round => rect.Inflate(pen.Thickness / 2),
            StrokeCap.Square => rect.Inflate(pen.Thickness),
            _ => rect,
        };
    }

    internal static void ConfigureStrokePaint(
        Pen.Resource pen,
        SKPaint paint, Size size,
        float scale = 1f)
    {
        scale = float.IsFinite(scale) && scale > 0f ? scale : 1f;
        float thickness = pen.Thickness * scale;
        switch (pen.StrokeAlignment)
        {
            case StrokeAlignment.Outside:
                thickness *= 2;
                break;

            case StrokeAlignment.Inside:
                thickness *= 2;
                float maxAspect = Math.Max(size.Width, size.Height);
                thickness = Math.Min(thickness, maxAspect);
                break;

            default:
                break;
        }

        paint.IsStroke = true;
        paint.StrokeWidth = thickness;
        paint.StrokeCap = (SKStrokeCap)pen.StrokeCap;
        paint.StrokeJoin = (SKStrokeJoin)pen.StrokeJoin;
        paint.StrokeMiter = pen.MiterLimit;
        SKPathEffect? dashEffect = null;
        if (pen.DashArray != null && pen.DashArray.Count > 0)
        {
            IReadOnlyList<float> srcDashes = pen.DashArray;

            int count = srcDashes.Count % 2 == 0 ? srcDashes.Count : srcDashes.Count * 2;

            float[] dashesArray = new float[count];

            for (int i = 0; i < count; ++i)
            {
                dashesArray[i] = (float)srcDashes[i % srcDashes.Count] * thickness;
            }

            float offset = (float)((pen.DashOffset / 100f) * thickness);

            dashEffect = SKPathEffect.CreateDash(dashesArray, offset);
        }

        SKPathEffect? trimEffect = CreateTrimEffect(pen);
        paint.PathEffect = CombineEffects(dashEffect, trimEffect);
    }

    internal static SKPathEffect? CreateTrimEffect(Pen.Resource pen)
    {
        if (pen.TrimStart == 0f && pen.TrimEnd == 100f)
            return null;

        float start = ((pen.TrimStart + pen.TrimOffset) % 100f) / 100f;
        float stop = ((pen.TrimEnd + pen.TrimOffset) % 100f) / 100f;
        if (start <= 0) start += 1f;
        if (stop <= 0) stop += 1f;

        return SKPathEffect.CreateTrim(
            Math.Min(start, stop),
            Math.Max(start, stop),
            start <= stop ? SKTrimPathEffectMode.Normal : SKTrimPathEffectMode.Inverted);
    }

    internal static SKPathEffect? CombineEffects(SKPathEffect? outer, SKPathEffect? inner)
    {
        if (outer != null && inner != null)
        {
            var composed = SKPathEffect.CreateCompose(outer, inner);
            outer.Dispose();
            inner.Dispose();
            return composed;
        }
        return outer ?? inner;
    }

    internal static SKPath? CreateOffsetPath(SKPath fillPath, Pen.Resource pen, Rect bounds, float scale = 1f)
    {
        if (pen.Offset == 0)
            return null;

        scale = float.IsFinite(scale) && scale > 0f ? scale : 1f;
        var offsetPath = new SKPath();
        using var offsetPaint = new SKPaint
        {
            IsStroke = true,
            StrokeWidth = Math.Abs(pen.Offset) * 2 * scale,
            StrokeJoin = (SKStrokeJoin)pen.StrokeJoin,
            StrokeCap = (SKStrokeCap)pen.StrokeCap,
            StrokeMiter = pen.MiterLimit,
            Style = SKPaintStyle.Stroke,
        };
        CreateStrokePath(fillPath, offsetPath, offsetPaint, bounds);

        if (pen.Offset > 0)
        {
            using var copy = new SKPath(offsetPath);
            copy.Op(fillPath, SKPathOp.Union, offsetPath);
        }
        else
        {
            using var copy = new SKPath(fillPath);
            copy.Op(offsetPath, SKPathOp.Difference, offsetPath);
        }

        return offsetPath;
    }

    // StrokeWidthが大きすぎる場合、元の内側に空間ができてしまうため、複数回に分けてStrokePathを生成する
    private static void CreateStrokePath(SKPath fillPath, SKPath strokePath, SKPaint paint, Rect bounds)
    {
        float thickness = paint.StrokeWidth;
        float maxAspect = Math.Max(bounds.Width, bounds.Height);
        if (maxAspect < thickness)
        {
            paint.StrokeWidth = maxAspect;
            bool first = true;

            while (maxAspect < thickness)
            {
                using SKPath tmp = paint.GetFillPath(first ? fillPath : strokePath);
                if (tmp == null) break;

                if (!first)
                {
                    using (var copy = new SKPath(strokePath))
                        tmp.Op(copy, SKPathOp.Union, strokePath);
                }
                else
                {
                    strokePath.AddPath(tmp);
                    first = false;
                }

                thickness -= maxAspect;
            }

            if (thickness > 0)
            {
                paint.StrokeWidth = thickness;
                using SKPath tmp2 = paint.GetFillPath(strokePath);
                if (tmp2 != null)
                {
                    using var copy = new SKPath(strokePath);
                    tmp2.Op(copy, SKPathOp.Union, strokePath);
                }
            }
        }
        else
        {
            paint.GetFillPath(fillPath, strokePath);
        }
    }

    internal static SKPath CreateStrokePath(SKPath fillPath, Pen.Resource pen, Rect bounds, float scale = 1f)
    {
        scale = float.IsFinite(scale) && scale > 0f ? scale : 1f;
        SKPath? offsetFillPath = CreateOffsetPath(fillPath, pen, bounds, scale);
        if (offsetFillPath != null)
            fillPath = offsetFillPath;

        var strokePath = new SKPath();

        using (var paint = new SKPaint())
        {
            ConfigureStrokePaint(pen, paint, bounds.Size, scale);

            switch (pen.StrokeAlignment)
            {
                case StrokeAlignment.Center:
                    CreateStrokePath(fillPath, strokePath, paint, bounds);
                    break;

                case StrokeAlignment.Outside:
                    CreateStrokePath(fillPath, strokePath, paint, bounds);

                    using (var strokePathCopy = new SKPath(strokePath))
                    {
                        strokePathCopy.Op(fillPath, SKPathOp.Difference, strokePath);
                    }

                    break;

                case StrokeAlignment.Inside:
                    paint.GetFillPath(fillPath, strokePath);

                    using (var strokePathCopy = new SKPath(strokePath))
                    {
                        strokePathCopy.Op(fillPath, SKPathOp.Intersect, strokePath);
                    }

                    break;
                default:
                    break;
            }
        }

        offsetFillPath?.Dispose();
        return strokePath;
    }
}
