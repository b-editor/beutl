#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Beutl.Controls.Curves;

public class CurveEditor : Control
{
    public static readonly StyledProperty<IList<Point>?> PointsProperty =
        AvaloniaProperty.Register<CurveEditor, IList<Point>?>(nameof(Points));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CurveEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<CurveVisualization> VisualizationProperty =
        AvaloniaProperty.Register<CurveEditor, CurveVisualization>(nameof(Visualization), CurveVisualization.None);

    public static readonly StyledProperty<CurveVisualizationRenderer?> VisualizationRendererProperty =
        AvaloniaProperty.Register<CurveEditor, CurveVisualizationRenderer?>(nameof(VisualizationRenderer));

    private static readonly IPen s_curvePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 122, 204)), 2).ToImmutable();
    private static readonly IPen s_axisPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1).ToImmutable();

    private int _draggingIndex = -1;

    public event EventHandler? DragStarted;

    public event EventHandler? DragCompleted;

    static CurveEditor()
    {
        AffectsRender<CurveEditor>(PointsProperty);
    }

    public IList<Point>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public CurveVisualization Visualization
    {
        get => GetValue(VisualizationProperty);
        set => SetValue(VisualizationProperty, value);
    }

    public CurveVisualizationRenderer? VisualizationRenderer
    {
        get => GetValue(VisualizationRendererProperty);
        set => SetValue(VisualizationRendererProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VisualizationRendererProperty)
        {
            if (change.OldValue is CurveVisualizationRenderer oldRenderer)
                oldRenderer.Updated -= OnRendererUpdated;

            if (change.NewValue is CurveVisualizationRenderer newRenderer)
                newRenderer.Updated += OnRendererUpdated;

            InvalidateVisual();
        }
    }

    private void OnRendererUpdated(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Points is null || IsReadOnly) return;

        var pos = e.GetPosition(this);
        var norm = Normalize(pos);
        var point = e.GetCurrentPoint(this);

        int index = HitTest(pos);
        if (index >= 0)
        {
            if (point.Properties.IsRightButtonPressed)
            {
                // 端点は削除不可
                if (index > 0 && index < Points.Count - 1)
                {
                    DragStarted?.Invoke(this, EventArgs.Empty);
                    Points.RemoveAt(index);
                    DragCompleted?.Invoke(this, EventArgs.Empty);
                    InvalidateVisual();
                }
            }
            else if (point.Properties.IsLeftButtonPressed)
            {
                _draggingIndex = index;
                e.Pointer.Capture(this);
                DragStarted?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            DragStarted?.Invoke(this, EventArgs.Empty);
            index = InsertPoint(norm);
            InvalidateVisual();

            _draggingIndex = index;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Points is null || IsReadOnly) return;

        if (_draggingIndex >= 0)
        {
            var norm = Normalize(e.GetPosition(this));
            norm = new Point(Math.Clamp(norm.X, 0, 1), Math.Clamp(norm.Y, 0, 1));
            if (_draggingIndex == 0)
                norm = new Point(0, norm.Y);
            else if (_draggingIndex == Points.Count - 1)
                norm = new Point(1, norm.Y);

            Points[_draggingIndex] = norm;
            SortPoints();
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingIndex >= 0)
        {
            e.Pointer.Capture(null);
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }

        _draggingIndex = -1;
    }

    private int HitTest(Point point)
    {
        if (Points is null) return -1;

        const double radius = 6;
        for (int i = 0; i < Points.Count; i++)
        {
            var pos = Denormalize(Points[i]);
            if (Math.Abs(pos.X - point.X) <= radius && Math.Abs(pos.Y - point.Y) <= radius)
            {
                return i;
            }
        }

        return -1;
    }

    private int InsertPoint(Point norm)
    {
        if (Points is null) return -1;

        int index = 0;
        while (index < Points.Count && Points[index].X < norm.X)
        {
            index++;
        }

        Points.Insert(index, norm);
        return index;
    }

    private void SortPoints()
    {
        if (Points is null) return;

        var sorted = Points.OrderBy(p => p.X).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            Points[i] = sorted[i];
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);

        VisualizationRenderer?.Draw(context, bounds, Visualization);

        for (int i = 1; i < 4; i++)
        {
            double x = bounds.Width / 4 * i;
            double y = bounds.Height / 4 * i;
            context.DrawLine(s_axisPen, new Point(x, 0), new Point(x, bounds.Height));
            context.DrawLine(s_axisPen, new Point(0, y), new Point(bounds.Width, y));
        }

        if (Points is { Count: > 0 })
        {
            var segments = new List<Point>(Points.Count);
            foreach (Point p in Points)
            {
                segments.Add(Denormalize(p));
            }

            if (segments.Count > 1)
            {
                var geometry = new StreamGeometry();
                using (var gctx = geometry.Open())
                {
                    gctx.BeginFigure(segments[0], false);
                    foreach (Point pt in segments.Skip(1))
                    {
                        gctx.LineTo(pt);
                    }

                    gctx.EndFigure(false);
                }

                context.DrawGeometry(null, s_curvePen, geometry);
            }

            foreach (Point pt in segments)
            {
                var rect = new Rect(pt.X - 4, pt.Y - 4, 8, 8);
                context.DrawEllipse(Brushes.White, s_curvePen, rect.Center, 4, 4);
            }
        }
    }

    private Point Normalize(Point pos)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return default;
        return new Point(pos.X / Bounds.Width, 1 - (pos.Y / Bounds.Height));
    }

    private Point Denormalize(Point pos)
    {
        return new Point(pos.X * Bounds.Width, (1 - pos.Y) * Bounds.Height);
    }
}
