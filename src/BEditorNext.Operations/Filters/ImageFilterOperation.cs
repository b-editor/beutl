using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations.Filters;

public abstract class ImageFilterOperation<T> : LayerOperation
    where T : Graphics.Filters.ImageFilter
{
    public abstract T Filter { get; }

    public override void BeginningRender(IScopedRenderable scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i].Item is IDrawable obj)
            {
                obj.Filters.Add(Filter);
            }
        }
    }

    public override void EndingRender(IScopedRenderable scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i].Item is IDrawable obj)
            {
                obj.Filters.Remove(Filter);
            }
        }
    }
}
