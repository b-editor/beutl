namespace Beutl.Graphics.Rendering;

public abstract class DrawNode : IGraphicNode
{
    public DrawNode(Rect bounds)
    {
        bounds = bounds.Normalize();

        Bounds = bounds;
    }

    public Rect Bounds { get; }

    public abstract void Render(ImmediateCanvas canvas);

    public abstract void Dispose();

    public abstract bool HitTest(Point point);
}
