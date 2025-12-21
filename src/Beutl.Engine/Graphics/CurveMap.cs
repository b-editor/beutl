using System.Collections.Immutable;
using System.Text.Json.Serialization;

using Beutl.Converters;

using SkiaSharp;

namespace Beutl.Graphics;

[JsonConverter(typeof(CurveMapJsonConverter))]
public sealed class CurveMap : IEquatable<CurveMap>
{
    public static readonly CurveMap Default = new([new(0, 0), new(1, 1)]);

    public CurveMap(IEnumerable<Point> points)
    {
        Points = Normalize(points);
    }

    public ImmutableArray<Point> Points { get; }

    public CurveMap WithPoints(IEnumerable<Point> points)
    {
        return new CurveMap(points);
    }

    public float Evaluate(float t)
    {
        if (Points.Length == 0)
            return t;

        if (t <= 0)
            return Points[0].Y;

        if (t >= 1)
            return Points[^1].Y;

        for (int i = 1; i < Points.Length; i++)
        {
            Point prev = Points[i - 1];
            Point next = Points[i];

            if (t <= next.X)
            {
                float ratio = (t - prev.X) / (next.X - prev.X);
                return prev.Y + (next.Y - prev.Y) * ratio;
            }
        }

        return Points[^1].Y;
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
        foreach (Point p in Points)
        {
            hash.Add(p);
        }

        return hash.ToHashCode();
    }

    private static ImmutableArray<Point> Normalize(IEnumerable<Point> points)
    {
        return points.OrderBy(p => p.X).ToImmutableArray();
    }
}
