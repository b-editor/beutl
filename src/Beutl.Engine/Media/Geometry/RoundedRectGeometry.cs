using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Utilities;

namespace Beutl.Media;

public sealed partial class RoundedRectGeometry : Geometry
{
    public RoundedRectGeometry()
    {
        ScanProperties<RoundedRectGeometry>();
    }

    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> Height { get; } = Property.CreateAnimatable<float>();

    public IProperty<CornerRadius> CornerRadius { get; } = Property.CreateAnimatable<CornerRadius>();

    public IProperty<float> Smoothing { get; } = Property.CreateAnimatable<float>();

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

    private void ApplyTopRightCorner(float width, float height,
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

    private void ApplyBottomRightCorner(float width, float height,
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

    private void ApplyBottomLeftCorner(float width, float height,
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

    private void ApplyTopLeftCorner(float width, float height,
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

    public override void ApplyTo(IGeometryContext context, Geometry.Resource resource)
    {
        base.ApplyTo(context, resource);
        var r = (Resource)resource;
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

        ApplyTopRightCorner(
            width, height, topRight, smoothing, context);
        ApplyBottomRightCorner(
            width, height, bottomRight, smoothing, context);
        ApplyBottomLeftCorner(
            width, height, bottomLeft, smoothing, context);
        ApplyTopLeftCorner(
            width, height, topLeft, smoothing, context);
    }
}
