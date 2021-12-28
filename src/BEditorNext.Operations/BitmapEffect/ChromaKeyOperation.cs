using BEditorNext.Graphics.Effects;
using BEditorNext.Media;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class ChromaKeyOperation : BitmapEffectOperation<ChromaKey>
{
    public static readonly PropertyDefine<Color> ColorProperty;
    public static readonly PropertyDefine<int> SaturationRangeProperty;
    public static readonly PropertyDefine<int> HueRangeProperty;

    static ChromaKeyOperation()
    {
        ColorProperty = RegisterProperty<Color, ChromaKeyOperation>(nameof(Color), (owner, obj) => owner.Color = obj, owner => owner.Color)
            .Animatable(true)
            .EnableEditor()
            .DefaultValue(Colors.Green)
            .JsonName("color");

        SaturationRangeProperty = RegisterProperty<int, ChromaKeyOperation>(nameof(SaturationRange), (owner, obj) => owner.SaturationRange = obj, owner => owner.SaturationRange)
            .Animatable(true)
            .EnableEditor()
            .JsonName("saturationRange");

        HueRangeProperty = RegisterProperty<int, ChromaKeyOperation>(nameof(HueRange), (owner, obj) => owner.HueRange = obj, owner => owner.HueRange)
            .Animatable(true)
            .EnableEditor()
            .JsonName("hueRange");
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
