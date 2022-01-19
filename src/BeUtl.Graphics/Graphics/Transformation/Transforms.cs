using BeUtl.Collections;

namespace BeUtl.Graphics.Transformation;

public sealed class Transforms : CoreList<Transform>
{
    private readonly Drawable _drawable;

    public Transforms(Drawable drawable)
    {
        _drawable = drawable;
        Attached = item =>
         {
             _drawable.InvalidateVisual();
             (item as ILogicalElement).NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(_drawable));
         };
        Detached = item =>
        {
            _drawable.InvalidateVisual();
            (item as ILogicalElement).NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(null));
        };
    }

    public Matrix Calculate()
    {
        Matrix value = Matrix.Identity;

        foreach (Transform item in AsSpan())
        {
            value = item.Value * value;
        }

        return value;
    }
}
