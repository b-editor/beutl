using Beutl.Graphics;
using Beutl.Media;
using Beutl.Utilities;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

/// <summary>
/// A verbatim copy of the geometry path construction as it stood at 989856e8d, when
/// <c>Geometry.Resource.GetCachedPath</c> dispatched through <c>GetOriginal().ApplyTo(context, this)</c>.
/// </summary>
/// <remarks>
/// The copy is the comparison baseline for <see cref="GeometryPathParityTests"/>: dispatch moved onto the
/// resource, and every value the old engine-object overrides read already came from the resource, so the two
/// must agree element for element. Edit this only to correct a transcription error — it is not a second
/// implementation to keep in step with the shipped one.
/// </remarks>
internal static class PreChangeGeometryPath
{
    public static GeometryContext Build(Geometry.Resource resource)
    {
        var context = new GeometryContext { FillType = resource.FillType };
        ApplyGeometry(context, resource);
        if (resource.Transform != null)
        {
            context.Transform(resource.Transform.Matrix);
        }

        return context;
    }

    private static void ApplyGeometry(IGeometryContext context, Geometry.Resource resource)
    {
        switch (resource)
        {
            case EllipseGeometry.Resource r:
                ApplyEllipse(context, r);
                break;
            case RectGeometry.Resource r:
                ApplyRect(context, r);
                break;
            case RoundedRectGeometry.Resource r:
                ApplyRoundedRect(context, r);
                break;
            case PathGeometry.Resource r:
                ApplyPathGeometry(context, r);
                break;
            default:
                throw new NotSupportedException($"{resource.GetType()} has no transcribed pre-change path.");
        }
    }

    private static void ApplyEllipse(IGeometryContext context, EllipseGeometry.Resource r)
    {
        float width = r.Width;
        float height = r.Height;
        if (float.IsInfinity(width))
            width = 0;

        if (float.IsInfinity(height))
            height = 0;

        float radiusX = width / 2;
        float radiusY = height / 2;
        var radius = new Size(radiusX, radiusY);

        context.MoveTo(new Point(radiusX, 0));
        context.ArcTo(radius, 0, true, false, new Point(radiusX, height));
        context.ArcTo(radius, 0, true, false, new Point(radiusX, 0));
        context.Close();
    }

    private static void ApplyRect(IGeometryContext context, RectGeometry.Resource r)
    {
        float width = r.Width;
        float height = r.Height;
        if (float.IsInfinity(width))
            width = 0;

        if (float.IsInfinity(height))
            height = 0;

        context.MoveTo(new Point(0, 0));
        context.LineTo(new Point(width, 0));
        context.LineTo(new Point(width, height));
        context.LineTo(new Point(0, height));
        context.LineTo(new Point(0, 0));
        context.Close();
    }

    private static void ApplyPathGeometry(IGeometryContext context, PathGeometry.Resource r)
    {
        foreach (PathFigure.Resource item in r.Figures)
        {
            ApplyFigure(context, item);
        }
    }

    private static void ApplyFigure(IGeometryContext context, PathFigure.Resource resource)
    {
        bool skipFirst = false;
        if (!resource.StartPoint.IsInvalid)
        {
            context.MoveTo(resource.StartPoint);
        }
        else if (resource.Segments.Count > 0)
        {
            if (resource.IsClosed)
            {
                var endPoint = resource.Segments[^1].GetEndPoint();
                if (endPoint.HasValue)
                {
                    context.MoveTo(endPoint.Value);
                }
            }
            else
            {
                var endPoint = resource.Segments[0].GetEndPoint();
                if (endPoint.HasValue)
                {
                    context.MoveTo(endPoint.Value);
                    skipFirst = true;
                }
            }
        }

        foreach (PathSegment.Resource item in resource.Segments)
        {
            if (skipFirst)
            {
                skipFirst = false;
                continue;
            }

            ApplySegment(context, item);
        }

        if (resource.IsClosed)
            context.Close();
    }

