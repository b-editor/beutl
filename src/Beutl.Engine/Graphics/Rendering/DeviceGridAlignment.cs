using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal static class DeviceGridAlignment
{
    public static Vector ResolveLogicalOffset(ImmediateCanvas canvas)
    {
        Matrix transform = canvas.Transform;
        float density = canvas.Density;
        // Only translation commutes with every translation-invariant filter. Rotation, scale,
        // skew, and perspective retain the established drawable-local rasterization path.
        if (!HasTranslationOnlyLinearPart(transform, density))
            return default;

        return new Vector(
            (transform.M31 + canvas.DeviceOrigin.X) / density,
            (transform.M32 + canvas.DeviceOrigin.Y) / density);
    }

    public static Vector NormalizePhase(Vector logicalOffset, float density)
    {
        static float Normalize(float offset, float activeDensity)
        {
            float deviceOffset = offset * activeDensity;
            return (deviceOffset - MathF.Floor(deviceOffset)) / activeDensity;
        }

        return new Vector(
            Normalize(logicalOffset.X, density),
            Normalize(logicalOffset.Y, density));
    }

    public static Vector ResolveRasterTranslation(
        PixelRect deviceBounds,
        Vector logicalOffset,
        float density)
    {
        return new Vector(
            ((logicalOffset.X * density) - deviceBounds.X) / density,
            ((logicalOffset.Y * density) - deviceBounds.Y) / density);
    }

    private static bool HasTranslationOnlyLinearPart(Matrix transform, float linearScale)
        => transform.M11 == linearScale
           && transform.M12 == 0
           && transform.M13 == 0
           && transform.M21 == 0
           && transform.M22 == linearScale
           && transform.M23 == 0
           && transform.M33 == 1;
}
