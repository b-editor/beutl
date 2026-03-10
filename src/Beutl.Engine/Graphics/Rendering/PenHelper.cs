using Beutl.Media;
using Beutl.Utilities;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal static class PenHelper
{
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

    public static void ConfigureStrokePaint(
        Pen.Resource pen,
        SKPaint paint, Size size)
    {
        float thickness = pen.Thickness;
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

    internal static SKPath? CreateOffsetPath(SKPath fillPath, Pen.Resource pen, Rect bounds)
    {
        if (pen.Offset == 0)
            return null;

        var offsetPath = new SKPath();
        using var offsetPaint = new SKPaint
        {
            IsStroke = true,
            StrokeWidth = Math.Abs(pen.Offset) * 2,
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

    public static SKPath CreateStrokePath(SKPath fillPath, Pen.Resource pen, Rect bounds)
    {
        SKPath? offsetFillPath = CreateOffsetPath(fillPath, pen, bounds);
        if (offsetFillPath != null)
            fillPath = offsetFillPath;

        var strokePath = new SKPath();

        using (var paint = new SKPaint())
        {
            ConfigureStrokePaint(pen, paint, bounds.Size);

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
