using BEditorNext.Graphics.Transformation;

namespace BEditorNext.Operations.Transform;

public sealed class RotateTransform : TransformOperation
{
    public static readonly CoreProperty<float> RotationProperty;
    private readonly RotationTransform _transform = new();

    static RotateTransform()
    {
        RotationProperty = ConfigureProperty<float, RotateTransform>(nameof(Rotation))
            .Accessor(o => o.Rotation, (o, v) => o.Rotation = v)
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .JsonName("rotation")
            .Register();
    }

    public float Rotation
    {
        get => MathHelper.ToDegrees(_transform.Rotation);
        set => _transform.Rotation = MathHelper.ToRadians(value);
    }

    public override Graphics.Transformation.Transform Transform => _transform;
}
