using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class BrightnessOperation : BitmapEffectOperation<Brightness>
{
    public static readonly PropertyDefine<short> ValueProperty;

    static BrightnessOperation()
    {
        ValueProperty = RegisterProperty<short, BrightnessOperation>(nameof(Value), (owner, obj) => owner.Value = obj, owner => owner.Value)
            .Animatable(true)
            .EnableEditor()
            .DefaultValue((short)0)
            .JsonName("value");
    }

    public short Value
    {
        get => Effect.Value;
        set => Effect.Value = value;
    }

    public override Brightness Effect { get; } = new();
}
