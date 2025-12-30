using System.Collections.Immutable;
using System.Text.Json.Serialization;

using Beutl.Converters;

using SkiaSharp;

namespace Beutl.Graphics;

[JsonConverter(typeof(CurveMapJsonConverter))]
public sealed class CurveMap : IEquatable<CurveMap>
{
    public static readonly CurveMap Default = new([new(0, 0), new(1, 1)]);

    public CurveMap(IEnumerable<CurveControlPoint> points)
    {
        Points = Normalize(points);
    }

    public ImmutableArray<CurveControlPoint> Points { get; }

    public CurveMap WithPoints(IEnumerable<CurveControlPoint> points)
    {
        return new CurveMap(points);
    }

    public float Evaluate(float t)
    {
        if (Points.Length == 0)
            return t;

        if (t <= 0)
            return Points[0].Point.Y;

        if (t >= 1)
            return Points[^1].Point.Y;

        for (int i = 1; i < Points.Length; i++)
        {
            CurveControlPoint prev = Points[i - 1];
            CurveControlPoint next = Points[i];

            if (t <= next.Point.X)
            {
                // Normalize t to the segment range [0, 1]
                float segmentT = (t - prev.Point.X) / (next.Point.X - prev.Point.X);

                // Use Bezier interpolation if handles are present
                if (prev.HasHandles || next.HasHandles)
                {
                    return EvaluateCubicBezier(prev, next, segmentT);
                }
                else
                {
                    // Linear interpolation
                    return prev.Point.Y + (next.Point.Y - prev.Point.Y) * segmentT;
                }
            }
        }

        return Points[^1].Point.Y;
    }

    private static float EvaluateCubicBezier(CurveControlPoint p0, CurveControlPoint p1, float t)
    {
        // Control points for cubic Bezier:
        // P0 = start point
        // P1 = start point + right handle
        // P2 = end point + left handle
        // P3 = end point

        Point startPoint = p0.Point;
        Point controlPoint1 = p0.AbsoluteRightHandle;
        Point controlPoint2 = p1.AbsoluteLeftHandle;
        Point endPoint = p1.Point;

        // For X, we need to find the parameter u such that X(u) = t
        // Since we normalized t to the segment, we need to find u for the Bezier
        float u = FindBezierParameterForX(
            startPoint.X, controlPoint1.X, controlPoint2.X, endPoint.X,
            startPoint.X + t * (endPoint.X - startPoint.X));

        // Then evaluate Y at that parameter
        return CubicBezier(u, startPoint.Y, controlPoint1.Y, controlPoint2.Y, endPoint.Y);
    }

    private static float FindBezierParameterForX(float x0, float x1, float x2, float x3, float targetX)
    {
        // Binary search to find u where X(u) = targetX
        float low = 0f;
        float high = 1f;
        float mid = 0.5f;

        for (int i = 0; i < 20; i++)
        {
            mid = (low + high) * 0.5f;
            float x = CubicBezier(mid, x0, x1, x2, x3);

            if (Math.Abs(x - targetX) < 1e-6f)
                break;

            if (x < targetX)
                low = mid;
            else
                high = mid;
        }

        return mid;
    }

    private static float CubicBezier(float t, float p0, float p1, float p2, float p3)
    {
        float u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    public SKShader ToShader()
    {
        var data = new byte[256];
        for (int i = 0; i < data.Length; i++)
        {
            float v = Evaluate(i / 255f);
            data[i] = (byte)Math.Clamp((int)(v * 255f), 0, 255);
        }

        var info = new SKImageInfo(256, 1, SKColorType.Alpha8, SKAlphaType.Unpremul);
        using SKData pixels = SKData.CreateCopy(data);
        using SKImage image = SKImage.FromPixels(info, pixels, info.RowBytes);
        return SKShader.CreateImage(
            image,
            SKShaderTileMode.Clamp,
            SKShaderTileMode.Clamp,
            SKSamplingOptions.Default,
            SKMatrix.CreateScale(1 / 256f, 1));
    }

    public bool Equals(CurveMap? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || other.Points.Length != Points.Length) return false;

        for (int i = 0; i < Points.Length; i++)
        {
            if (Points[i] != other.Points[i])
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as CurveMap);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (CurveControlPoint p in Points)
        {
            hash.Add(p);
        }

        return hash.ToHashCode();
    }

    private static ImmutableArray<CurveControlPoint> Normalize(IEnumerable<CurveControlPoint> points)
    {
        return points.OrderBy(p => p.Point.X).ToImmutableArray();
    }
}
