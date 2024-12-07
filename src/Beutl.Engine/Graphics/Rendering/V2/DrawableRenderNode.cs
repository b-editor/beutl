namespace Beutl.Graphics.Rendering.V2;

public class DrawableRenderNode(Drawable drawable) : ContainerRenderNode
{
    public Drawable Drawable { get; private set; } = drawable;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Drawable = null!;
    }
}
