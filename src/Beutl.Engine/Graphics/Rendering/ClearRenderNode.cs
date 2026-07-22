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

    public override void Process(RenderNodeContext context)
    {
        Color color = Color;
        TargetCommandDescription description = TargetCommandDescription.Create(
            session => session.Canvas.Use(canvas => canvas.Clear(color)),
            TargetRegion.Full,
            Rect.Empty,
            RenderHitTestContract.None,
            TargetAccess.ReadWrite,
            structuralKey: typeof(ClearRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity(color));
        context.Publish(context.TargetCommand([], description));
    }
}
