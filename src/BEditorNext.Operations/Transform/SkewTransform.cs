using System.Numerics;

namespace BEditorNext.Operations.Transform;

public sealed class SkewTransform : TransformOperation
{
    public static readonly PropertyDefine<float> SkewXProperty;
    public static readonly PropertyDefine<float> SkewYProperty;
    private readonly Graphics.Transformation.SkewTransform _transform = new();

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

    public float SkewX
    {
        get => MathHelper.ToDegrees(_transform.SkewX);
        set => _transform.SkewX = MathHelper.ToRadians(value);
    }

    public float SkewY
    {
        get => MathHelper.ToDegrees(_transform.SkewY);
        set => _transform.SkewY = MathHelper.ToRadians(value);
    }

    public override Graphics.Transformation.ITransform Transform => _transform;
}