    private static void ApplySegment(IGeometryContext context, PathSegment.Resource resource)
    {
        switch (resource)
        {
            case LineSegment.Resource r:
                context.LineTo(r.Point);
                break;
            case QuadraticBezierSegment.Resource r:
                context.QuadraticTo(r.ControlPoint, r.EndPoint);
                break;
            case CubicBezierSegment.Resource r:
                context.CubicTo(r.ControlPoint1, r.ControlPoint2, r.EndPoint);
                break;
            case ConicSegment.Resource r:
                context.ConicTo(r.ControlPoint, r.EndPoint, r.Weight);
                break;
            case ArcSegment.Resource r:
                context.ArcTo(r.Radius, r.RotationAngle, r.IsLargeArc, r.SweepClockwise, r.Point);
                break;
            default:
                throw new NotSupportedException($"{resource.GetType()} has no transcribed pre-change path.");
        }
    }

    private static void ApplyRoundedRect(IGeometryContext context, RoundedRectGeometry.Resource r)
    {
        float width = r.Width;
        float height = r.Height;
        if (float.IsInfinity(width))
            width = 0;

        if (float.IsInfinity(height))
            height = 0;

        (float radiusX, float radiusY) = (width / 2, height / 2);
        float maxRadius = Math.Max(radiusX, radiusY);
        CornerRadius cornerRadius = r.CornerRadius;
        float topLeft = Math.Clamp(cornerRadius.TopLeft, 0, maxRadius);
        float topRight = Math.Clamp(cornerRadius.TopRight, 0, maxRadius);
        float bottomRight = Math.Clamp(cornerRadius.BottomRight, 0, maxRadius);
        float bottomLeft = Math.Clamp(cornerRadius.BottomLeft, 0, maxRadius);
        float smoothing = r.Smoothing / 100;

        ApplyTopRightCorner(width, height, topRight, smoothing, context);
        ApplyBottomRightCorner(width, height, bottomRight, smoothing, context);
        ApplyBottomLeftCorner(width, height, bottomLeft, smoothing, context);
        ApplyTopLeftCorner(width, height, topLeft, smoothing, context);
    }

    // https://github.com/yjb94/react-native-squircle-skia
    private static void GetPathParams(
        float width, float height, float cornerRadius, float smoothing,
        out float a, out float b, out float c, out float d, out float p, out float circularSectionLength)
    {
        float maxRadius = MathF.Min(width, height) / 2;
        cornerRadius = MathF.Min(cornerRadius, maxRadius);

        p = MathF.Min((1 + smoothing) * cornerRadius, maxRadius);

        float angleAlpha;
        float angleBeta;

        if (cornerRadius <= maxRadius / 2)
        {
            angleBeta = 90 * (1 - smoothing);
            angleAlpha = 45 * smoothing;
        }
        else
        {
            float diffRatio = (cornerRadius - maxRadius / 2) / (maxRadius / 2);

            angleBeta = 90 * (1 - smoothing * (1 - diffRatio));
            angleAlpha = 45 * smoothing * (1 - diffRatio);
        }

        float angleTheta = (90 - angleBeta) / 2;
        float p3ToP4Distance = cornerRadius * MathF.Tan(MathUtilities.Deg2Rad(angleTheta / 2));

        circularSectionLength = MathF.Sin(MathUtilities.Deg2Rad(angleBeta / 2)) * cornerRadius * MathF.Sqrt(2);

        c = p3ToP4Distance * MathF.Cos(MathUtilities.Deg2Rad(angleAlpha));
        d = c * MathF.Tan(MathUtilities.Deg2Rad(angleAlpha));
        b = (p - circularSectionLength - c - d) / 3;
        a = 2 * b;
    }

    private static void ApplyTopRightCorner(float width, float height,
        float cornerRadius, float smoothing, IGeometryContext context)
    {
        if (cornerRadius != 0)
        {
            GetPathParams(
                width, height, cornerRadius, smoothing,
                out float a, out float b, out float c, out float d, out float p, out float circularSectionLength);

            context.MoveTo(new Point(MathF.Max(width / 2, width - p), 0));
            context.CubicTo(
                new Point(width - (p - a), 0),
                new Point(width - (p - a - b), 0),
                new Point(width - (p - a - b - c), d));
            context.ArcTo(
                new Size(cornerRadius, cornerRadius),
                0,
                false,
                true,
                new Point(circularSectionLength, circularSectionLength) + context.LastPoint);
            context.CubicTo(
                new Point(width, p - a - b),
                new Point(width, p - a),
                new Point(width, MathF.Min(height / 2, p)));
        }
        else
        {
            context.MoveTo(new Point(width / 2, 0));
            context.LineTo(new Point(width, 0));
            context.LineTo(new Point(width, height / 2));
        }
    }

