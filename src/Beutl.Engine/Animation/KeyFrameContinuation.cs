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

    // Extends the track along the slope its boundary segment has at the boundary key. The keys' own values and the
    // easing's slope give the velocity, since samples of the track may be rounded (integer animators round them).
    // A track without a key at the boundary, or without a segment beside it, holds its value there.
    public static T Continue<T>(KeyFrameAnimation<T> animation, T held, MediaTime time, MediaTime boundary, bool loopOut)
    {
        IListProperty<IKeyFrame> keys = animation.KeyFrames;
        MediaTime local = animation.ToLocal(boundary);
        int first = FindFirst(keys, local);
        int last = FindLast(keys, local);
        // Keys sharing the boundary time make a jump there: the outermost one gives the value, and the segment
        // runs from the innermost one to its neighbor.
        (int outer, int inner, int neighbor) = loopOut ? (last, first, first - 1) : (first, last, last + 1);
        if (first < 0 || neighbor < 0 || neighbor >= keys.Count
            || keys[outer] is not KeyFrame<T> outerKey || keys[inner] is not KeyFrame<T> edge
            || keys[neighbor] is not KeyFrame<T> other)
        {
            return held;
        }

        long segment = Math.Abs((edge.KeyTime.Value - other.KeyTime.Value).Flicks);

        // The segment's easing belongs to its later key.
        Easing easing = (loopOut ? edge : other).Easing.Value;
        // Divide by the step float progress actually takes, so a linear segment has a slope of exactly 1.
        float inside = loopOut ? 1 - EasingStep : EasingStep;
        double slope = loopOut
            ? (easing.Ease(1) - easing.Ease(inside)) / (1 - (double)inside)
            : (easing.Ease(inside) - easing.Ease(0)) / (double)inside;
        if (Math.Abs(slope) < FlatSlope)
            return held;

        double elapsed = (double)Math.Abs((time - boundary).Flicks) / segment;
        return ComponentAnimations.AddScaledDelta(outerKey.Value.Value!, other.Value.Value!, edge.Value.Value!, slope * elapsed);
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
