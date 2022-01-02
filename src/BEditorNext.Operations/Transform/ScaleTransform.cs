using BEditorNext.Graphics.Transformation;

namespace BEditorNext.Operations.Transform;

public sealed class ScaleTransform : TransformOperation
{
    public static readonly PropertyDefine<float> ScaleProperty;
    public static readonly PropertyDefine<float> ScaleXProperty;
    public static readonly PropertyDefine<float> ScaleYProperty;
    private readonly Graphics.Transformation.ScaleTransform _transform = new();

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

    public override ITransform Transform => _transform;
}
