using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public abstract class DrawableOperation : LayerOperation
{
    public abstract Drawable Drawable { get; }

    public override void Render(in OperationRenderArgs args)
    {
        Drawable? drawable = Drawable;

        if (drawable != null)
        {
            args.List.Add(drawable);
        }
    }
}
