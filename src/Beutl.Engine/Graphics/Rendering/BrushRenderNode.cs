using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public abstract class BrushRenderNode : RenderNode
{
    protected BrushRenderNode(Brush.Resource? fill, Pen.Resource? pen)
    {
        Fill = fill.Capture();
        Pen = pen.Capture();
    }

    // めも: Drawable.Resourceで一括でVersionを管理するのは危険
    // Transform (changed) -> Brush (changed) -> Pen (changed) だとすると、Transformのバージョンしか変わらない
    public (Brush.Resource Resource, int Version)? Fill { get; private set; }

    public (Pen.Resource Resource, int Version)? Pen { get; private set; }

    protected bool Update(Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = false;
        if (Fill?.Resource.GetOriginal() != fill?.GetOriginal()
            || Fill?.Version != fill?.Version)
        {
            Fill = fill.Capture();
            changed = true;
        }

        if (Pen?.Resource.GetOriginal() != pen?.GetOriginal()
            || Pen?.Version != pen?.Version)
        {
            Pen = pen.Capture();
            changed = true;
        }

        return changed;
    }
}
