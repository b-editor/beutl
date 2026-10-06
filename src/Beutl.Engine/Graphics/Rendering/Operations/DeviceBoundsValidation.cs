using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal static class DeviceBoundsValidation
{
    public static bool MatchesExtent(float rasterExtent, float density, int deviceExtent)
    {
        float reconstructed = rasterExtent * density;
        if (!float.IsFinite(reconstructed))
            return false;

        float expected = deviceExtent;
        float ulp = Math.Max(
            Math.Abs(MathF.BitIncrement(expected) - expected),
            Math.Abs(expected - MathF.BitDecrement(expected)));
        float tolerance = Math.Min(0.75f, Math.Max(0.0001f, ulp * 2f));
        return Math.Abs((double)reconstructed - deviceExtent) <= tolerance;
    }

    /// <summary>Whether <paramref name="deviceBounds"/> reaches every edge of <paramref name="semantic"/>.</summary>
    /// <remarks>
    /// Only the four edges are compared. <see cref="PixelRect.Contains(PixelRect)"/> also requires the
    /// inner rectangle's corners to lie inside, which differs for an inner rectangle of negative size.
    /// </remarks>
    public static bool Covers(PixelRect deviceBounds, PixelRect semantic)
        => deviceBounds.X <= semantic.X
           && deviceBounds.Y <= semantic.Y
           && deviceBounds.Right >= semantic.Right
           && deviceBounds.Bottom >= semantic.Bottom;
}
