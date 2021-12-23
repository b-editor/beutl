using BEditorNext.Graphics.Effects;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class BinarizationOperation : RenderOperation
{
    public static readonly PropertyDefine<byte> ValueProperty;
    private readonly Binarization _effect = new();

    static BinarizationOperation()
    {
        ValueProperty = RegisterProperty<byte, BinarizationOperation>(nameof(Value), (owner, obj) => owner.Value = obj, owner => owner.Value)
            .Animatable(true)
            .EnableEditor()
            .DefaultValue(byte.MinValue)
            .JsonName("value");
    }

    public byte Value
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
