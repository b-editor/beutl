namespace Beutl.Graphics.Transformation;

public sealed class TranslateTransform : Transform
{
    public static readonly CoreProperty<float> XProperty;
    public static readonly CoreProperty<float> YProperty;
    private float _y;
    private float _x;

    static TranslateTransform()
    {
        XProperty = ConfigureProperty<float, TranslateTransform>(nameof(X))
            .Accessor(o => o.X, (o, v) => o.X = v)
            .DefaultValue(0)
            .Register();

        YProperty = ConfigureProperty<float, TranslateTransform>(nameof(Y))
            .Accessor(o => o.Y, (o, v) => o.Y = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<TranslateTransform>(XProperty, YProperty);
    }

    public TranslateTransform()
    {
    }

    public TranslateTransform(float x, float y)
    {
        X = x;
        Y = y;
    }

    public TranslateTransform(Vector vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public TranslateTransform(Point point)
    {
        X = point.X;
        Y = point.Y;
    }

    public float X
    {
        get => _x;
        set => SetAndRaise(XProperty, ref _x, value);
    }

    public float Y
    {
        get => _y;
        set => SetAndRaise(YProperty, ref _y, value);
    }

    public override Matrix Value => Matrix.CreateTranslation(X, Y);
}
