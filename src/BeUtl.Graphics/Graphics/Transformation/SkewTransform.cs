namespace BeUtl.Graphics.Transformation;

public sealed class SkewTransform : ITransform
{
    public SkewTransform()
    {
    }

    public SkewTransform(float skewX, float skewY)
    {
        SkewX = skewX;
        SkewY = skewY;
    }

    public float SkewX { get; set; }

    public float SkewY { get; set; }

    public Matrix Value => Matrix.CreateSkew(SkewX, SkewY);

    public static SkewTransform FromDegree(float skewX, float skewY)
    {
        const float radToDeg = 180.0f / MathF.PI;
        return new SkewTransform(skewX * radToDeg, skewY * radToDeg);
    }
}
