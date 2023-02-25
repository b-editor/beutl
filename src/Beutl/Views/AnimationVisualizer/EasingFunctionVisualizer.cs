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

    public EasingFunctionVisualizer(IAnimation<T> animation)
        : base(animation)
    {
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
        _points?.Dispose();
        _points = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        InvalidatePoints();
        InvalidateVisual();
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
        if (Animation is KeyFrameAnimation<T> kfAnimation)
        {
            TimeSpan duration = CalculateDuration();
            Span<IKeyFrame> span = kfAnimation.KeyFrames.GetMarshal().Value;
            _points ??= new(span.Length);
            EnsureCapacity(_points, span.Length);

            int index = 0;
            float right = 0;
            int totaldiv = (int)duration.TotalMilliseconds / 100;
            TimeSpan prevTime = default;

            foreach (IKeyFrame item in span)
            {
                float p = (float)((item.KeyTime - prevTime) / duration);
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
                prevTime = item.KeyTime;
            }

            for (int i = index; i < _points.Count; i++)
            {
                _points[i].Dispose();
            }

            _points.RemoveRange(index, _points.Count - index);
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_points == null || _points.Count == 0)
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
                        Vector2 actual = point * m;
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

public class EasingFunctionSpanVisualizer<T> : AnimationSpanVisualizer<T>
{
    private PooledList<Vector2>? _points;

    private readonly Pen _pen = new()
    {
        Brush = Brushes.DarkGray,
        LineJoin = PenLineJoin.Round,
        LineCap = PenLineCap.Round,
        Thickness = 2.5,
    };

    public EasingFunctionSpanVisualizer(KeyFrameAnimation<T> animation, KeyFrame<T> keyframe)
        : base(animation, keyframe)
    {
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
        KeyFrame.Invalidated += OnAnimationInvalidated;
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
        KeyFrame.Invalidated -= OnAnimationInvalidated;
        _points?.Dispose();
        _points = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        InvalidatePoints();
        InvalidateVisual();
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
        int totaldiv = (int)duration.TotalMilliseconds / 100;
        float p = (float)(CalculateKeyFrameLength() / duration);
        int div = (int)(totaldiv * p);

        if (_points == null)
        {
            _points = new PooledList<Vector2>(div);
        }
        else
        {
            _points.Clear();
            EnsureCapacity(_points, div);
        }

        for (int i = 0; i <= div; i++)
        {
            float progress = i / (float)div;
            float value = KeyFrame.Easing.Ease(progress);

            value = Math.Abs(value - 1);

            _points.Add(new Vector2(progress, value));
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_points == null || _points.Count == 0)
        {
            InvalidatePoints();
        }

        if (_points != null)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;
            var m = new Vector2((float)width, (float)height);

            bool first = true;
            Vector2 prev = default;
            foreach (Vector2 point in _points.Span)
            {
                if (first)
                {
                    prev = point * m;
                    first = false;
                }
                else
                {
                    Vector2 actual = point * m;
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
