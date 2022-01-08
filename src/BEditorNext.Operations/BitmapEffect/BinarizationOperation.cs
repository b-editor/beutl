using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class BinarizationOperation : BitmapEffectOperation<Binarization>
{
    public static readonly CoreProperty<byte> ValueProperty;

    static BinarizationOperation()
    {
        ValueProperty = ConfigureProperty<byte, BinarizationOperation>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .Animatable(true)
            .EnableEditor()
            .DefaultValue(byte.MinValue)
            .Header("ValueString")
            .JsonName("value")
            .Register();
    }

    public byte Value
    {
        get => Effect.Value;
        set => Effect.Value = value;
    }

    public override Binarization Effect { get; } = new();
}
