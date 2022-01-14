using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations.Filters;

public abstract class ImageFilterOperation<T> : LayerOperation
    where T : Graphics.Filters.ImageFilter
{
    public abstract T Filter { get; }

    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            IRenderable item = args.List[i];
            if (item is IDrawable bmp)
            {
                bmp.Filters.Add(Filter);
            }
        }
    }
}
