using System.Numerics;

using Avalonia;
using Avalonia.Collections.Pooled;
using Avalonia.Media;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public abstract class NumberAnimationVisualizer<T> : AnimationVisualizer<T>
    where T : INumber<T>, IMinMaxValue<T>
{
    private readonly PooledList<Vector2> _points = new();

    private readonly Pen _pen = new()
    {
        Brush = Brushes.DarkGray,
        LineJoin = PenLineJoin.Round,
        LineCap = PenLineCap.Round,
        Thickness = 2.5,
    };

    protected NumberAnimationVisualizer(Animation<T> animation)
        : base(animation)
    {
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        InvalidatePoints(_points);
    }

    protected (T Min, T Max) CalculateRange(TimeSpan duration)
    {
        T min = T.MaxValue;
        T max = T.MinValue;
        const int div = 1000;

        for (int i = 0; i <= div; i++)
        {
            double progress = i / (double)div;
            TimeSpan ts = duration * progress;

            T interpolated = Animation.Interpolate(ts);
            min = T.Min(interpolated, min);
            max = T.Max(interpolated, max);
        }

        return (min, max);
    }

    protected abstract void InvalidatePoints(PooledList<Vector2> points);

    public override void Render(DrawingContext context)
    {
        if (_points.Count <= 0)
        {
            InvalidatePoints(_points);
        }

        double width = Bounds.Width;
        double height = Bounds.Height;
        var m = new Vector2((float)width, (float)height);

        bool first = true;
        Vector2 prev = default;
        foreach (Vector2 item in _points.Span)
        {
            if (first)
            {
                prev = item * m;
                first = false;
            }
            else
            {
                var actual = item * m;
                context.DrawLine(
                    _pen,
                    new Point(prev.X, prev.Y),
                    new Point(actual.X, actual.Y));
                prev = actual;
            }
        }
    }
}
