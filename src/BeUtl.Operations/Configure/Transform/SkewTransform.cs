namespace BeUtl.Operations.Configure.Transform;

public sealed class SkewTransform : TransformOperation
{
    public static readonly CoreProperty<float> SkewXProperty;
    public static readonly CoreProperty<float> SkewYProperty;
    private readonly Graphics.Transformation.SkewTransform _transform = new();

    static SkewTransform()
    {
        SkewXProperty = ConfigureProperty<float, SkewTransform>(nameof(SkewX))
            .OverrideMetadata(DefaultMetadatas.SkewX)
            .Accessor(owner => owner.SkewX, (owner, obj) => owner.SkewX = obj)
            .Register();

        SkewYProperty = ConfigureProperty<float, SkewTransform>(nameof(SkewY))
            .OverrideMetadata(DefaultMetadatas.SkewY)
            .Accessor(owner => owner.SkewY, (owner, obj) => owner.SkewY = obj)
            .Register();
    }

    public float SkewX
    {
        get => _transform.SkewX;
        set => _transform.SkewX = value;
    }

    public float SkewY
    {
        get => _transform.SkewY;
        set => _transform.SkewY = value;
    }

    public override Graphics.Transformation.Transform Transform => _transform;
}
