using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Models;

// NOTE: like RenderScale.cs, this lives in Beutl.Editor (not the app exe) so the derivation is
// unit-testable from tests/Beutl.UnitTests; namespace Beutl.Models sits next to its consumers
// FrameCacheManager / FrameCacheOptions.

/// <summary>
/// Derives the reduced frame-cache entry size for the preview player from the on-screen panel size.
/// Pure so the player view model can reapply it to every rebuilt <c>FrameCacheManager</c>
/// (quality switch / Fit resize / frame-size edit), not only the instance current at the last resize.
/// </summary>
public static class PreviewFrameCacheSizing
{
    /// <summary>
    /// Maps the preview panel size (<paramref name="maxFrameSize"/>, physical pixels) and the scene
    /// frame size to the cache entry size. Returns <see langword="null"/> (cache at original size)
    /// when the panel is at least as large as the frame, or when either size is degenerate.
    /// </summary>
    public static PixelSize? DeriveCacheSize(Size maxFrameSize, PixelSize frameSize)
    {
        Size frame = frameSize.ToSize(1);
        float scale = Stretch.Uniform.CalculateScaling(maxFrameSize, frame).X;

        // A panel at least as large as the frame needs no reduced cache. Rejecting scale >= 1 also
        // keeps (int)(1 / scale) from becoming 0, which would make 1 / den +Infinity and
        // PixelSize.FromSize produce an int.MaxValue-sized entry.
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
