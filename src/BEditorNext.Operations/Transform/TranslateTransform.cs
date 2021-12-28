using System.Numerics;

namespace BEditorNext.Operations.Transform;

public sealed class TranslateTransform : TransformOperation
{
    public static readonly PropertyDefine<float> XProperty;
    public static readonly PropertyDefine<float> YProperty;

    static TranslateTransform()
    {
        XProperty = RegisterProperty<float, TranslateTransform>(nameof(X), (owner, obj) => owner.X = obj, owner => owner.X)
            .DefaultValue(0)
            .EnableEditor()
            .Animatable()
            .Header("XString")
            .JsonName("x");

        YProperty = RegisterProperty<float, TranslateTransform>(nameof(Y), (owner, obj) => owner.Y = obj, owner => owner.Y)
            .DefaultValue(0)
            .EnableEditor()
            .Animatable()
            .Header("YString")
            .JsonName("y");
    }

    public float X { get; set; }

    public float Y { get; set; }

    public override Matrix3x2 GetMatrix()
    {
        return Matrix3x2.CreateTranslation(X, Y);
    }
}
