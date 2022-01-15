namespace BeUtl.Operations.Transform;

public sealed class TranslateTransform : TransformOperation
{
    public static readonly CoreProperty<float> XProperty;
    public static readonly CoreProperty<float> YProperty;
    private readonly Graphics.Transformation.TranslateTransform _transform = new();

    static TranslateTransform()
    {
        XProperty = ConfigureProperty<float, TranslateTransform>(nameof(X))
            .Accessor(o => o.X, (o, v) => o.X = v)
            .DefaultValue(0)
            .EnableEditor()
            .Animatable()
            .Header("XString")
            .JsonName("x")
            .Register();

        YProperty = ConfigureProperty<float, TranslateTransform>(nameof(Y))
            .Accessor(o => o.Y, (o, v) => o.Y = v)
            .DefaultValue(0)
            .EnableEditor()
            .Animatable()
            .Header("YString")
            .JsonName("y")
            .Register();
    }

    public float X
    {
        get => _transform.X;
        set => _transform.X = value;
    }

    public float Y
    {
        get => _transform.Y;
        set => _transform.Y = value;
    }

    public override Graphics.Transformation.Transform Transform => _transform;
}
