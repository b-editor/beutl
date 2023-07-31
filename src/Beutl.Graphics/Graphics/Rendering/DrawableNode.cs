namespace Beutl.Graphics.Rendering;

public class DrawableNode : ContainerNode
{
    public DrawableNode(Drawable drawable)
    {
        Drawable = drawable;
    }

    public Drawable Drawable { get; private set; }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Drawable = null!;
    }
}
