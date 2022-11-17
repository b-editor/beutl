using System.Numerics;

using Avalonia;
using Avalonia.Collections.Pooled;
using Avalonia.Media;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public class EasingFunctionVisualizer<T> : AnimationVisualizer<T>
{
    private PooledList<PooledList<Vector2>>? _points;

    private readonly Pen _pen = new()
    {
        Brush = Brushes.DarkGray,
        LineJoin = PenLineJoin.Round,
        LineCap = PenLineCap.Round,
        Thickness = 2.5,
    };

    public EasingFunctionVisualizer(Animation<T> animation)
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
        _points?.Dispose();
        _points = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        InvalidatePoints();
    }

    private static void EnsureCapacity<TElement>(PooledList<TElement> list, int capacity)
    {
        if (list.Capacity < capacity)
        {
            int newCapacity = list.Capacity == 0 ? 4 : list.Capacity * 2;

            if (newCapacity < capacity)
                newCapacity = capacity;

            list.Capacity = newCapacity;
        }
    }

    private void InvalidatePoints()
    {
        TimeSpan duration = CalculateDuration();
        Span<AnimationSpan<T>> span = Animation.Children.GetMarshal().Value;
        _points ??= new(span.Length);
        EnsureCapacity(_points, span.Length);

        int index = 0;
        float right = 0;
        int totaldiv = (int)duration.TotalMilliseconds / 100;

        foreach (AnimationSpan<T> item in span)
        {
            float p = (float)(item.Duration / duration);
            int div = (int)(totaldiv * p);

            PooledList<Vector2>? inner;
            if (index < _points.Count)
            {
                inner = _points[index];
            }
            else
            {
                inner = new PooledList<Vector2>();
                _points.Add(inner);
            }

            inner.Clear();
            EnsureCapacity(inner, div);
            for (int i = 0; i <= div; i++)
            {
                float value = item.Easing.Ease(i / (float)div);

                value = Math.Abs(value - 1);

                inner.Add(new Vector2((i / (float)div * p) + right, value));
            }

            right += p;
            index++;
        }

        for (int i = index; i < _points.Count; i++)
        {
            _points[i].Dispose();
        }

        _points.RemoveRange(index, _points.Count - index);
    }

    public override void Render(DrawingContext context)
    {
        if (_points == null || _points.Count <= 0)
        {
            InvalidatePoints();
        }

        if (_points != null)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;
            var m = new Vector2((float)width, (float)height);

            foreach (PooledList<Vector2> item in _points.Span)
            {
                bool first = true;
                Vector2 prev = default;
                foreach (Vector2 point in item.Span)
                {
                    if (first)
                    {
                        prev = point * m;
                        first = false;
                    }
                    else
                    {
                        var actual = point * m;
                        context.DrawLine(
                            _pen,
                            new Point(prev.X, prev.Y),
                            new Point(actual.X, actual.Y));
                        prev = actual;
                    }
                }
            }
        }
    }
}
