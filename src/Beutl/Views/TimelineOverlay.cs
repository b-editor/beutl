using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Views;

public static class TimelineSharedObject
{
    public static readonly IPen SelectionPen;
    public static readonly IBrush SelectionFillBrush = new ImmutableSolidColorBrush(Colors.CornflowerBlue, 0.3);

    static TimelineSharedObject()
    {
        SelectionPen = new ImmutablePen(Brushes.CornflowerBlue, 0.5);
    }
}

public sealed class TimelineOverlay : Control
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

    public static readonly DirectProperty<TimelineOverlay, Thickness> EndingBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Thickness>(
            nameof(EndingBarMargin), o => o.EndingBarMargin, (o, v) => o.EndingBarMargin = v);

    public static readonly DirectProperty<TimelineOverlay, Thickness> SeekBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Thickness>(
            nameof(SeekBarMargin), o => o.SeekBarMargin, (o, v) => o.SeekBarMargin = v);

    public static readonly StyledProperty<IBrush?> SeekBarBrushProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(SeekBarBrush));
    
    public static readonly StyledProperty<IBrush?> EndingBarBrushProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(EndingBarBrush));

    private Vector _offset;
    private Thickness _endingBarMargin;
    private Thickness _seekBarMargin;
    private Size _viewport;
    private Rect _selectionRange;
    private ImmutablePen? _seekBarPen;
    private ImmutablePen? _endingBarPen;

    static TimelineOverlay()
    {
        AffectsRender<TimelineOverlay>(
            OffsetProperty,
            ViewportProperty,
            SelectionRangeProperty,
            EndingBarMarginProperty,
            SeekBarMarginProperty,
            SeekBarBrushProperty,
            EndingBarBrushProperty);
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

    public Thickness EndingBarMargin
    {
        get => _endingBarMargin;
        set => SetAndRaise(EndingBarMarginProperty, ref _endingBarMargin, value);
    }

    public Thickness SeekBarMargin
    {
        get => _seekBarMargin;
        set => SetAndRaise(SeekBarMarginProperty, ref _seekBarMargin, value);
    }

    public IBrush? SeekBarBrush
    {
        get => GetValue(SeekBarBrushProperty);
        set => SetValue(SeekBarBrushProperty, value);
    }
    
    public IBrush? EndingBarBrush
    {
        get => GetValue(EndingBarBrushProperty);
        set => SetValue(EndingBarBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeekBarBrushProperty)
        {
            _seekBarPen = new ImmutablePen(SeekBarBrush?.ToImmutable(), 1.25);
        }
        else if (change.Property == EndingBarBrushProperty)
        {
            _endingBarPen = new ImmutablePen(EndingBarBrush?.ToImmutable(), 1.25);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _seekBarPen ??= new ImmutablePen(SeekBarBrush?.ToImmutable(), 1.25);
        _endingBarPen ??= new ImmutablePen(EndingBarBrush?.ToImmutable(), 1.25);

        Rect rect = _selectionRange.Normalize();
        context.FillRectangle(TimelineSharedObject.SelectionFillBrush, rect);

        context.DrawRectangle(TimelineSharedObject.SelectionPen, rect);

        using (context.PushTransform(Matrix.CreateTranslation(0, _offset.Y)))
        {
            double height = _viewport.Height;
            var seekbar = new Point(_seekBarMargin.Left, 0);
            var endingbar = new Point(_endingBarMargin.Left, 0);
            var bottom = new Point(0, height);

            context.DrawLine(_seekBarPen, seekbar, seekbar + bottom);
            context.DrawLine(_endingBarPen, endingbar, endingbar + bottom);
        }
    }
}
