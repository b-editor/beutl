namespace Beutl.Graphics.Rendering;

public sealed class BlendModeNode : ContainerNode
{
    public BlendModeNode(BlendMode blendMode)
    {
        BlendMode = blendMode;
    }

    public BlendMode BlendMode { get; }

    public bool Equals(BlendMode blendMode)
    {
        return BlendMode == blendMode;
    }

    public override void Dispose()
    {
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushBlendMode(BlendMode))
        {
            base.Render(canvas);
        }
    }
}
