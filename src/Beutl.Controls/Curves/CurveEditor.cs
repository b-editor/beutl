#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using Beutl.Graphics;

using AvaPoint = Avalonia.Point;
using AvaRect = Avalonia.Rect;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Controls.Curves;

public class CurveEditor : Control
{
    public static readonly StyledProperty<IList<CurveControlPoint>?> PointsProperty =
        AvaloniaProperty.Register<CurveEditor, IList<CurveControlPoint>?>(nameof(Points));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CurveEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<CurveVisualization> VisualizationProperty =
        AvaloniaProperty.Register<CurveEditor, CurveVisualization>(nameof(Visualization), CurveVisualization.None);

    public static readonly StyledProperty<CurveVisualizationRenderer?> VisualizationRendererProperty =
        AvaloniaProperty.Register<CurveEditor, CurveVisualizationRenderer?>(nameof(VisualizationRenderer));

    private static readonly IPen s_curvePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 122, 204)), 2).ToImmutable();
    private static readonly IPen s_axisPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1).ToImmutable();
    private static readonly IPen s_handleLinePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 200, 100)), 1).ToImmutable();
    private static readonly IBrush s_handleBrush = new SolidColorBrush(Color.FromArgb(200, 255, 200, 100)).ToImmutable();

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
        AffectsRender<CurveEditor>(PointsProperty);
    }

    public IList<CurveControlPoint>? Points
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
                        // ハンドルをリセット
                        DragStarted?.Invoke(this, EventArgs.Empty);
                        if (target == DragTarget.LeftHandle)
                            Points[index] = point.WithLeftHandle(default);
                        else
                            Points[index] = point.WithRightHandle(default);
                        DragCompleted?.Invoke(this, EventArgs.Empty);
                        InvalidateVisual();
                        return;
                    }

                    target = DragTarget.Point;
                }

                if (target == DragTarget.Point)
                {
                    // 端点は削除不可
                    if (index > 0 && index < Points.Count - 1)
                    {
                        DragStarted?.Invoke(this, EventArgs.Empty);
                        Points.RemoveAt(index);
                        _selectedIndex = -1;
                        DragCompleted?.Invoke(this, EventArgs.Empty);
                        InvalidateVisual();
                    }
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
            e.Pointer.Capture(null);
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }

        _draggingIndex = -1;
        _dragTarget = DragTarget.None;
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

            // 左ハンドル
            var leftHandlePos = Denormalize(selectedPoint.AbsoluteLeftHandle);
            if (Math.Abs(leftHandlePos.X - screenPos.X) <= handleRadius &&
                Math.Abs(leftHandlePos.Y - screenPos.Y) <= handleRadius &&
                _selectedIndex != 0)
            {
                return (_selectedIndex, DragTarget.LeftHandle);
            }

            // 右ハンドル
            var rightHandlePos = Denormalize(selectedPoint.AbsoluteRightHandle);
            if (Math.Abs(rightHandlePos.X - screenPos.X) <= handleRadius &&
                Math.Abs(rightHandlePos.Y - screenPos.Y) <= handleRadius &&
                _selectedIndex != Points.Count - 1)
            {
                return (_selectedIndex, DragTarget.RightHandle);
            }
        }

        // メインポイントをチェック
        for (int i = 0; i < Points.Count; i++)
        {
            var pos = Denormalize(Points[i].Point);
            if (Math.Abs(pos.X - screenPos.X) <= pointRadius && Math.Abs(pos.Y - screenPos.Y) <= pointRadius)
            {
                return (i, DragTarget.Point);
            }
        }

        return (-1, DragTarget.None);
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

            // 傾きを計算
            float slope = (nextPoint.Y - prevPoint.Y) / (nextPoint.X - prevPoint.X);

            leftHandle = new BtlPoint(-leftDist, -leftDist * slope);
            rightHandle = new BtlPoint(rightDist, rightDist * slope);
        }
        else if (index > 0)
        {
            // 前のポイントのみある場合
            var prevPoint = Points[index - 1].Point;
            float dist = (norm.X - prevPoint.X) * 0.25f;
            leftHandle = new BtlPoint(-dist, 0);
        }
        else if (index < Points.Count)
        {
            // 次のポイントのみある場合
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

        var sorted = Points.OrderBy(p => p.Point.X).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            Points[i] = sorted[i];
        }

        // Update selected index after sorting
        if (_selectedIndex >= 0 && _draggingIndex >= 0)
        {
            for (int i = 0; i < sorted.Length; i++)
            {
                if (Points[i].Point == sorted[_draggingIndex].Point)
                {
                    _selectedIndex = i;
                    _draggingIndex = i;
                    break;
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new AvaRect(Bounds.Size);

        VisualizationRenderer?.Draw(context, bounds, Visualization);

        // Draw grid
        for (int i = 1; i < 4; i++)
        {
            double x = bounds.Width / 4 * i;
            double y = bounds.Height / 4 * i;
            context.DrawLine(s_axisPen, new AvaPoint(x, 0), new AvaPoint(x, bounds.Height));
            context.DrawLine(s_axisPen, new AvaPoint(0, y), new AvaPoint(bounds.Width, y));
        }

        if (Points is { Count: > 0 })
        {
            // Draw curve
            if (Points.Count > 1)
            {
                var geometry = new StreamGeometry();
                using (var gctx = geometry.Open())
                {
                    var firstPoint = Denormalize(Points[0].Point);
                    gctx.BeginFigure(firstPoint, false);

                    for (int i = 1; i < Points.Count; i++)
                    {
                        var prev = Points[i - 1];
                        var curr = Points[i];

                        if (prev.HasHandles || curr.HasHandles)
                        {
                            // Draw cubic Bezier
                            var cp1 = Denormalize(prev.AbsoluteRightHandle);
                            var cp2 = Denormalize(curr.AbsoluteLeftHandle);
                            var endPoint = Denormalize(curr.Point);
                            gctx.CubicBezierTo(cp1, cp2, endPoint);
                        }
                        else
                        {
                            // Draw line
                            gctx.LineTo(Denormalize(curr.Point));
                        }
                    }

                    gctx.EndFigure(false);
                }

                context.DrawGeometry(null, s_curvePen, geometry);
            }

            // Draw handles for selected point
            if (_selectedIndex >= 0 && _selectedIndex < Points.Count)
            {
                var selectedPoint = Points[_selectedIndex];
                var mainPos = Denormalize(selectedPoint.Point);

                // Draw left handle
                var leftHandlePos = Denormalize(selectedPoint.AbsoluteLeftHandle);
                context.DrawLine(s_handleLinePen, mainPos, leftHandlePos);
                context.DrawEllipse(s_handleBrush, s_handleLinePen, leftHandlePos, 4, 4);

                // Draw right handle
                var rightHandlePos = Denormalize(selectedPoint.AbsoluteRightHandle);
                context.DrawLine(s_handleLinePen, mainPos, rightHandlePos);
                context.DrawEllipse(s_handleBrush, s_handleLinePen, rightHandlePos, 4, 4);
            }

            // Draw main points
            for (int i = 0; i < Points.Count; i++)
            {
                var pt = Denormalize(Points[i].Point);
                var brush = i == _selectedIndex ? Brushes.Yellow : Brushes.White;
                context.DrawEllipse(brush, s_curvePen, pt, 4, 4);
            }
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