    private static void ApplyBottomRightCorner(float width, float height,
        float cornerRadius, float smoothing, IGeometryContext context)
    {
        if (cornerRadius != 0)
        {
            GetPathParams(
                width, height, cornerRadius, smoothing,
                out float a, out float b, out float c, out float d, out float p, out float circularSectionLength);

            context.LineTo(new Point(width, MathF.Max(height / 2, height - p)));
            context.CubicTo(
                new Point(width, height - (p - a)),
                new Point(width, height - (p - a - b)),
                new Point(width - d, height - (p - a - b - c)));
            context.ArcTo(
                new Size(cornerRadius, cornerRadius),
                0,
                false,
                true,
                new Point(-circularSectionLength, circularSectionLength) + context.LastPoint);
            context.CubicTo(
                new Point(width - (p - a - b), height),
                new Point(width - (p - a), height),
                new Point(MathF.Max(width / 2, width - p), height));
        }
        else
        {
            context.LineTo(new Point(width, height));
            context.LineTo(new Point(width / 2, height));
        }
    }

    private static void ApplyBottomLeftCorner(float width, float height,
        float cornerRadius, float smoothing, IGeometryContext context)
    {
        if (cornerRadius != 0)
        {
            GetPathParams(
                width, height, cornerRadius, smoothing,
                out float a, out float b, out float c, out float d, out float p, out float circularSectionLength);

            context.LineTo(new Point(MathF.Min(width / 2, p), height));
            context.CubicTo(
                new Point(p - a, height),
                new Point(p - a - b, height),
                new Point(p - a - b - c, height - d));
            context.ArcTo(
                new Size(cornerRadius, cornerRadius),
                0,
                false,
                true,
                new Point(-circularSectionLength, -circularSectionLength) + context.LastPoint);
            context.CubicTo(
                new Point(0, height - (p - a - b)),
                new Point(0, height - (p - a)),
                new Point(0, MathF.Max(height / 2, height - p)));
        }
        else
        {
            context.LineTo(new Point(0, height));
            context.LineTo(new Point(0, height / 2));
        }
    }

    private static void ApplyTopLeftCorner(float width, float height,
        float cornerRadius, float smoothing, IGeometryContext context)
    {
        if (cornerRadius != 0)
        {
            GetPathParams(
                width, height, cornerRadius, smoothing,
                out float a, out float b, out float c, out float d, out float p, out float circularSectionLength);

            context.LineTo(new Point(0, MathF.Min(height / 2, p)));
            context.CubicTo(
                new Point(0, p - a),
                new Point(0, p - a - b),
                new Point(d, p - a - b - c));
            context.ArcTo(
                new Size(cornerRadius, cornerRadius),
                0,
                false,
                true,
                new Point(circularSectionLength, -circularSectionLength) + context.LastPoint);
            context.CubicTo(
                new Point(p - a - b, 0),
                new Point(p - a, 0),
                new Point(MathF.Min(width / 2, p), 0));
        }
        else
        {
            context.LineTo(new Point(0, 0));
        }

        context.Close();
    }

    public static IReadOnlyList<string> Describe(SKPath path)
    {
        var elements = new List<string>
        {
            $"fillType={path.FillType}",
            $"points={path.PointCount}",
            $"verbs={path.VerbCount}",
            $"tightBounds={path.TightBounds}",
            $"bounds={path.Bounds}",
            $"svg={path.ToSvgPathData()}",
        };

        using SKPath.RawIterator iterator = path.CreateRawIterator();
        Span<SKPoint> points = stackalloc SKPoint[4];
        SKPathVerb verb;
        int index = 0;
        do
        {
            verb = iterator.Next(points);
            elements.Add(verb switch
            {
                SKPathVerb.Move => $"[{index}] Move {points[0]}",
                SKPathVerb.Line => $"[{index}] Line {points[0]} {points[1]}",
                SKPathVerb.Quad => $"[{index}] Quad {points[0]} {points[1]} {points[2]}",
                SKPathVerb.Conic =>
                    $"[{index}] Conic {points[0]} {points[1]} {points[2]} w={iterator.ConicWeight()}",
                SKPathVerb.Cubic => $"[{index}] Cubic {points[0]} {points[1]} {points[2]} {points[3]}",
                SKPathVerb.Close => $"[{index}] Close",
                _ => $"[{index}] Done",
            });
            index++;
        } while (verb != SKPathVerb.Done);

        return elements;
    }
}
