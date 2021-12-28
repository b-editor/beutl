using System.Numerics;

namespace BEditorNext.Operations.Transform;

public sealed class SkewTransform : TransformOperation
{
    public static readonly PropertyDefine<float> SkewXProperty;
    public static readonly PropertyDefine<float> SkewYProperty;

    static SkewTransform()
    {
        SkewXProperty = RegisterProperty<float, SkewTransform>(nameof(SkewX), (owner, obj) => owner.SkewX = obj, owner => owner.SkewX)
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .JsonName("skewX");

        SkewYProperty = RegisterProperty<float, SkewTransform>(nameof(SkewY), (owner, obj) => owner.SkewY = obj, owner => owner.SkewY)
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .JsonName("skewY");
    }

    public float SkewX { get; set; }

    public float SkewY { get; set; }

    public override Matrix3x2 GetMatrix()
    {
        return Matrix3x2.CreateSkew(MathHelper.ToRadians(SkewX), MathHelper.ToRadians(SkewY));
    }
}
