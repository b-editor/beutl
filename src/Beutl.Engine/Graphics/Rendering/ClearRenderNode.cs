using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class ClearRenderNode(Color color) : RenderNode
{
    private static readonly TargetCommandDefinition<Color> s_definition =
        TargetCommandDefinition<Color>.Create(
            static (session, state) => session.Canvas.Use(canvas => canvas.Clear(state)),
            TargetRegion.Full,
            Rect.Empty,
            RenderHitTestContract.None);

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

    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.TargetCommand([], s_definition.Call(Color)));
    }
}
