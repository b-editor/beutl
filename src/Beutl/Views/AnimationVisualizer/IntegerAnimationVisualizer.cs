using System.Numerics;

using Avalonia.Collections.Pooled;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public class IntegerAnimationVisualizer<T> : NumberAnimationVisualizer<T>
    where T : INumber<T>, IMinMaxValue<T>, IBinaryInteger<T>
{
    public IntegerAnimationVisualizer(Animation<T> animation)
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
            float sub = float.CreateTruncating(max - min);
            for (int i = 0; i <= div; i++)
            {
                float progress = i / (float)div;
                TimeSpan ts = duration * progress;

                float value = float.CreateTruncating(Animation.Interpolate(ts) - min) / sub;
                value = Math.Abs(value - 1);

                points.Add(new Vector2(progress, value));
            }
        }
    }
}

public class IntegerAnimationSpanVisualizer<T> : NumberAnimationSpanVisualizer<T>
    where T : INumber<T>, IMinMaxValue<T>, IBinaryInteger<T>
{
    public IntegerAnimationSpanVisualizer(Animation<T> animation, AnimationSpan<T> animationSpan)
        : base(animation, animationSpan)
    {
    }

    protected override void InvalidatePoints(PooledList<Vector2> points)
    {
        points.Clear();
        TimeSpan duration = CalculateDuration();
        (T min, T max) = CalculateRange(duration);
        int div = (int)AnimationSpan.Duration.TotalMilliseconds / 100;
        // EnsureCapacity
        if (points.Capacity < div)
        {
            int newCapacity = points.Capacity == 0 ? 4 : points.Capacity * 2;

            if (newCapacity < div)
                newCapacity = div;

            points.Capacity = newCapacity;
        }

        float sub = float.CreateTruncating(max - min);
        for (int i = 0; i <= div; i++)
        {
            float progress = i / (float)div;

            float value = float.CreateTruncating(AnimationSpan.Interpolate(progress) - min) / sub;
            value = Math.Abs(value - 1);

            points.Add(new Vector2(progress, value));
        }
    }
}
