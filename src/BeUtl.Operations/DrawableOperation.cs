using BeUtl.Graphics;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public abstract class DrawableOperation : LayerOperation
{
    public abstract Drawable Drawable { get; }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        Drawable.IsVisible = IsEnabled;
        args.Result = Drawable;
        //args.Result = IsEnabled ? Drawable : null;
    }
}
