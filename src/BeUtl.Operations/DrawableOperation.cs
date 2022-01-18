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

    public override void ApplySetters(in OperationRenderArgs args)
    {
        Drawable.IsVisible = IsEnabled;
        base.ApplySetters(args);
    }

    protected override void EndingRenderCore(ILayerScope scope)
    {
        scope.Invalidate(Drawable);
    }
}
