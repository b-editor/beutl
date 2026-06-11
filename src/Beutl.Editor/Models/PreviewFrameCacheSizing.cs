using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Models;

// NOTE: like RenderScale.cs, this type lives in Beutl.Editor (not the Beutl app exe) so the pure
// derivation is unit-testable from tests/Beutl.UnitTests; the namespace stays Beutl.Models next to
// FrameCacheManager / FrameCacheOptions, which consume the derived size.

/// <summary>
/// Derives the reduced frame-cache entry size for the preview player from the on-screen panel size.
/// Pure function: the player view model reapplies it to every rebuilt <c>FrameCacheManager</c>
/// (quality switch / Fit resize / frame-size edit), not just to the instance that was current when
/// the panel last resized.
/// </summary>
public static class PreviewFrameCacheSizing
{
    /// <summary>
    /// Maps the preview panel size (<paramref name="maxFrameSize"/>, physical pixels) and the scene
    /// frame size to the cache entry size. Returns <see langword="null"/> (= cache at original size)
    /// when the panel is at least as large as the frame, or when either size is degenerate.
    /// </summary>
    public static PixelSize? DeriveCacheSize(Size maxFrameSize, PixelSize frameSize)
    {
        Size frame = frameSize.ToSize(1);
        float scale = Stretch.Uniform.CalculateScaling(maxFrameSize, frame).X;

        // A panel at least as large as the frame needs no reduced cache, so use the original size.
        // Rejecting scale >= 1 here also keeps (int)(1 / scale) from becoming 0, which would blow
        // 1 / den up to +Infinity and make PixelSize.FromSize produce an int.MaxValue-sized entry.
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
