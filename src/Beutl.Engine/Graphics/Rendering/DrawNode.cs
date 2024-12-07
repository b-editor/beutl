using System.Diagnostics;

namespace Beutl.Graphics.Rendering;

[Obsolete]
public abstract class DrawNode : IGraphicNode
{
    public DrawNode(Rect bounds)
    {
        bounds = bounds.Normalize();

        Bounds = bounds;
    }

    ~DrawNode()
    {
        Debug.WriteLine("GC発生");
        if (!IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public Rect Bounds { get; }

    public bool IsDisposed { get; private set; }

    public abstract void Render(ImmediateCanvas canvas);

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
    }

    public abstract bool HitTest(Point point);
}
