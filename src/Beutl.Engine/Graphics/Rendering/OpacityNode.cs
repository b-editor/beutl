namespace Beutl.Graphics.Rendering;

[Obsolete]
public sealed class OpacityNode(float opacity) : ContainerNode
{
    public float Opacity { get; } = opacity;

    public bool Equals(float opacity)
    {
        return Opacity == opacity;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushOpacity(Opacity))
        {
            base.Render(canvas);
        }
    }
}
