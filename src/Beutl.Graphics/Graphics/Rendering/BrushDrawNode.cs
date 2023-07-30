using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public abstract class BrushDrawNode : DrawNode
{
    protected BrushDrawNode(IBrush? fill, IPen? pen, Rect bounds)
        : base(bounds)
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
