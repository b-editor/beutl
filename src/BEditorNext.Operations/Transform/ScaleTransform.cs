using System.Numerics;

namespace BEditorNext.Operations.Transform;

public sealed class ScaleTransform : TransformOperation
{
    public static readonly PropertyDefine<float> ScaleProperty;
    public static readonly PropertyDefine<float> ScaleXProperty;
    public static readonly PropertyDefine<float> ScaleYProperty;

    static ScaleTransform()
    {
        ScaleProperty = RegisterProperty<float, ScaleTransform>(nameof(Scale), (owner, obj) => owner.Scale = obj, owner => owner.Scale)
            .EnableEditor()
            .Animatable()
            .DefaultValue(100f)
            .JsonName("scale");

        ScaleXProperty = RegisterProperty<float, ScaleTransform>(nameof(ScaleX), (owner, obj) => owner.ScaleX = obj, owner => owner.ScaleX)
            .EnableEditor()
            .Animatable()
            .DefaultValue(100f)
            .JsonName("scaleX");

        ScaleYProperty = RegisterProperty<float, ScaleTransform>(nameof(ScaleY), (owner, obj) => owner.ScaleY = obj, owner => owner.ScaleY)
            .EnableEditor()
            .Animatable()
            .DefaultValue(100f)
            .JsonName("scaleY");
    }

    public float Scale { get; set; }

    public float ScaleX { get; set; }

    public float ScaleY { get; set; }

    public override Matrix3x2 GetMatrix()
    {
        return Matrix3x2.CreateScale(new Vector2(ScaleX, ScaleY) * Scale / 10000);
    }
}
