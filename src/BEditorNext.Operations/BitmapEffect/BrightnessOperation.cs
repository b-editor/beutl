
using BEditorNext.Graphics.Effects;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class BrightnessOperation : RenderOperation
{
    public static readonly PropertyDefine<short> ValueProperty;
    private readonly Brightness _effect = new();

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
        get => _effect.Value;
        set => _effect.Value = value;
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
