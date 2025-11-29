using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class ClearRenderNode(Color color) : RenderNode
{
    public Color Color { get; private set; } = color;

    public bool Update(Color color)
    {
        if (Color != color)
        {
            Color = color;
            HasChanges = true;
            return true;
        }
        return false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return [RenderNodeOperation.CreateLambda(Rect.Empty, canvas => canvas.Clear(Color))];
    }
}
