using Beutl.Media;

namespace Beutl.Graphics.Rendering.V2;

public abstract class BrushRenderNode : RenderNode
{
    protected BrushRenderNode(IBrush? fill, IPen? pen)
    {
        Fill = (fill as IMutableBrush)?.ToImmutable() ?? fill;
        Pen = (pen as IMutablePen)?.ToImmutable() ?? pen;
    }

    public IBrush? Fill { get; }

    public IPen? Pen { get; }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        (Fill as IDisposable)?.Dispose();
    }
}
