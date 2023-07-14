using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public abstract class BrushDrawNode : DrawNode
{
    protected BrushDrawNode(IBrush? fill, IPen? pen, Rect bounds)
        : base(bounds)
    {
        Fill = fill;
        Pen = pen;
    }

    public IBrush? Fill { get; }

    public IPen? Pen { get; }
}
