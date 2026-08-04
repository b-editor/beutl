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

    /// <summary>
    /// Maps <paramref name="canvas"/>'s own surface pixels back into its current logical space.
    /// Unlike <see cref="ResolveLogicalOffset"/>, which selects a grid phase and therefore only
    /// recognizes a translation, this stays exact under any invertible transform.
    /// </summary>
    /// <exception cref="InvalidOperationException">The canvas transform is singular.</exception>
    public static Matrix ResolveSurfaceToLogical(ImmediateCanvas canvas)
    {
        Matrix transform = canvas.Transform;
        if (!transform.TryInvert(out Matrix inverse))
        {
            throw new InvalidOperationException(
                $"The target transform {transform} is singular, so the target surface cannot be mapped "
                + "back into the local logical space. Reading the target back under a zero-scale or "
                + "otherwise degenerate transform is not supported.");
        }

        return inverse;
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
