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
            .OverrideMetadata(DefaultMetadatas.X)
            .Register();

        YProperty = ConfigureProperty<float, TranslateTransform>(nameof(Y))
            .Accessor(o => o.Y, (o, v) => o.Y = v)
            .OverrideMetadata(DefaultMetadatas.Y)
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
