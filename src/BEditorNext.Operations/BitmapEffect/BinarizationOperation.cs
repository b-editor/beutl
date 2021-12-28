using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class BinarizationOperation : BitmapEffectOperation<Binarization>
{
    public static readonly PropertyDefine<byte> ValueProperty;

    static BinarizationOperation()
    {
        ValueProperty = RegisterProperty<byte, BinarizationOperation>(nameof(Value), (owner, obj) => owner.Value = obj, owner => owner.Value)
            .Animatable(true)
            .EnableEditor()
            .DefaultValue(byte.MinValue)
            .JsonName("value");
    }

    public byte Value
    {
        get => Effect.Value;
        set => Effect.Value = value;
    }

    public override Binarization Effect { get; } = new();
}
