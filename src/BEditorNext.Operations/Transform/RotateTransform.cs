using System.Numerics;

namespace BEditorNext.Operations.Transform;

public sealed class RotateTransform : TransformOperation
{
    public static readonly PropertyDefine<float> RotationProperty;

    static RotateTransform()
    {
        RotationProperty = RegisterProperty<float, RotateTransform>(nameof(Rotation), (owner, obj) => owner.Rotation = obj, owner => owner.Rotation)
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .JsonName("rotation");
    }

    public float Rotation { get; set; }

    public override Matrix3x2 GetMatrix()
    {
        return Matrix3x2.CreateRotation(Rotation);
    }
}
