using Beutl.Media;
using Beutl.Utilities;

using SkiaSharp;

using PathOptions = (float Thickness, Beutl.Media.StrokeAlignment Alignment, Beutl.Media.StrokeCap StrokeCap, Beutl.Media.StrokeJoin StrokeJoin, float MiterLimit);

namespace Beutl.Graphics.Rendering;

internal static class PenHelper
{
    public static Rect GetBounds(Rect rect, IPen? pen)
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

    public static Rect CalculateBoundsWithStrokeCap(Rect rect, IPen? pen)
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
        IPen pen,
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

            var pe = SKPathEffect.CreateDash(dashesArray, offset);

            paint.PathEffect = pe;
        }
    }

    public static SKPath CreateStrokePath(SKPath fillPath, IPen pen, Rect bounds)
    {
        var strokePath = new SKPath();

        using (var paint = new SKPaint())
        {
            ConfigureStrokePaint(pen, paint, bounds.Size);

            void CreateStrokePath(SKPath strokePath, SKPaint paint)
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

            switch (pen.StrokeAlignment)
            {
                case StrokeAlignment.Center:
                    CreateStrokePath(strokePath, paint);
                    break;

                case StrokeAlignment.Outside:
                    CreateStrokePath(strokePath, paint);

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

        return strokePath;
    }
}
