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
            MarkChanged();
            return true;
        }
        return false;
    }

    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.TargetCommand(
            [],
            TargetCommandDescription.Create(
                Color,
                static (session, state) => session.Canvas.Use(canvas => canvas.Clear(state)),
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None)));
    }
}
