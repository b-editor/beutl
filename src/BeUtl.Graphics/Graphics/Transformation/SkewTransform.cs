namespace BeUtl.Graphics.Transformation;

public sealed class SkewTransform : Transform
{
    private float _skewY;
    private float _skewX;

    public SkewTransform()
    {
    }

    public SkewTransform(float skewX, float skewY)
    {
        SkewX = skewX;
        SkewY = skewY;
    }

    public float SkewX
    {
        get => _skewX;
        set => SetProperty(ref _skewX, value);
    }

    public float SkewY
    {
        get => _skewY;
        set => SetProperty(ref _skewY, value);
    }

    public override Matrix Value => Matrix.CreateSkew(SkewX, SkewY);

    public static SkewTransform FromDegree(float skewX, float skewY)
    {
        const float radToDeg = 180.0f / MathF.PI;
        return new SkewTransform(skewX * radToDeg, skewY * radToDeg);
    }
}
