using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class NegaposiOperation : BitmapEffectOperation<Negaposi>
{
    public static readonly CoreProperty<byte> RedProperty;
    public static readonly CoreProperty<byte> GreenProperty;
    public static readonly CoreProperty<byte> BlueProperty;

    static NegaposiOperation()
    {
        RedProperty = ConfigureProperty<byte, NegaposiOperation>(nameof(Red))
            .Accessor(o => o.Red, (o, v) => o.Red = v)
            .Animatable()
            .EnableEditor()
            .JsonName("red")
            .Header("RedString")
            .Register();

        GreenProperty = ConfigureProperty<byte, NegaposiOperation>(nameof(Green))
            .Accessor(o => o.Green, (o, v) => o.Green = v)
            .Animatable()
            .EnableEditor()
            .JsonName("green")
            .Header("GreenString")
            .Register();

        BlueProperty = ConfigureProperty<byte, NegaposiOperation>(nameof(Blue))
            .Accessor(o => o.Blue, (o, v) => o.Blue = v)
            .Animatable()
            .EnableEditor()
            .JsonName("blue")
            .Header("BlueString")
            .Register();
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
