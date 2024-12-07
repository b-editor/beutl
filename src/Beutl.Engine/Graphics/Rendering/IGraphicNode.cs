namespace Beutl.Graphics.Rendering;

[Obsolete]
public interface IGraphicNode : INode
{
    Rect Bounds { get; }

    bool HitTest(Point point);

    void Render(ImmediateCanvas canvas);
}
