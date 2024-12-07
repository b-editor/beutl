namespace Beutl.Graphics.Rendering;

[Obsolete]
public sealed class PushNode : ContainerNode
{
    public PushNode()
    {
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.Push())
        {
            base.Render(canvas);
        }
    }
}
