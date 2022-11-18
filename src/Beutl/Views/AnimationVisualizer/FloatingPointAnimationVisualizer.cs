using System.Numerics;

using Avalonia;
using Avalonia.Collections.Pooled;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public class FloatingPointAnimationVisualizer<T> : NumberAnimationVisualizer<T>
    where T : INumber<T>, IMinMaxValue<T>, IFloatingPoint<T>
{
    public FloatingPointAnimationVisualizer(Animation<T> animation)
        : base(animation)
    {
    }

    protected override void InvalidatePoints(PooledList<Vector2> points)
    {
        points.Clear();
        TimeSpan duration = CalculateDuration();
        (T min, T max) = CalculateRange(duration);
        int div = (int)duration.TotalMilliseconds / 100;
        // EnsureCapacity
        if (points.Capacity < div)
        {
            int newCapacity = points.Capacity == 0 ? 4 : points.Capacity * 2;

            if (newCapacity < div)
                newCapacity = div;

            points.Capacity = newCapacity;
        }

        if (Animation.Children.Count > 0)
        {
            T minAbs = T.Abs(min);
            T maxAbs = T.Abs(max);
            T sum = minAbs + maxAbs;
            for (int i = 0; i <= div; i++)
            {
                float progress = i / (float)div;
                TimeSpan ts = duration * progress;

                T value = (Animation.Interpolate(ts) + minAbs) / sum;
                value = T.Abs(value - T.One);

                points.Add(new Vector2(progress, float.CreateTruncating(value)));
            }
        }
    }
}
