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
}
