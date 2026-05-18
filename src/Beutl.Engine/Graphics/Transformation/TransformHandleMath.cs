namespace Beutl.Graphics.Transformation;

/// <summary>
/// Pure math helper used by the transform handle operations.
/// Assumes the canonical [T, R, S] layout (application order S → R → T, with T outermost), and computes
/// against the following structure of <see cref="Drawable.GetTransformMatrix"/>:
/// <code>screen = ((local - origin) * S_op * R_op) + T_op + origin + pt_alignment</code>
/// </summary>
internal static class TransformHandleMath
{
    /// <summary>
    /// Normalizes an angle delta (in radians) into the range <c>[-π, π]</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Math.Atan2"/> is discontinuous on the negative X axis, so when a drag crosses
    /// to the left side of the pivot the delta (current - start) jumps by roughly ±2π. This trims it
    /// down to the shortest-rotation form.
    /// Inputs are assumed to be finite (callers must filter).
    /// </remarks>
    public static double NormalizeAngleDelta(double deltaRad)
    {
        return Math.IEEERemainder(deltaRad, 2 * Math.PI);
    }

    /// <summary>
    /// Computes the ΔT to add to the operative Translate so that the anchor (opposite corner) stays
    /// fixed on screen.
    /// </summary>
    /// <remarks>
    /// <para>Derivation (canonical [T, R, S] layout, application order S → R → T):</para>
    /// <code>
    /// screen_anchor = (anchor - origin) * S_op * R_op + T_op + origin + pt_alignment
    /// </code>
    /// <para>Condition for keeping screen_anchor identical between old and new:</para>
    /// <code>
    /// ΔT = ((anchor - origin) * (S_old - S_new) / 100) * R_op
    /// </code>
    /// <para>Scale is expressed on a 100 basis. This function is specific to the new [T, R, S] layout (T outermost).</para>
    /// </remarks>
    /// <param name="rotation">Operative R matrix (only the 2x2 part is used).</param>
    public static (float dx, float dy) ComputePivotTranslationDelta(
        float startScaleX,
        float startScaleY,
        float newScaleX,
        float newScaleY,
        double anchorX,
        double anchorY,
        double originX,
        double originY,
        Matrix rotation)
    {
        double preRx = (anchorX - originX) * (startScaleX - newScaleX) / 100.0;
        double preRy = (anchorY - originY) * (startScaleY - newScaleY) / 100.0;

        float dx = (float)(preRx * rotation.M11 + preRy * rotation.M21);
        float dy = (float)(preRx * rotation.M12 + preRy * rotation.M22);
        return (dx, dy);
    }

    /// <summary>
    /// When the rendered bounds are translated relative to the transform-derived AABB (e.g. via a FilterEffect),
    /// computes the correction to post-translate onto userMatrix.
    /// Returns the matrix unchanged when the center difference satisfies <c>|dx|, |dy| &lt; 0.5px</c> (absorbs numerical noise).
    /// </summary>
    public static Matrix AlignUserMatrixToRenderedBounds(Matrix userMatrix, Size localSize, Rect renderedBounds)
    {
        Point transformCenter = new Rect(localSize).TransformToAABB(userMatrix).Center;
        Point renderedCenter = renderedBounds.Center;
        float dx = renderedCenter.X - transformCenter.X;
        float dy = renderedCenter.Y - transformCenter.Y;

        const float Eps = 0.5f;
        if (MathF.Abs(dx) < Eps && MathF.Abs(dy) < Eps)
        {
            return userMatrix;
        }
        return userMatrix * Matrix.CreateTranslation(dx, dy);
    }

    /// <summary>
    /// Aspect-ratio lock used while Shift is held: snaps |rx|, |ry| to the smaller magnitude while
    /// keeping each axis' sign.
    /// </summary>
    public static (double rx, double ry) LockAspect(double rx, double ry)
    {
        double signX = rx < 0 ? -1.0 : 1.0;
        double signY = ry < 0 ? -1.0 : 1.0;
        double mag = Math.Min(Math.Abs(rx), Math.Abs(ry));
        return (signX * mag, signY * mag);
    }
}
