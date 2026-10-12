using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Editor.Components.Views;

public sealed class TimelineOverlay : TimelineBarsControl
{
    public static readonly DirectProperty<TimelineOverlay, Vector> OffsetProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Vector>(
            nameof(Offset), o => o.Offset, (o, v) => o.Offset = v);

    public static readonly DirectProperty<TimelineOverlay, Size> ViewportProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Size>(
            nameof(Viewport), o => o.Viewport, (o, v) => o.Viewport = v);

    public static readonly DirectProperty<TimelineOverlay, Rect> SelectionRangeProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Rect>(
            nameof(SelectionRange), o => o.SelectionRange, (o, v) => o.SelectionRange = v);

    public static readonly DirectProperty<TimelineOverlay, double?> SnapBarPositionProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, double?>(
            nameof(SnapBarPosition), o => o.SnapBarPosition, (o, v) => o.SnapBarPosition = v);

    public static readonly StyledProperty<IBrush?> SnapBarBrushProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(SnapBarBrush));

    private Vector _offset;
    private double? _snapBarPosition;
    private Size _viewport;
    private Rect _selectionRange;
    private ImmutablePen? _snapBarPen;

    static TimelineOverlay()
    {
        AffectsRender<TimelineOverlay>(
            OffsetProperty,
            ViewportProperty,
            SelectionRangeProperty,
            SnapBarPositionProperty,
            SnapBarBrushProperty);
    }

    public TimelineOverlay()
    {
        ClipToBounds = true;
    }

    public Vector Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    public Size Viewport
    {
        get => _viewport;
        set => SetAndRaise(ViewportProperty, ref _viewport, value);
    }

    public Rect SelectionRange
    {
        get => _selectionRange;
        set => SetAndRaise(SelectionRangeProperty, ref _selectionRange, value);
    }

    public double? SnapBarPosition
    {
        get => _snapBarPosition;
        set => SetAndRaise(SnapBarPositionProperty, ref _snapBarPosition, value);
    }

    public IBrush? SnapBarBrush
    {
        get => GetValue(SnapBarBrushProperty);
        set => SetValue(SnapBarBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SnapBarBrushProperty)
        {
            _snapBarPen = new ImmutablePen(SnapBarBrush?.ToImmutable(), 1.0, new ImmutableDashStyle([3, 3], 0));
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect rect = _selectionRange.Normalize();
        context.FillRectangle(TimelineSharedObject.SelectionFillBrush, rect);

        context.DrawRectangle(TimelineSharedObject.SelectionPen, rect);

        using (context.PushTransform(Matrix.CreateTranslation(0, _offset.Y)))
        {
            double height = _viewport.Height;
            DrawBars(context, height);

            if (_snapBarPosition is { } snap)
            {
                _snapBarPen ??= new ImmutablePen(SnapBarBrush?.ToImmutable(), 1.0, new ImmutableDashStyle([3, 3], 0));
                var snapBar = new Point(snap, 0);
                context.DrawLine(_snapBarPen, snapBar, snapBar + new Point(0, height));
            }
        }
    }
}
