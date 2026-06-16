using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// Derives the reduced frame-cache entry size from the on-screen preview panel size.
/// </summary>
public static class PreviewFrameCacheSizing
{
    /// <summary>
    /// Returns the cache entry size, or null if the panel is at least as large as the frame.
    /// </summary>
    public static PixelSize? DeriveCacheSize(Size maxFrameSize, PixelSize frameSize)
    {
        Size frame = frameSize.ToSize(1);
        float scale = Stretch.Uniform.CalculateScaling(maxFrameSize, frame).X;

        // No reduction needed when panel >= frame. Also avoids division by zero.
        if (!(scale > 0f) || scale >= 1f)
        {
            return null;
        }

        int den = (int)(1f / scale);
        if (den % 2 == 1)
        {
            den++;
        }

        return PixelSize.FromSize(frame, 1f / den);
    }
}
