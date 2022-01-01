using BEditorNext.Graphics.Effects;
using BEditorNext.Media;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class ColorKeyOperation : BitmapEffectOperation<ColorKey>
{
    public static readonly PropertyDefine<int> ValueProperty;
    public static readonly PropertyDefine<Color> ColorProperty;

    static ColorKeyOperation()
    {
        ValueProperty = RegisterProperty<int, ColorKeyOperation>(nameof(Value), (o, v) => o.Value = v, o => o.Value)
            .EnableEditor()
            .Animatable()
            //.Header()
            .JsonName("value");

        ColorProperty = RegisterProperty<Color, ColorKeyOperation>(nameof(Color), (o, v) => o.Color = v, o => o.Color)
            .EnableEditor()
            .Animatable()
            .Header("ColorString")
            .JsonName("color");
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
