using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class MakeTransparentOperation : BitmapEffectOperation<MakeTransparent>
{
    public static readonly CoreProperty<float> OpacityProperty;

    static MakeTransparentOperation()
    {
        OpacityProperty = ConfigureProperty<float, MakeTransparentOperation>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .Animatable(true)
            .EnableEditor()
            .Maximum(100)
            .Minimum(0)
            .DefaultValue(100)
            .Header("OpacityString")
            .JsonName("opacity")
            .Register();
    }

    public float Opacity
    {
        get => Effect.Opacity * 100;
        set => Effect.Opacity = value / 100;
    }

    public override MakeTransparent Effect { get; } = new();
}
