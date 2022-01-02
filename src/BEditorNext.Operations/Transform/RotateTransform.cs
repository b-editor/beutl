using BEditorNext.Graphics.Transformation;

namespace BEditorNext.Operations.Transform;

public sealed class RotateTransform : TransformOperation
{
    public static readonly PropertyDefine<float> RotationProperty;
    private readonly RotationTransform _transform = new();

    static RotateTransform()
    {
        RotationProperty = RegisterProperty<float, RotateTransform>(nameof(Rotation), (owner, obj) => owner.Rotation = obj, owner => owner.Rotation)
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .JsonName("rotation");
    }

    public float Rotation
    {
        get => MathHelper.ToDegrees(_transform.Rotation);
        set => _transform.Rotation = MathHelper.ToRadians(value);
    }

    public override ITransform Transform => _transform;
}
