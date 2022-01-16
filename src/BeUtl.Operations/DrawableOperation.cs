using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;

namespace BeUtl.Operations;

public abstract class DrawableOperation : LayerOperation
{
    public abstract Drawable Drawable { get; }

    public override void BeginningRender(ILayerScope scope)
    {
        Drawable.InvalidateVisual();
        scope.Append(Drawable);
    }

    public override void EndingRender(ILayerScope scope)
    {
        scope.Invalidate(Drawable);
    }

    public override void Render(in OperationRenderArgs args)
    {
    }
}
