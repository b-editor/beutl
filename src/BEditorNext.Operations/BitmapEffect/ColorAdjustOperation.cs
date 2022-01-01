using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class ColorAdjustOperation : BitmapEffectOperation<ColorAdjust>
{
    public static readonly PropertyDefine<short> RedProperty;
    public static readonly PropertyDefine<short> GreenProperty;
    public static readonly PropertyDefine<short> BlueProperty;

    static ColorAdjustOperation()
    {
        RedProperty = RegisterProperty<short, ColorAdjustOperation>(nameof(Red), (o, v) => o.Red = v, o => o.Red)
            .Animatable()
            .EnableEditor()
            .JsonName("red")
            .Header("RedString");

        GreenProperty = RegisterProperty<short, ColorAdjustOperation>(nameof(Green), (o, v) => o.Green = v, o => o.Green)
            .Animatable()
            .EnableEditor()
            .JsonName("green")
            .Header("GreenString");

        BlueProperty = RegisterProperty<short, ColorAdjustOperation>(nameof(Blue), (o, v) => o.Blue = v, o => o.Blue)
            .Animatable()
            .EnableEditor()
            .JsonName("blue")
            .Header("BlueString");
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
