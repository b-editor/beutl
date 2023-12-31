using Beutl.Media;
using Beutl.Media.Immutable;

namespace Beutl.Graphics.Rendering;

public class DrawableNode(Drawable drawable) : ContainerNode
{
    public Drawable Drawable { get; private set; } = drawable;

    public override void Render(ImmediateCanvas canvas)
    {
        base.Render(canvas);
        //Rect bounds = Bounds.Inflate(5);
        //canvas.DrawRectangle(bounds, null, new ImmutablePen(Brushes.White, null, 0, 5));
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Drawable = null!;
    }
}
