namespace BEditorNext.Operations.Transform;

public sealed class TranslateTransform : TransformOperation
{
    public static readonly PropertyDefine<float> XProperty;
    public static readonly PropertyDefine<float> YProperty;
    private readonly Graphics.Transformation.TranslateTransform _transform = new();

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

    public float X
    {
        get => _transform.X;
        set => _transform.X = value;
    }

    public float Y
    {
        get => _transform.Y;
        set => _transform.Y = value;
    }

    public override Graphics.Transformation.ITransform Transform => _transform;
}
