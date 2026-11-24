using System.Collections.Specialized;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using Beutl.Graphics;

using AvaPoint = Avalonia.Point;
using AvaRect = Avalonia.Rect;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Controls.Curves;

public partial class CurveEditor : Control
{
    private static readonly IPen s_curvePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 122, 204)), 2).ToImmutable();
    private static readonly IPen s_axisPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1).ToImmutable();
    private static readonly IPen s_handleLinePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 200, 100)), 1).ToImmutable();
    private static readonly IBrush s_handleBrush = new SolidColorBrush(Color.FromArgb(200, 255, 200, 100)).ToImmutable();
    private static readonly IBrush s_markerRangeBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)).ToImmutable();
    private static readonly IPen s_markerRangeEdgePen =
        new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1, new DashStyle([3, 3], 0)).ToImmutable();
    private static readonly IPen s_markerShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), 3).ToImmutable();
    private static readonly IPen s_markerPen = new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), 1.5).ToImmutable();
    private static readonly IPen s_hoverPen =
        new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1, new DashStyle([2, 2], 0)).ToImmutable();

    private int _draggingIndex = -1;
    private DragTarget _dragTarget = DragTarget.None;
    private int _selectedIndex = -1;

    private enum DragTarget
    {
        None,
        Point,
        LeftHandle,
        RightHandle
    }

    public event EventHandler? DragStarted;

    public event EventHandler? DragCompleted;

    static CurveEditor()
    {
        AffectsRender<CurveEditor>(PointsProperty, MarkerPositionProperty, MarkerRangeProperty, HoverPositionProperty);
    }

    [GeneratedStyledProperty]
    public partial IList<CurveControlPoint>? Points { get; set; }

    [GeneratedStyledProperty]
    public partial bool IsReadOnly { get; set; }

    [GeneratedStyledProperty(DefaultValue = CurveVisualization.None)]
    public partial CurveVisualization Visualization { get; set; }

    [GeneratedStyledProperty]
    public partial CurveVisualizationRenderer? VisualizationRenderer { get; set; }

    // Where a picked color stands on the curve, from 0 to 1, drawn as a line; null draws none.
    [GeneratedStyledProperty]
    public partial double? MarkerPosition { get; set; }

    // The width of the band around the marker that the color's range covers, from 0 to 1. The band goes on at the
    // other edge where it passes one, as hue does.
    [GeneratedStyledProperty]
    public partial double MarkerRange { get; set; }

    // Where the color under an eyedropper stands, drawn as a dashed line; null draws none.
    [GeneratedStyledProperty]
    public partial double? HoverPosition { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PointsProperty && VisualRoot is not null)
        {
            if (change.OldValue is INotifyCollectionChanged oldPoints)
                oldPoints.CollectionChanged -= OnPointsCollectionChanged;

            if (change.NewValue is INotifyCollectionChanged newPoints)
                newPoints.CollectionChanged += OnPointsCollectionChanged;
        }
        else if (change.Property == VisualizationRendererProperty)
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

    // The points change from outside as well, as an undo or the color range in the curves tab changes the curve, so the
    // editor follows the list while it is shown.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Points is INotifyCollectionChanged points)
            points.CollectionChanged += OnPointsCollectionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Points is INotifyCollectionChanged points)
            points.CollectionChanged -= OnPointsCollectionChanged;
    }

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Points is null || IsReadOnly) return;

        var pos = e.GetPosition(this);
        var norm = Normalize(pos);
        var pointerPoint = e.GetCurrentPoint(this);

        var (index, target) = HitTest(pos);

        if (index >= 0)
        {
            if (pointerPoint.Properties.IsRightButtonPressed)
            {
                if (target == DragTarget.LeftHandle || target == DragTarget.RightHandle)
                {
                    var point = Points[index];
                    bool isHandleDefault = target == DragTarget.LeftHandle
                        ? point.LeftHandle == default
                        : point.RightHandle == default;

                    if (!isHandleDefault)
                    {
                        DragStarted?.Invoke(this, EventArgs.Empty);
                        Points[index] = target == DragTarget.LeftHandle
                            ? point.WithLeftHandle(default)
                            : point.WithRightHandle(default);
                        DragCompleted?.Invoke(this, EventArgs.Empty);
                        InvalidateVisual();
                        return;
                    }

                    target = DragTarget.Point;
                }

                if (target == DragTarget.Point && index > 0 && index < Points.Count - 1)
                {
                    // 端点は削除不可
                    DragStarted?.Invoke(this, EventArgs.Empty);
                    Points.RemoveAt(index);
                    _selectedIndex = -1;
                    DragCompleted?.Invoke(this, EventArgs.Empty);
                    InvalidateVisual();
                }
            }
            else if (pointerPoint.Properties.IsLeftButtonPressed)
            {
                _draggingIndex = index;
                _dragTarget = target;
                _selectedIndex = index;
                e.Pointer.Capture(this);
                DragStarted?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
        }
        else if (pointerPoint.Properties.IsLeftButtonPressed)
        {
            DragStarted?.Invoke(this, EventArgs.Empty);
            index = InsertPoint(norm);
            _selectedIndex = index;
            InvalidateVisual();

            _draggingIndex = index;
            _dragTarget = DragTarget.Point;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Points is null || IsReadOnly) return;

        if (_draggingIndex >= 0 && _dragTarget != DragTarget.None)
        {
            var norm = Normalize(e.GetPosition(this));
            var point = Points[_draggingIndex];

            switch (_dragTarget)
            {
                case DragTarget.Point:
                    norm = new BtlPoint(Math.Clamp(norm.X, 0, 1), Math.Clamp(norm.Y, 0, 1));
                    if (_draggingIndex == 0)
                        norm = new BtlPoint(0, norm.Y);
                    else if (_draggingIndex == Points.Count - 1)
                        norm = new BtlPoint(1, norm.Y);

                    Points[_draggingIndex] = point.WithPoint(norm);
                    SortPoints();
                    break;

                case DragTarget.LeftHandle:
                    var leftOffsetX = Math.Min(0, norm.X - point.Point.X); // 左側のみ
                    var leftOffset = new BtlPoint(leftOffsetX, norm.Y - point.Point.Y);
                    Points[_draggingIndex] = point.WithLeftHandle(leftOffset);
                    break;

                case DragTarget.RightHandle:
                    var rightOffsetX = Math.Max(0, norm.X - point.Point.X); // 右側のみ
                    var rightOffset = new BtlPoint(rightOffsetX, norm.Y - point.Point.Y);
                    Points[_draggingIndex] = point.WithRightHandle(rightOffset);
                    break;
            }

            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingIndex >= 0)
        {
            EndDrag();
            e.Pointer.Capture(null);
        }
    }

    // Capture loss keeps the curve where the drag reached, as release does.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_draggingIndex >= 0)
            EndDrag();
    }

    // Ends the drag in progress without DragCompleted, for a host that ended the gesture itself.
    public void StopDrag()
    {
        _draggingIndex = -1;
        _dragTarget = DragTarget.None;
        // The host may revert the points next, and nothing else redraws the curve for that.
        InvalidateVisual();
    }

    private void EndDrag()
    {
        StopDrag();
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private (int Index, DragTarget Target) HitTest(AvaPoint screenPos)
    {
        if (Points is null) return (-1, DragTarget.None);

        const double pointRadius = 6;
        const double handleRadius = 3;

        // 選択されたポイントのハンドルを優先的にチェック
        if (_selectedIndex >= 0 && _selectedIndex < Points.Count)
        {
            var selectedPoint = Points[_selectedIndex];

            var leftHandlePos = Denormalize(selectedPoint.AbsoluteLeftHandle);
            if (IsWithin(leftHandlePos, screenPos, handleRadius) &&
                _selectedIndex != 0)
            {
                return (_selectedIndex, DragTarget.LeftHandle);
            }

            var rightHandlePos = Denormalize(selectedPoint.AbsoluteRightHandle);
            if (IsWithin(rightHandlePos, screenPos, handleRadius) &&
                _selectedIndex != Points.Count - 1)
            {
                return (_selectedIndex, DragTarget.RightHandle);
            }
        }

        for (int i = 0; i < Points.Count; i++)
        {
            var pos = Denormalize(Points[i].Point);
            if (IsWithin(pos, screenPos, pointRadius))
            {
                return (i, DragTarget.Point);
            }
        }

        return (-1, DragTarget.None);
    }

    private static bool IsWithin(AvaPoint point, AvaPoint center, double radius)
    {
        return Math.Abs(point.X - center.X) <= radius && Math.Abs(point.Y - center.Y) <= radius;
    }

    private int InsertPoint(BtlPoint norm)
    {
        if (Points is null) return -1;

        int index = 0;
        while (index < Points.Count && Points[index].Point.X < norm.X)
        {
            index++;
        }

        // 隣接するポイントを参考にハンドルを計算
        BtlPoint leftHandle = default;
        BtlPoint rightHandle = default;

        if (index > 0 && index < Points.Count)
        {
            // 前後のポイントがある場合、その方向に基づいてハンドルを設定
            var prevPoint = Points[index - 1].Point;
            var nextPoint = Points[index].Point;

            // 前後のポイントへの距離の1/4をハンドルの長さとする
            float leftDist = (norm.X - prevPoint.X) * 0.25f;
            float rightDist = (nextPoint.X - norm.X) * 0.25f;

            float slope = (nextPoint.Y - prevPoint.Y) / (nextPoint.X - prevPoint.X);

            leftHandle = new BtlPoint(-leftDist, -leftDist * slope);
            rightHandle = new BtlPoint(rightDist, rightDist * slope);
        }
        else if (index > 0)
        {
            var prevPoint = Points[index - 1].Point;
            float dist = (norm.X - prevPoint.X) * 0.25f;
            leftHandle = new BtlPoint(-dist, 0);
        }
        else if (index < Points.Count)
        {
            var nextPoint = Points[index].Point;
            float dist = (nextPoint.X - norm.X) * 0.25f;
            rightHandle = new BtlPoint(dist, 0);
        }

        Points.Insert(index, new CurveControlPoint(norm, leftHandle, rightHandle));
        return index;
    }

    private void SortPoints()
    {
        if (Points is null) return;

        // Sort the indices, so the dragged point is found again after it passes a neighbor.
        int[] order = Enumerable.Range(0, Points.Count).OrderBy(i => Points[i].Point.X).ToArray();
        CurveControlPoint[] sorted = order.Select(i => Points[i]).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            Points[i] = sorted[i];
        }

        if (_selectedIndex >= 0 && _draggingIndex >= 0)
        {
            _draggingIndex = Array.IndexOf(order, _draggingIndex);
            _selectedIndex = _draggingIndex;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new AvaRect(Bounds.Size);

        VisualizationRenderer?.Draw(context, bounds, Visualization);

        DrawGrid(context, bounds);
        DrawMarkers(context, bounds);

        if (Points is { Count: > 0 })
        {
            DrawCurve(context, Points);
            DrawSelectedHandles(context, Points);
            DrawPoints(context, Points);
        }
    }

    private static void DrawGrid(DrawingContext context, AvaRect bounds)
    {
        for (int i = 1; i < 4; i++)
        {
            double x = bounds.Width / 4 * i;
            double y = bounds.Height / 4 * i;
            context.DrawLine(s_axisPen, new AvaPoint(x, 0), new AvaPoint(x, bounds.Height));
            context.DrawLine(s_axisPen, new AvaPoint(0, y), new AvaPoint(bounds.Width, y));
        }
    }

    private void DrawMarkers(DrawingContext context, AvaRect bounds)
    {
        if (MarkerPosition is { } position && double.IsFinite(position))
        {
            double range = Math.Clamp(MarkerRange, 0, 1);
            if (range > 0)
            {
                double start = position - range / 2;
                double end = position + range / 2;
                if (start < 0)
                {
                    DrawSpan(context, bounds, start + 1, 1);
                    DrawSpan(context, bounds, 0, end);
                }
                else if (end > 1)
                {
                    DrawSpan(context, bounds, start, 1);
                    DrawSpan(context, bounds, 0, end - 1);
                }
                else
                {
                    DrawSpan(context, bounds, start, end);
                }

                DrawVertical(context, bounds, s_markerRangeEdgePen, WrapPosition(start));
                DrawVertical(context, bounds, s_markerRangeEdgePen, WrapPosition(end));
            }

            DrawVertical(context, bounds, s_markerShadowPen, position);
            DrawVertical(context, bounds, s_markerPen, position);
        }

        if (HoverPosition is { } hover && double.IsFinite(hover))
        {
            DrawVertical(context, bounds, s_hoverPen, hover);
        }

        static double WrapPosition(double x) => x is >= 0 and <= 1 ? x : x - Math.Floor(x);

        static void DrawSpan(DrawingContext context, AvaRect bounds, double from, double to)
        {
            context.DrawRectangle(s_markerRangeBrush, null,
                new AvaRect(from * bounds.Width, 0, Math.Max(0, to - from) * bounds.Width, bounds.Height));
        }

        static void DrawVertical(DrawingContext context, AvaRect bounds, IPen pen, double x)
        {
            double at = Math.Clamp(x, 0, 1) * bounds.Width;
            context.DrawLine(pen, new AvaPoint(at, 0), new AvaPoint(at, bounds.Height));
        }
    }

    private void DrawCurve(DrawingContext context, IList<CurveControlPoint> points)
    {
        if (points.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var gctx = geometry.Open())
            {
                var firstPoint = Denormalize(points[0].Point);
                gctx.BeginFigure(firstPoint, false);

                for (int i = 1; i < points.Count; i++)
                {
                    var prev = points[i - 1];
                    var curr = points[i];

                    if (prev.HasHandles || curr.HasHandles)
                    {
                        var cp1 = Denormalize(prev.AbsoluteRightHandle);
                        var cp2 = Denormalize(curr.AbsoluteLeftHandle);
                        var endPoint = Denormalize(curr.Point);
                        gctx.CubicBezierTo(cp1, cp2, endPoint);
                    }
                    else
                    {
                        gctx.LineTo(Denormalize(curr.Point));
                    }
                }

                gctx.EndFigure(false);
            }

            context.DrawGeometry(null, s_curvePen, geometry);
        }
    }

    private void DrawSelectedHandles(DrawingContext context, IList<CurveControlPoint> points)
    {
        if (_selectedIndex >= 0 && _selectedIndex < points.Count)
        {
            var selectedPoint = points[_selectedIndex];
            var mainPos = Denormalize(selectedPoint.Point);

            var leftHandlePos = Denormalize(selectedPoint.AbsoluteLeftHandle);
            context.DrawLine(s_handleLinePen, mainPos, leftHandlePos);
            context.DrawEllipse(s_handleBrush, s_handleLinePen, leftHandlePos, 4, 4);

            var rightHandlePos = Denormalize(selectedPoint.AbsoluteRightHandle);
            context.DrawLine(s_handleLinePen, mainPos, rightHandlePos);
            context.DrawEllipse(s_handleBrush, s_handleLinePen, rightHandlePos, 4, 4);
        }
    }

    private void DrawPoints(DrawingContext context, IList<CurveControlPoint> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            var pt = Denormalize(points[i].Point);
            var brush = i == _selectedIndex ? Brushes.Yellow : Brushes.White;
            context.DrawEllipse(brush, s_curvePen, pt, 4, 4);
        }
    }

    private BtlPoint Normalize(AvaPoint pos)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return default;
        return new BtlPoint((float)(pos.X / Bounds.Width), (float)(1 - (pos.Y / Bounds.Height)));
    }

    private AvaPoint Denormalize(BtlPoint pos)
    {
        return new AvaPoint(pos.X * Bounds.Width, (1 - pos.Y) * Bounds.Height);
    }
}
