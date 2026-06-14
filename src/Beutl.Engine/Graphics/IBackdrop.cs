using Beutl.Media;

namespace Beutl.Graphics;

public enum TransformOperator
{
    Prepend,

    Append,

    Set
}

public interface IBackdrop
{
    void Draw(ImmediateCanvas canvas);
}

internal sealed class TmpBackdrop(Bitmap bitmap, float captureScale) : IBackdrop
{
    public void Draw(ImmediateCanvas canvas)
    {
        // Un-scale by the capture's density, not the replay canvas's density.
        if (captureScale == 1f)
        {
            canvas.DrawBitmap(bitmap, Brushes.Resource.White, null);
        }
        else
        {
            var dest = new Rect(0, 0, bitmap.Width / captureScale, bitmap.Height / captureScale);
            canvas.DrawBitmapScaled(bitmap, dest, Brushes.Resource.White);
        }
    }
}
