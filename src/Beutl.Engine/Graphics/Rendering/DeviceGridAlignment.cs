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
    /// <returns>
    /// <see langword="false"/> when the canvas transform is singular. The whole surface then collapses
    /// onto a point, so no logical region has a preimage of non-zero area and there is nothing to sample.
    /// </returns>
    public static bool TryResolveSurfaceToLogical(ImmediateCanvas canvas, out Matrix surfaceToLogical)
        => canvas.Transform.TryInvert(out surfaceToLogical);

    /// <summary>
    /// Surface pixels per unit of <paramref name="canvas"/>'s current logical space.
    /// <see cref="ImmediateCanvas.Density"/> counts pixels per unit of the canvas's own base space, so a
    /// scaling transform pushed on top of it leaves the two apart: reading the surface back at
    /// <see cref="ImmediateCanvas.Density"/> under a 2x transform would allocate half the target's supply.
    /// </summary>
    public static float ResolveLocalDensity(ImmediateCanvas canvas)
    {
        Matrix transform = canvas.Transform;
        if (transform.M13 != 0 || transform.M23 != 0 || transform.M33 != 1)
        {
            throw new NotSupportedException(
                "PreserveTargetSupply cannot represent the position-dependent density of a perspective transform.");
        }

        // The operator norm is the only scalar affine supply that stays lossless under shear as well as
        // anisotropic scale. A maximum basis-vector length can still underestimate an oblique direction.
        double a = transform.M11;
        double b = transform.M12;
        double c = transform.M21;
        double d = transform.M22;
        double squaredFrobenius = (a * a) + (b * b) + (c * c) + (d * d);
        double determinant = (a * d) - (b * c);
        double discriminant = Math.Max(
            0d,
            (squaredFrobenius * squaredFrobenius) - (4d * determinant * determinant));
        double largestEigenvalue = (squaredFrobenius + Math.Sqrt(discriminant)) / 2d;
        float density = (float)Math.Sqrt(largestEigenvalue);
        return float.IsFinite(density) && density > 0f ? density : canvas.Density;
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
