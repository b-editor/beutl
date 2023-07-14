using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class ClearNode : DrawNode
{
    public ClearNode(Color color)
        : base(Rect.Empty)
    {
        Color = color;
    }

    public Color Color { get; }

    public bool Equals(Color color)
    {
        return Color == color;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.Clear(Color);
    }

    public override void Dispose()
    {
    }

    public override bool HitTest(Point point)
    {
        return false;
    }
}
