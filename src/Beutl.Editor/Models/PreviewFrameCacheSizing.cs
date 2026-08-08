using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// Derives the reduced frame-cache entry size from the on-screen preview panel size.
/// </summary>
public static class PreviewFrameCacheSizing
{
    /// <summary>
    /// Returns the reduced cache entry size, or null when there is nothing to gain from reducing:
    /// the panel is at least as large as the frame, or the reduction rounds down to 1.
    /// </summary>
    /// <remarks>
    /// The reduction is rounded down so an entry is never coarser than what the panel shows. A
    /// coarser entry would make a cached frame visibly softer than a freshly rendered one, and the
    /// preview alternates between the two as it crosses cache boundaries.
    /// </remarks>
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
        if (den <= 1)
        {
            return null;
        }

        PixelSize reduced = PixelSize.FromSize(frame, 1f / den);
        return new PixelSize(Math.Max(1, reduced.Width), Math.Max(1, reduced.Height));
    }
}
