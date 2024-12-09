using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class ClearRenderNode(Color color) : RenderNode
{
    public Color Color { get; } = color;

    public bool Equals(Color color)
    {
        return Color == color;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return [RenderNodeOperation.CreateLambda(Rect.Empty, canvas => canvas.Clear(Color))];
    }
}
