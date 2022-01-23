namespace BeUtl.Graphics.Transformation;

public sealed class ScaleTransform : Transform
{
    public static readonly CoreProperty<float> ScaleProperty;
    public static readonly CoreProperty<float> ScaleXProperty;
    public static readonly CoreProperty<float> ScaleYProperty;
    private float _scale = 1;
    private float _scaleX = 1;
    private float _scaleY = 1;

    static ScaleTransform()
    {
        ScaleProperty = ConfigureProperty<float, ScaleTransform>(nameof(Scale))
            .Accessor(o => o.Scale, (o, v) => o.Scale = v)
            .DefaultValue(1)
            .Register();

        ScaleXProperty = ConfigureProperty<float, ScaleTransform>(nameof(ScaleX))
            .Accessor(o => o.ScaleX, (o, v) => o.ScaleX = v)
            .DefaultValue(1)
            .Register();

        ScaleYProperty = ConfigureProperty<float, ScaleTransform>(nameof(ScaleY))
            .Accessor(o => o.ScaleY, (o, v) => o.ScaleY = v)
            .DefaultValue(1)
            .Register();

        AffectsRender<ScaleTransform>(ScaleProperty, ScaleXProperty, ScaleYProperty);
    }

    public ScaleTransform()
    {
    }

    public ScaleTransform(Vector vector, float scale = 1)
    {
        Scale = scale;
        ScaleX = vector.X;
        ScaleY = vector.Y;
    }

    public ScaleTransform(float x, float y, float scale = 1)
    {
        Scale = scale;
        ScaleX = x;
        ScaleY = y;
    }

    public float Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public float ScaleX
    {
        get => _scaleX;
        set => SetAndRaise(ScaleXProperty, ref _scaleX, value);
    }

    public float ScaleY
    {
        get => _scaleY;
        set => SetAndRaise(ScaleYProperty, ref _scaleY, value);
    }

    public override Matrix Value => Matrix.CreateScale(Scale * ScaleX, Scale * ScaleY);
}
