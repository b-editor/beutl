
using BEditorNext.Graphics.Effects;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class ChromaKeyOperation : RenderOperation
{
    public static readonly PropertyDefine<Color> ColorProperty;
    public static readonly PropertyDefine<int> SaturationRangeProperty;
    public static readonly PropertyDefine<int> HueRangeProperty;
    private readonly ChromaKey _effect = new();

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
        get => _effect.Color;
        set => _effect.Color = value;
    }
    
    public int SaturationRange
    {
        get => _effect.SaturationRange;
        set => _effect.SaturationRange = value;
    }
    
    public int HueRange
    {
        get => _effect.HueRange;
        set => _effect.HueRange = value;
    }

    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            IRenderable item = args.List[i];
            if (item is IRenderableBitmap bmp)
            {
                bmp.Effects.Add(_effect);
            }
        }
    }
}
