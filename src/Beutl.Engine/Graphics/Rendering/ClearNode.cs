using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class ClearNode(Color color) : DrawNode(Rect.Empty)
{
    public Color Color { get; } = color;

    public bool Equals(Color color)
    {
        return Color == color;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.Clear(Color);
    }

    public override bool HitTest(Point point)
    {
        return false;
    }
}
