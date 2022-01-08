using BEditorNext.Graphics.Effects;
using BEditorNext.Media;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class ColorKeyOperation : BitmapEffectOperation<ColorKey>
{
    public static readonly CoreProperty<int> ValueProperty;
    public static readonly CoreProperty<Color> ColorProperty;

    static ColorKeyOperation()
    {
        ValueProperty = ConfigureProperty<int, ColorKeyOperation>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .EnableEditor()
            .Animatable()
            .Header("Value")
            .JsonName("value")
            .Register();

        ColorProperty = ConfigureProperty<Color, ColorKeyOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .EnableEditor()
            .Animatable()
            .Header("ColorString")
            .JsonName("color")
            .Register();
    }

    public int Value
    {
        get => Effect.Value;
        set => Effect.Value = value;
    }

    public Color Color
    {
        get => Effect.Color;
        set => Effect.Color = value;
    }

    public override ColorKey Effect { get; } = new();
}
