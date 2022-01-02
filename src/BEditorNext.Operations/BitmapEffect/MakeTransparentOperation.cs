using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class MakeTransparentOperation: BitmapEffectOperation<MakeTransparent>
{
    public static readonly PropertyDefine<float> OpacityProperty;

    static MakeTransparentOperation()
    {
        OpacityProperty = RegisterProperty<float, MakeTransparentOperation>(nameof(Opacity), (owner, obj) => owner.Opacity = obj, owner => owner.Opacity)
            .Animatable(true)
            .EnableEditor()
            .Maximum(100)
            .Minimum(0)
            .DefaultValue(100)
            .Header("OpacityString")
            .JsonName("opacity");
    }

    public float Opacity
    {
        get => Effect.Opacity * 100;
        set => Effect.Opacity = value / 100;
    }

    public override MakeTransparent Effect { get; } = new();
}
