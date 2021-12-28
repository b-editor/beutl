using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations.BitmapEffect;

public abstract class BitmapEffectOperation<T> : RenderOperation
    where T : Graphics.Effects.BitmapEffect
{
    public abstract T Effect { get; }

    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            IRenderable item = args.List[i];
            if (item is IRenderableBitmap bmp)
            {
                bmp.Effects.Add(Effect);
            }
        }
    }
}
