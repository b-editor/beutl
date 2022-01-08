using BEditorNext.Graphics.Effects;
using BEditorNext.Media;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class ChromaKeyOperation : BitmapEffectOperation<ChromaKey>
{
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<int> SaturationRangeProperty;
    public static readonly CoreProperty<int> HueRangeProperty;

    static ChromaKeyOperation()
    {
        ColorProperty = ConfigureProperty<Color, ChromaKeyOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .Animatable(true)
            .EnableEditor()
            .DefaultValue(Colors.Green)
            .Header("ColorString")
            .JsonName("color")
            .Register();

        SaturationRangeProperty = ConfigureProperty<int, ChromaKeyOperation>(nameof(SaturationRange))
            .Accessor(o => o.SaturationRange, (o, v) => o.SaturationRange = v)
            .Animatable(true)
            .EnableEditor()
            .Header("SaturationRangeString")
            .JsonName("saturationRange")
            .Register();

        HueRangeProperty = ConfigureProperty<int, ChromaKeyOperation>(nameof(HueRange))
            .Accessor(o => o.HueRange, (o, v) => o.HueRange = v)
            .Animatable(true)
            .EnableEditor()
            .Header("HueRangeString")
            .JsonName("hueRange")
            .Register();
    }

    public Color Color
    {
        get => Effect.Color;
        set => Effect.Color = value;
    }

    public int SaturationRange
    {
        get => Effect.SaturationRange;
        set => Effect.SaturationRange = value;
    }

    public int HueRange
    {
        get => Effect.HueRange;
        set => Effect.HueRange = value;
    }

    public override ChromaKey Effect { get; } = new();
}
