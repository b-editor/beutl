using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.UnitTests.Engine.Graphics.Transformation;

public class TransformHandleMathTests
{
    // ===== ComputePivotTranslationDelta (based on the new [T, R, S] order) =====
    //
    // New formula: ΔT = ((anchor - origin) * (S_old - S_new) / 100) · R
    //   Scale is on a 100 basis (100 = 1x effective); R is the 2x2 rotation matrix.
    // screen_anchor invariant:
    //   screen_anchor = (anchor - origin) * (S/100) · R + T + origin
    //   With T_new = T_old + ΔT, the screen_anchor stays identical between old and new.

    [Test]
    public void ComputePivotTranslationDelta_AnchorAtOrigin_ReturnsZero()
    {
        // When the anchor coincides with the pivot, effD = 0, so ΔT = 0 even if the scale changes.
        var (dx, dy) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX: 100f, startScaleY: 100f,
            newScaleX: 200f, newScaleY: 200f,
            anchorX: 50.0, anchorY: 50.0,
            originX: 50.0, originY: 50.0,
            rotation: Matrix.Identity);

        Assert.Multiple(() =>
        {
            Assert.That(dx, Is.EqualTo(0f));
            Assert.That(dy, Is.EqualTo(0f));
        });
    }

    [Test]
    public void ComputePivotTranslationDelta_DoublesScaleNoRotation_AnchorMovesNegative()
    {
        // Axis-aligned case. With anchor=(100,0), origin=(0,0) and scaleX 100→200:
        // ΔTx = 100 * (100 - 200) / 100 = -100, ΔTy = 0.
        var (dx, dy) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX: 100f, startScaleY: 100f,
            newScaleX: 200f, newScaleY: 100f,
            anchorX: 100.0, anchorY: 0.0,
            originX: 0.0, originY: 0.0,
            rotation: Matrix.Identity);

        Assert.Multiple(() =>
        {
            Assert.That(dx, Is.EqualTo(-100f));
            Assert.That(dy, Is.EqualTo(0f));
        });
    }

    [Test]
    public void ComputePivotTranslationDelta_HalvesScaleNoRotation_AnchorMovesPositive()
    {
        // scaleX 200→100 (2x→1x): ΔTx = 100 * (200 - 100) / 100 = 100.
        var (dx, dy) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX: 200f, startScaleY: 100f,
            newScaleX: 100f, newScaleY: 100f,
            anchorX: 100.0, anchorY: 0.0,
            originX: 0.0, originY: 0.0,
            rotation: Matrix.Identity);

        Assert.Multiple(() =>
        {
            Assert.That(dx, Is.EqualTo(100f));
            Assert.That(dy, Is.EqualTo(0f));
        });
    }

    [Test]
    public void ComputePivotTranslationDelta_AccountsForOriginOffset()
    {
        // The closer the pivot is to the anchor, the smaller effD becomes.
        // effD = 100 - 80 = 20, ΔTx = 20 * (100 - 200) / 100 = -20.
        var (dx, _) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX: 100f, startScaleY: 100f,
            newScaleX: 200f, newScaleY: 100f,
            anchorX: 100.0, anchorY: 0.0,
            originX: 80.0, originY: 0.0,
            rotation: Matrix.Identity);

        Assert.That(dx, Is.EqualTo(-20f));
    }

    [Test]
    public void ComputePivotTranslationDelta_With90DegRotation_RotatesPreRDelta()
    {
        // anchor=(100,0), origin=(0,0), scale 100→200.
        // pre-R: (Δx, Δy) = (100 * -100/100, 0) = (-100, 0)
        // R = 90° rotation: (x,y) → (-y, x) via row-vector right multiplication.
        // Result: (-100, 0) · R(90°) = (0, -100).
        Matrix r90 = Matrix.CreateRotation(System.MathF.PI / 2);
        var (dx, dy) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX: 100f, startScaleY: 100f,
            newScaleX: 200f, newScaleY: 100f,
            anchorX: 100.0, anchorY: 0.0,
            originX: 0.0, originY: 0.0,
            rotation: r90);

        Assert.Multiple(() =>
        {
            Assert.That(dx, Is.EqualTo(0f).Within(1e-4));
            Assert.That(dy, Is.EqualTo(-100f).Within(1e-4));
        });
    }

    [Test]
    public void ComputePivotTranslationDelta_AnchorFixedInScreenSpace_Invariant_NoRotation()
    {
        // screen_anchor invariant: with T_new = T_old + ΔT, screen_anchor matches between old and new.
        //   screen_anchor = (anchor - origin) * (S/100) · R + T + origin
        // Case where R = Identity.
        float startScaleX = 100f, startScaleY = 100f;
        float newScaleX = 300f, newScaleY = 150f;
        double anchorX = 200.0, anchorY = 80.0;
        double originX = 50.0, originY = 20.0;
        float startTransX = 30f, startTransY = 10f;

        var (dx, dy) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX, startScaleY, newScaleX, newScaleY,
            anchorX, anchorY, originX, originY,
            Matrix.Identity);
        float newTransX = startTransX + dx;
        float newTransY = startTransY + dy;

        double screenAnchorOldX = (anchorX - originX) * (startScaleX / 100.0) + startTransX + originX;
        double screenAnchorNewX = (anchorX - originX) * (newScaleX / 100.0) + newTransX + originX;
        double screenAnchorOldY = (anchorY - originY) * (startScaleY / 100.0) + startTransY + originY;
        double screenAnchorNewY = (anchorY - originY) * (newScaleY / 100.0) + newTransY + originY;

        Assert.Multiple(() =>
        {
            Assert.That(screenAnchorNewX, Is.EqualTo(screenAnchorOldX).Within(1e-3));
            Assert.That(screenAnchorNewY, Is.EqualTo(screenAnchorOldY).Within(1e-3));
        });
    }

    [Test]
    public void ComputePivotTranslationDelta_AnchorFixedInScreenSpace_Invariant_WithRotation()
    {
        // screen_anchor invariant when R is 45°.
        // screen_anchor = (anchor - origin) * (S/100) · R + T + origin
        Matrix r = Matrix.CreateRotation(System.MathF.PI / 4);
        float startScaleX = 100f, startScaleY = 100f;
        float newScaleX = 200f, newScaleY = 150f;
        double anchorX = 100.0, anchorY = 60.0;
        double originX = 20.0, originY = 10.0;
        float startTransX = 5f, startTransY = 15f;

        var (dx, dy) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX, startScaleY, newScaleX, newScaleY,
            anchorX, anchorY, originX, originY,
            r);
        float newTransX = startTransX + dx;
        float newTransY = startTransY + dy;

        // Compute screen_anchor (axis-aligned point → R → translate) for both old and new.
        // Per-axis vector of (anchor - origin) * (S/100):
        double oldVx = (anchorX - originX) * (startScaleX / 100.0);
        double oldVy = (anchorY - originY) * (startScaleY / 100.0);
        // · R (row-vector right-multiplication, 2x2 portion)
        double oldRx = oldVx * r.M11 + oldVy * r.M21;
        double oldRy = oldVx * r.M12 + oldVy * r.M22;
        double screenOldX = oldRx + startTransX + originX;
        double screenOldY = oldRy + startTransY + originY;

        double newVx = (anchorX - originX) * (newScaleX / 100.0);
        double newVy = (anchorY - originY) * (newScaleY / 100.0);
        double newRx = newVx * r.M11 + newVy * r.M21;
        double newRy = newVx * r.M12 + newVy * r.M22;
        double screenNewX = newRx + newTransX + originX;
        double screenNewY = newRy + newTransY + originY;

        Assert.Multiple(() =>
        {
            Assert.That(screenNewX, Is.EqualTo(screenOldX).Within(1e-3));
            Assert.That(screenNewY, Is.EqualTo(screenOldY).Within(1e-3));
        });
    }

    [Test]
    public void ComputePivotTranslationDelta_NegativeStartScale_Invariant_NoRotation()
    {
        // Confirm that the anchor stays fixed on screen even with negative (flipped) scales.
        float startScaleX = -100f, startScaleY = -100f;
        float newScaleX = -200f, newScaleY = -100f;
        double anchorX = 200.0, anchorY = 0.0;
        double originX = 50.0, originY = 0.0;
        float startTransX = 30f;

        var (dx, _) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX, startScaleY, newScaleX, newScaleY,
            anchorX, anchorY, originX, originY,
            Matrix.Identity);
        float newTransX = startTransX + dx;

        double screenAnchorOld = (anchorX - originX) * (startScaleX / 100.0) + startTransX + originX;
        double screenAnchorNew = (anchorX - originX) * (newScaleX / 100.0) + newTransX + originX;

        Assert.That(screenAnchorNew, Is.EqualTo(screenAnchorOld).Within(1e-3));
    }

    [Test]
    public void ComputePivotTranslationDelta_SignFlipAcrossZero_Invariant_NoRotation()
    {
        // Case where the scale sign flips while crossing the pivot (100 → -100).
        float startScaleX = 100f, startScaleY = 100f;
        float newScaleX = -100f, newScaleY = 100f;
        double anchorX = 100.0, anchorY = 0.0;
        double originX = 0.0, originY = 0.0;

        var (dx, _) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX, startScaleY, newScaleX, newScaleY,
            anchorX, anchorY, originX, originY,
            Matrix.Identity);
        float newTransX = 0f + dx;

        double screenAnchorOld = (anchorX - originX) * (startScaleX / 100.0) + 0 + originX;
        double screenAnchorNew = (anchorX - originX) * (newScaleX / 100.0) + newTransX + originX;

        Assert.That(screenAnchorNew, Is.EqualTo(screenAnchorOld).Within(1e-3));
    }

    [Test]
    public void ComputePivotTranslationDelta_FiniteAtZeroScale()
    {
        // ΔT remains finite even when newScale = 0, because the new formula does not divide by newScale.
        var (dx, dy) = TransformHandleMath.ComputePivotTranslationDelta(
            startScaleX: 100f, startScaleY: 100f,
            newScaleX: 0f, newScaleY: 0f,
            anchorX: 10.0, anchorY: 10.0,
            originX: 0.0, originY: 0.0,
            rotation: Matrix.Identity);

        Assert.Multiple(() =>
        {
            Assert.That(float.IsFinite(dx), Is.True);
            Assert.That(float.IsFinite(dy), Is.True);
        });
    }

    // ===== NormalizeAngleDelta =====

    [Test]
    public void NormalizeAngleDelta_Zero_ReturnsZero()
    {
        Assert.That(TransformHandleMath.NormalizeAngleDelta(0), Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void NormalizeAngleDelta_SmallValue_PassThrough()
    {
        double d = 0.1;
        Assert.That(TransformHandleMath.NormalizeAngleDelta(d), Is.EqualTo(d).Within(1e-12));
    }

    [Test]
    public void NormalizeAngleDelta_NegativeSmallValue_PassThrough()
    {
        double d = -0.1;
        Assert.That(TransformHandleMath.NormalizeAngleDelta(d), Is.EqualTo(d).Within(1e-12));
    }

    [Test]
    public void NormalizeAngleDelta_BranchCutCrossing_WrapsToShortestRotation()
    {
        // Dragging from +179° to -179° produces a raw delta of -358° (-6.246 rad); normalize it to +2° (+0.0349 rad).
        double startDeg = 179;
        double endDeg = -179;
        double rawDelta = (endDeg - startDeg) * Math.PI / 180.0; // -358°
        double expected = 2 * Math.PI / 180.0;                     //   +2°

        double normalized = TransformHandleMath.NormalizeAngleDelta(rawDelta);

        Assert.That(normalized, Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void NormalizeAngleDelta_BranchCutOppositeDirection_WrapsToShortestRotation()
    {
        // From -179° to +179° the raw delta is +358° → normalizes to -2°.
        double rawDelta = 358 * Math.PI / 180.0;
        double expected = -2 * Math.PI / 180.0;

        double normalized = TransformHandleMath.NormalizeAngleDelta(rawDelta);

        Assert.That(normalized, Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void NormalizeAngleDelta_MultipleRevolutions_WrapsToShortestRotation()
    {
        // Mathematical invariant: |result| <= π. 3π → -π or +π (boundary); -3π → same boundary.
        double result3pi = TransformHandleMath.NormalizeAngleDelta(3 * Math.PI);
        double resultNeg3pi = TransformHandleMath.NormalizeAngleDelta(-3 * Math.PI);

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(result3pi), Is.EqualTo(Math.PI).Within(1e-9));
            Assert.That(Math.Abs(resultNeg3pi), Is.EqualTo(Math.PI).Within(1e-9));
        });
    }

    [Test]
    public void NormalizeAngleDelta_Range_AlwaysWithinPi()
    {
        // Sweep to verify the |result| <= π invariant.
        for (double d = -10 * Math.PI; d <= 10 * Math.PI; d += 0.1)
        {
            double r = TransformHandleMath.NormalizeAngleDelta(d);
            Assert.That(Math.Abs(r), Is.LessThanOrEqualTo(Math.PI + 1e-9),
                $"NormalizeAngleDelta({d}) = {r} should be within [-π, π]");
        }
    }

    [Test]
    public void NormalizeAngleDelta_ExactPi_ReturnsPi()
    {
        // Exact π input. The IEEERemainder-based implementation returns either +π or -π, but |result| = π holds.
        // The |result| == π invariant should be preserved even if the implementation changes in the future.
        double result = TransformHandleMath.NormalizeAngleDelta(Math.PI);
        Assert.That(Math.Abs(result), Is.EqualTo(Math.PI).Within(1e-12));
    }

    [Test]
    public void NormalizeAngleDelta_ExactNegativePi_ReturnsAbsPi()
    {
        double result = TransformHandleMath.NormalizeAngleDelta(-Math.PI);
        Assert.That(Math.Abs(result), Is.EqualTo(Math.PI).Within(1e-12));
    }

    [Test]
    public void NormalizeAngleDelta_NaN_Propagates()
    {
        // Public API contract: callers are expected to filter NaN/Infinity, but the function itself
        // is guaranteed to propagate NaN without throwing (consistent with the upstream caller's double.IsFinite gate).
        Assert.That(double.IsNaN(TransformHandleMath.NormalizeAngleDelta(double.NaN)), Is.True);
    }

    [Test]
    public void NormalizeAngleDelta_Infinity_PropagatesAsNonFinite()
    {
        // Confirm that +∞ / -∞ also fall on the IsFinite-fail side (either NaN or ±∞).
        double posInf = TransformHandleMath.NormalizeAngleDelta(double.PositiveInfinity);
        double negInf = TransformHandleMath.NormalizeAngleDelta(double.NegativeInfinity);
        Assert.Multiple(() =>
        {
            Assert.That(double.IsFinite(posInf), Is.False);
            Assert.That(double.IsFinite(negInf), Is.False);
        });
    }

    // ===== AlignUserMatrixToRenderedBounds =====

    [Test]
    public void AlignUserMatrixToRenderedBounds_IdentityAndMatchingBounds_ReturnsUserMatrix()
    {
        // userMatrix=I, rendered bounds == local rect → centers match, no correction.
        var userMatrix = Matrix.Identity;
        var localSize = new Size(100, 50);
        var bounds = new Rect(0, 0, 100, 50);

        Matrix result = TransformHandleMath.AlignUserMatrixToRenderedBounds(userMatrix, localSize, bounds);

        Assert.That(result, Is.EqualTo(userMatrix));
    }

    [Test]
    public void AlignUserMatrixToRenderedBounds_OffsetEffect_AppliesPostTranslate()
    {
        // userMatrix=I, transform AABB center = (50, 25), rendered bounds center = (60, 30)
        // → simulates a pure translation effect. dx=10, dy=5 are post-translated.
        var userMatrix = Matrix.Identity;
        var localSize = new Size(100, 50);
        var bounds = new Rect(10, 5, 100, 50);

        Matrix result = TransformHandleMath.AlignUserMatrixToRenderedBounds(userMatrix, localSize, bounds);

        // Post-translate maps the origin (0,0) to (10, 5)
        Point origin = result.Transform(new Point(0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(origin.X, Is.EqualTo(10f).Within(1e-3));
            Assert.That(origin.Y, Is.EqualTo(5f).Within(1e-3));
        });
    }

    [Test]
    public void AlignUserMatrixToRenderedBounds_SymmetricBlur_NoChange()
    {
        // userMatrix=I, transform AABB = (0,0)-(100,50), center=(50,25).
        // The bounds expand by 10px on each side to (-10,-10)-(110,60), but center=(50, 25) is unchanged. dx=dy=0 → no correction.
        var userMatrix = Matrix.Identity;
        var localSize = new Size(100, 50);
        var bounds = new Rect(-10, -10, 120, 70);

        Matrix result = TransformHandleMath.AlignUserMatrixToRenderedBounds(userMatrix, localSize, bounds);

        Assert.That(result, Is.EqualTo(userMatrix));
    }

    [Test]
    public void AlignUserMatrixToRenderedBounds_BelowThreshold_ReturnsUserMatrix()
    {
        // When the center difference is below 0.5px (e.g. 0.4px), treat it as numerical noise and do not apply a correction.
        var userMatrix = Matrix.Identity;
        var localSize = new Size(100, 50);
        var bounds = new Rect(0.4f, 0.4f, 100, 50);

        Matrix result = TransformHandleMath.AlignUserMatrixToRenderedBounds(userMatrix, localSize, bounds);

        Assert.That(result, Is.EqualTo(userMatrix));
    }

    [Test]
    public void AlignUserMatrixToRenderedBounds_AtThreshold_AppliesPostTranslate()
    {
        // At dx=0.51px (just over the 0.5 threshold), the correction is applied.
        var userMatrix = Matrix.Identity;
        var localSize = new Size(100, 50);
        var bounds = new Rect(0.51f, 0, 100, 50);

        Matrix result = TransformHandleMath.AlignUserMatrixToRenderedBounds(userMatrix, localSize, bounds);

        Point origin = result.Transform(new Point(0, 0));
        Assert.That(origin.X, Is.EqualTo(0.51f).Within(1e-3));
    }

    [Test]
    public void AlignUserMatrixToRenderedBounds_RotatedMatrix_OffsetComputedFromAabb()
    {
        // The AABB of a 100x100 square rotated by 45° is √2 times the size. When the rendered bounds match that AABB, no correction is applied.
        Matrix userMatrix = Matrix.CreateRotation(System.MathF.PI / 4);
        var localSize = new Size(100, 100);

        // Compute the rotated AABB by rotating the 4 corners (0,0), (100,0), (100,100), (0,100).
        // The center stays at (50, 50), and the AABB is roughly (-35.36, 35.36)-(35.36, 106.07). In practice
        // the center is (35.36, 70.71)... here we assert that "no correction is applied when rendered bounds
        // match the transform AABB".
        System.Span<Point> corners = stackalloc Point[]
        {
            userMatrix.Transform(new Point(0, 0)),
            userMatrix.Transform(new Point(100, 0)),
            userMatrix.Transform(new Point(100, 100)),
            userMatrix.Transform(new Point(0, 100)),
        };
        float minX = corners[0].X, maxX = corners[0].X, minY = corners[0].Y, maxY = corners[0].Y;
        for (int i = 1; i < 4; i++)
        {
            if (corners[i].X < minX) minX = corners[i].X;
            if (corners[i].X > maxX) maxX = corners[i].X;
            if (corners[i].Y < minY) minY = corners[i].Y;
            if (corners[i].Y > maxY) maxY = corners[i].Y;
        }
        var bounds = new Rect(minX, minY, maxX - minX, maxY - minY);

        Matrix result = TransformHandleMath.AlignUserMatrixToRenderedBounds(userMatrix, localSize, bounds);

        Assert.That(result, Is.EqualTo(userMatrix));
    }

    // ===== LockAspect =====

    [Test]
    public void LockAspect_BothPositive_TakesMinMagnitude()
    {
        var (rx, ry) = TransformHandleMath.LockAspect(2.0, 3.0);
        Assert.Multiple(() =>
        {
            Assert.That(rx, Is.EqualTo(2.0));
            Assert.That(ry, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void LockAspect_NegativeX_PreservesSignAndMinMagnitude()
    {
        // |-1.5| < |2.0|, so magnitude = 1.5 with each axis' sign preserved.
        var (rx, ry) = TransformHandleMath.LockAspect(-1.5, 2.0);
        Assert.Multiple(() =>
        {
            Assert.That(rx, Is.EqualTo(-1.5));
            Assert.That(ry, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void LockAspect_BothNegative_PreservesBothFlips()
    {
        var (rx, ry) = TransformHandleMath.LockAspect(-2.0, -3.0);
        Assert.Multiple(() =>
        {
            Assert.That(rx, Is.EqualTo(-2.0));
            Assert.That(ry, Is.EqualTo(-2.0));
        });
    }

    [Test]
    public void LockAspect_ZeroInput_ReturnsZeroMagnitudeForBoth()
    {
        // If one side is 0, magnitude = 0 and the other side also becomes 0 (sign preserved = +).
        var (rx, ry) = TransformHandleMath.LockAspect(0.0, 5.0);
        Assert.Multiple(() =>
        {
            Assert.That(rx, Is.EqualTo(0.0));
            Assert.That(ry, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void LockAspect_NaN_Propagates()
    {
        // Public API contract: with NaN input, do not throw and propagate NaN to both axes
        // (the caller is expected to gate on `IsFinite`; same pattern as NormalizeAngleDelta_NaN_Propagates).
        var (rx, ry) = TransformHandleMath.LockAspect(double.NaN, 2.0);
        Assert.Multiple(() =>
        {
            Assert.That(double.IsNaN(rx), Is.True);
            Assert.That(double.IsNaN(ry), Is.True);
        });
    }
}
