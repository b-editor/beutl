using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class ColorAdjustOperation : BitmapEffectOperation<ColorAdjust>
{
    public static readonly CoreProperty<short> RedProperty;
    public static readonly CoreProperty<short> GreenProperty;
    public static readonly CoreProperty<short> BlueProperty;

    static ColorAdjustOperation()
    {
        RedProperty = ConfigureProperty<short, ColorAdjustOperation>(nameof(Red))
            .Accessor(o => o.Red, (o, v) => o.Red = v)
            .Animatable()
            .EnableEditor()
            .JsonName("red")
            .Header("RedString")
            .Register();

        GreenProperty = ConfigureProperty<short, ColorAdjustOperation>(nameof(Green))
            .Accessor(o => o.Green, (o, v) => o.Green = v)
            .Animatable()
            .EnableEditor()
            .JsonName("green")
            .Header("GreenString")
            .Register();

        BlueProperty = ConfigureProperty<short, ColorAdjustOperation>(nameof(Blue))
            .Accessor(o => o.Blue, (o, v) => o.Blue = v)
            .Animatable()
            .EnableEditor()
            .JsonName("blue")
            .Header("BlueString")
            .Register();
    }

    public short Red
    {
        get => Effect.Red;
        set => Effect.Red = value;
    }

    public short Green
    {
        get => Effect.Green;
        set => Effect.Green = value;
    }

    public short Blue
    {
        get => Effect.Blue;
        set => Effect.Blue = value;
    }

    public override ColorAdjust Effect { get; } = new();
}
