using System.Numerics;

using Avalonia;
using Avalonia.Collections.Pooled;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        Thickness = 1.5,
    };
    private IDisposable? _disposable;

    protected NumberAnimationVisualizer(Animation<T> animation)
        : base(animation)
    {
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
        _disposable = Application.Current!.GetResourceObservable("TextControlForeground").Subscribe(b =>
        {
            if (b is IBrush brush)
            {
                _pen.Brush = brush;
                InvalidateVisual();
            }
        });
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
        _disposable?.Dispose();
        _disposable = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        InvalidatePoints(_points);
        InvalidateVisual();
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
        if (_points.Count == 0)
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

public abstract class NumberAnimationSpanVisualizer<T> : AnimationSpanVisualizer<T>
    where T : INumber<T>, IMinMaxValue<T>
{
    private readonly PooledList<Vector2> _points = new();

    private readonly Pen _pen = new()
    {
        Brush = Brushes.DarkGray,
        LineJoin = PenLineJoin.Round,
        LineCap = PenLineCap.Round,
        Thickness = 1.5,
    };
    private IDisposable? _disposable;

    protected NumberAnimationSpanVisualizer(Animation<T> animation, AnimationSpan<T> animationSpan)
        : base(animation, animationSpan)
    {
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
        AnimationSpan.Invalidated += OnAnimationInvalidated;
        _disposable = Application.Current!.GetResourceObservable("TextControlForeground").Subscribe(b =>
        {
            if (b is IBrush brush)
            {
                _pen.Brush = brush;
                InvalidateVisual();
            }
        });
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
        AnimationSpan.Invalidated -= OnAnimationInvalidated;
        _disposable?.Dispose();
        _disposable = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        InvalidatePoints(_points);
        InvalidateVisual();
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
        if (_points.Count == 0)
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
