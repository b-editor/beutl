using BeUtl.Graphics;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public abstract class DrawableOperation : LayerOperation
{
    public abstract Drawable Drawable { get; }

    public override void BeginningRender(IScopedRenderable scope)
    {
        Drawable.InvalidateVisual();
        scope.Append(Drawable);
    }

    public override void EndingRender(IScopedRenderable scope)
    {
        scope.Invalidate(Drawable);
    }

    public override void Render(in OperationRenderArgs args)
    {
    }
}
