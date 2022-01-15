namespace BEditorNext.Operations.Transform;

public sealed class SkewTransform : TransformOperation
{
    public static readonly CoreProperty<float> SkewXProperty;
    public static readonly CoreProperty<float> SkewYProperty;
    private readonly Graphics.Transformation.SkewTransform _transform = new();

    static SkewTransform()
    {
        SkewXProperty = ConfigureProperty<float, SkewTransform>(nameof(SkewX))
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .Accessor(owner => owner.SkewX, (owner, obj) => owner.SkewX = obj)
            .JsonName("skewX")
            .Register();

        SkewYProperty = ConfigureProperty<float, SkewTransform>(nameof(SkewY))
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .JsonName("skewY")
            .Accessor(owner => owner.SkewY, (owner, obj) => owner.SkewY = obj)
            .Register();
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

    public override Graphics.Transformation.Transform Transform => _transform;
}
