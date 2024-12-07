namespace Beutl.Graphics.Rendering;

public class DrawableRenderNode(Drawable drawable) : ContainerRenderNode
{
    public Drawable Drawable { get; private set; } = drawable;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Drawable = null!;
    }
}
