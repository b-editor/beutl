using Beutl.Engine;

namespace Beutl.Graphics.Rendering;

public class DrawableRenderNode(Drawable.Resource drawable) : ContainerRenderNode
{
    public (Drawable.Resource Resource, int Version)? Drawable { get; private set; } = drawable.Capture();

    public bool Update(Drawable.Resource drawable)
    {
        if (Drawable?.Resource.GetOriginal() != drawable?.GetOriginal()
            || Drawable?.Version != drawable?.Version)
        {
            Drawable = drawable.Capture();
            HasChanges = true;
            return true;
        }

        return false;
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Drawable = null!;
    }
}
