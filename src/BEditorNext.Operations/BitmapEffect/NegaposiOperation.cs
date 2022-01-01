using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class NegaposiOperation : BitmapEffectOperation<Negaposi>
{
    public static readonly PropertyDefine<byte> RedProperty;
    public static readonly PropertyDefine<byte> GreenProperty;
    public static readonly PropertyDefine<byte> BlueProperty;

    static NegaposiOperation()
    {
        RedProperty = RegisterProperty<byte, NegaposiOperation>(nameof(Red), (o, v) => o.Red = v, o => o.Red)
            .Animatable()
            .EnableEditor()
            .JsonName("red")
            .Header("RedString");

        GreenProperty = RegisterProperty<byte, NegaposiOperation>(nameof(Green), (o, v) => o.Green = v, o => o.Green)
            .Animatable()
            .EnableEditor()
            .JsonName("green")
            .Header("GreenString");

        BlueProperty = RegisterProperty<byte, NegaposiOperation>(nameof(Blue), (o, v) => o.Blue = v, o => o.Blue)
            .Animatable()
            .EnableEditor()
            .JsonName("blue")
            .Header("BlueString");
    }

    public byte Red
    {
        get => Effect.Red;
        set => Effect.Red = value;
    }

    public byte Green
    {
        get => Effect.Green;
        set => Effect.Green = value;
    }

    public byte Blue
    {
        get => Effect.Blue;
        set => Effect.Blue = value;
    }

    public override Negaposi Effect { get; } = new();
}
