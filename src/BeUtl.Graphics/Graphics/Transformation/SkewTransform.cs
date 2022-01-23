using BeUtl.Utilities;

namespace BeUtl.Graphics.Transformation;

public sealed class SkewTransform : Transform
{
    public static readonly CoreProperty<float> SkewXProperty;
    public static readonly CoreProperty<float> SkewYProperty;
    private float _skewY;
    private float _skewX;

    static SkewTransform()
    {
        SkewXProperty = ConfigureProperty<float, SkewTransform>(nameof(SkewX))
            .Accessor(o => o.SkewX, (o, v) => o.SkewX = v)
            .DefaultValue(0)
            .Register();

        SkewYProperty = ConfigureProperty<float, SkewTransform>(nameof(SkewY))
            .Accessor(o => o.SkewY, (o, v) => o.SkewY = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<SkewTransform>(SkewXProperty, SkewYProperty);
    }

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
        set => SetAndRaise(SkewXProperty, ref _skewX, value);
    }

    public float SkewY
    {
        get => _skewY;
        set => SetAndRaise(SkewYProperty, ref _skewY, value);
    }

    public override Matrix Value => Matrix.CreateSkew(MathUtilities.ToRadians(_skewX), MathUtilities.ToRadians(_skewY));

    public static SkewTransform FromRadians(float skewX, float skewY)
    {
        return new SkewTransform(MathUtilities.ToDegrees(skewX), MathUtilities.ToDegrees(skewY));
    }
}
