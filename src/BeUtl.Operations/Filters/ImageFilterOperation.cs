using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;

namespace BeUtl.Operations.Filters;

public abstract class ImageFilterOperation<T> : LayerOperation
    where T : Graphics.Filters.ImageFilter
{
    public abstract T Filter { get; }

    public override void ApplySetters(in OperationRenderArgs args)
    {
        Filter.IsEnabled = IsEnabled;
        base.ApplySetters(args);
    }

    protected override void BeginningRenderCore(ILayerScope scope)
    {
        scope.First<IDrawable>()?.Filters.Add(Filter);
    }

    protected override void EndingRenderCore(ILayerScope scope)
    {
        Filter.Parent?.Filters.Remove(Filter);
    }
}
