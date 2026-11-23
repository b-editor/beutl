using Beutl.Animation.Easings;
using Beutl.Engine.Expressions;

namespace Beutl.Animation;

// An animation that extends each of its keyframe tracks past a boundary for LoopMode.Continue.
internal interface ILoopContinuation<T>
{
    T Continue(ExpressionContext boundary, MediaTime time, bool loopOut);
}

internal static class KeyFrameContinuation
{
    private const float EasingStep = 1e-3f;

    // Easing takes a float progress, so slopes this small are rounding noise; treat them as a flat ending.
    private const double FlatSlope = 1e-3;

    // How much the secant must grow as its step halves to read as a jump; a jump doubles it.
    private const double JumpRatio = 1.8;

    // Extends the track along its boundary segment, at the easing's slope at the boundary. A track whose animator
    // is linear extends with exact, saturating arithmetic on the keys' values. Other tracks hand the segment's
    // animator a progress past the segment, so its own semantics apply (Color blends in linear light, a separated
    // component follows its unit schedule) and the value is rounded only once; samples close to the boundary would
    // round integer values to the same one. A track holds its boundary value without a key at the boundary, without
    // a segment beside it, or where it jumps at the boundary, since a jump has no velocity to carry on.
    public static T Continue<T>(KeyFrameAnimation<T> animation, T held, MediaTime time, MediaTime boundary, bool loopOut)
    {
        IListProperty<IKeyFrame> keys = animation.KeyFrames;
        MediaTime local = animation.ToLocal(boundary);
        int first = FindFirst(keys, local);
        int last = FindLast(keys, local);
        // Keys sharing the boundary time: the outermost one gives the value, and the innermost one ends the segment.
        (int outer, int inner, int neighbor) = loopOut ? (last, first, first - 1) : (first, last, last + 1);
        if (first < 0 || neighbor < 0 || neighbor >= keys.Count
            || keys[outer] is not KeyFrame<T> outerKey || keys[inner] is not KeyFrame<T> edge
            || keys[neighbor] is not KeyFrame<T> other)
        {
            return held;
        }

        // The key's own value, as the animator's sample of it may be rounded (PixelPoint interpolates in float).
        T value = outerKey.Value.Value ?? held;
        if (!EqualityComparer<T>.Default.Equals(value, edge.Value.Value!))
            return value;

        // The segment's easing belongs to its later key.
        double slope = BoundarySlope((loopOut ? edge : other).Easing.Value, loopOut);
        if (slope == 0)
            return value;

        long segment = Math.Abs((edge.KeyTime.Value - other.KeyTime.Value).Flicks);
        double elapsed = slope * Math.Abs((time - boundary).Flicks) / segment;
        if (animation.HierarchicalParent is not IComponentInterpolator<T> && ComponentAnimations.InterpolatesLinearly<T>())
            return ComponentAnimations.AddScaledDelta(value, other.Value.Value!, value, elapsed);

        (KeyFrame<T> from, KeyFrame<T> to) = loopOut ? (other, edge) : (edge, other);
        return animation.InterpolateValues((float)(loopOut ? 1 + elapsed : -elapsed),
            from.Value.Value!, to.Value.Value!, from.KeyTime.Value, to.KeyTime.Value, local);
    }

    // The easing's slope at the end (or start) of its segment, or zero when it is flat or jumps there.
    private static double BoundarySlope(Easing easing, bool end)
    {
        float boundary = end ? 1 : 0;
        double Slope(float step)
        {
            float inside = end ? 1 - step : step;
            // Divide by the step float progress actually takes, so a linear segment has a slope of exactly 1.
            return (easing.Ease(boundary) - easing.Ease(inside)) / (boundary - (double)inside);
        }

        double near = Slope(EasingStep);
        double far = Slope(2 * EasingStep);
        // Combining both steps cancels the secant's first-order error, so an ease-out whose slope vanishes at the
        // boundary (quadratic and steeper) reads as flat rather than slowly drifting.
        double slope = 2 * near - far;
        if (Math.Abs(slope) < FlatSlope)
            return 0;

        // A jump at the boundary (HoldEasing, a step-like spline) doubles the secant as the step halves. It is not
        // a velocity to carry on, so the track holds. A steep but continuous curve grows less than that.
        return Math.Abs(near) > JumpRatio * Math.Abs(far) ? 0 : slope;
    }

    private static int FindLast(IListProperty<IKeyFrame> keys, MediaTime time)
    {
        for (int i = keys.Count - 1; i >= 0; i--)
        {
            if (keys[i].KeyTime.Value == time)
                return i;
        }

        return -1;
    }

    private static int FindFirst(IListProperty<IKeyFrame> keys, MediaTime time)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].KeyTime.Value == time)
                return i;
        }

        return -1;
    }
}
