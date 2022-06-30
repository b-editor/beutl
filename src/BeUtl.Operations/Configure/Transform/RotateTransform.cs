using BeUtl.Graphics.Transformation;

namespace BeUtl.Operations.Configure.Transform;

public sealed class RotateTransform : TransformOperation
{
    public static readonly CoreProperty<float> RotationProperty;
    private readonly RotationTransform _transform = new();

    static RotateTransform()
    {
        RotationProperty = ConfigureProperty<float, RotateTransform>(nameof(Rotation))
            .Accessor(o => o.Rotation, (o, v) => o.Rotation = v)
            .OverrideMetadata(DefaultMetadatas.Rotation)
            .Register();
    }

    public float Rotation
    {
        get => _transform.Rotation;
        set => _transform.Rotation = value;
    }

    public override Graphics.Transformation.Transform Transform => _transform;
}
