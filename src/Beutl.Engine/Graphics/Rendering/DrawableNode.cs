namespace Beutl.Graphics.Rendering;

[Obsolete]
public class DrawableNode(Drawable drawable) : ContainerNode
{
    public Drawable Drawable { get; private set; } = drawable;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Drawable = null!;
    }
}
