namespace Beutl.Graphics.Rendering;

public interface IGraphicNode : INode
{
    Rect Bounds { get; }

    bool HitTest(Point point);

    void Render(ImmediateCanvas canvas);
}
