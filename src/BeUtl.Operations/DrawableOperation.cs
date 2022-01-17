using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;

namespace BeUtl.Operations;

public abstract class DrawableOperation : LayerOperation
{
    public abstract Drawable Drawable { get; }

    protected override void BeginningRenderCore(ILayerScope scope)
    {
        Drawable.InvalidateVisual();
        scope.Append(Drawable);
    }

    protected override void EndingRenderCore(ILayerScope scope)
    {
        scope.Invalidate(Drawable);
    }
}
