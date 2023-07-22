using Beutl.Media;
using Beutl.Media.Immutable;

namespace Beutl.Graphics.Rendering;

public class DrawableNode : ContainerNode
{
    public DrawableNode(Drawable drawable)
    {
        Drawable = drawable;
    }

    public Drawable Drawable { get; }

    public override void Render(ImmediateCanvas canvas)
    {
        base.Render(canvas);
        Rect bounds = Bounds.Inflate(10);
        canvas.DrawRectangle(bounds, null, new ImmutablePen(Brushes.White, null, 0, 5));
    }
}
