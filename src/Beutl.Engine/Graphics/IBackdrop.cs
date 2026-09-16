using Beutl.Media;

namespace Beutl.Graphics;

/// <summary>How a pushed matrix composes with the transform already in effect.</summary>
/// <remarks>
/// Every operator composes against the scene's own transform and leaves the target's device grid alone, so a
/// push means the same thing whatever density the frame is rendered at.
/// </remarks>
public enum TransformOperator
{
    /// <summary>Applies the matrix before the transform already in effect, in the content's own space.</summary>
    Prepend,

    /// <summary>Applies the matrix after the transform already in effect, in the space that transform maps onto.</summary>
    Append,

    /// <summary>Replaces the transform already in effect, placing the content in the scene's root space.</summary>
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
