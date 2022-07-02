namespace BeUtl.Operations.Configure.Transform;

public sealed class ScaleTransform : TransformOperation
{
    public static readonly CoreProperty<float> ScaleProperty;
    public static readonly CoreProperty<float> ScaleXProperty;
    public static readonly CoreProperty<float> ScaleYProperty;
    private readonly Graphics.Transformation.ScaleTransform _transform = new();

    static ScaleTransform()
    {
        ScaleProperty = ConfigureProperty<float, ScaleTransform>(nameof(Scale))
            .OverrideMetadata(DefaultMetadatas.Scale)
            .Accessor(owner => owner.Scale, (owner, obj) => owner.Scale = obj)
            .Register();

        ScaleXProperty = ConfigureProperty<float, ScaleTransform>(nameof(ScaleX))
            .OverrideMetadata(DefaultMetadatas.ScaleX)
            .Accessor(owner => owner.ScaleX, (owner, obj) => owner.ScaleX = obj)
            .Register();

        ScaleYProperty = ConfigureProperty<float, ScaleTransform>(nameof(ScaleY))
            .OverrideMetadata(DefaultMetadatas.ScaleY)
            .Accessor(owner => owner.ScaleY, (owner, obj) => owner.ScaleY = obj)
            .Register();
    }

    public float Scale
    {
        get => _transform.Scale * 100;
        set => _transform.Scale = value / 100;
    }

    public float ScaleX
    {
        get => _transform.ScaleX * 100;
        set => _transform.ScaleX = value / 100;
    }

    public float ScaleY
    {
        get => _transform.ScaleY * 100;
        set => _transform.ScaleY = value / 100;
    }

    public override Graphics.Transformation.Transform Transform => _transform;
}
