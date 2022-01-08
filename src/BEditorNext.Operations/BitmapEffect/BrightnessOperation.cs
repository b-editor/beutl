using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class BrightnessOperation : BitmapEffectOperation<Brightness>
{
    public static readonly CoreProperty<short> ValueProperty;

    static BrightnessOperation()
    {
        ValueProperty = ConfigureProperty<short, BrightnessOperation>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .Animatable(true)
            .EnableEditor()
            .DefaultValue(0)
            .Header("ValueString")
            .JsonName("value")
            .Register();
    }

    public short Value
    {
        get => Effect.Value;
        set => Effect.Value = value;
    }

    public override Brightness Effect { get; } = new();
}
