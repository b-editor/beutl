using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Editor.Components.Views;

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

    public static readonly DirectProperty<TimelineOverlay, Thickness> StartingBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Thickness>(
            nameof(StartingBarMargin), o => o.StartingBarMargin, (o, v) => o.StartingBarMargin = v);

    public static readonly DirectProperty<TimelineOverlay, Thickness> EndingBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Thickness>(
            nameof(EndingBarMargin), o => o.EndingBarMargin, (o, v) => o.EndingBarMargin = v);

    public static readonly DirectProperty<TimelineOverlay, Thickness> SeekBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineOverlay, Thickness>(
            nameof(SeekBarMargin), o => o.SeekBarMargin, (o, v) => o.SeekBarMargin = v);

    public static readonly StyledProperty<IBrush?> SeekBarBrushProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(SeekBarBrush));

    public static readonly StyledProperty<IBrush?> StartingBarBrushProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(StartingBarBrush));

    public static readonly StyledProperty<IBrush?> EndingBarBrushProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(EndingBarBrush));

    private Vector _offset;
    private Thickness _startingBarMargin;
    private Thickness _endingBarMargin;
    private Thickness _seekBarMargin;
    private Size _viewport;
    private Rect _selectionRange;
    private ImmutablePen? _seekBarPen;
    private ImmutablePen? _startingBarPen;
    private ImmutablePen? _endingBarPen;

    static TimelineOverlay()
    {
        AffectsRender<TimelineOverlay>(
            OffsetProperty,
            ViewportProperty,
            SelectionRangeProperty,
            StartingBarMarginProperty,
            EndingBarMarginProperty,
            SeekBarMarginProperty,
            SeekBarBrushProperty,
            StartingBarBrushProperty,
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

    public Thickness StartingBarMargin
    {
        get => _startingBarMargin;
        set => SetAndRaise(StartingBarMarginProperty, ref _startingBarMargin, value);
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

    public IBrush? StartingBarBrush
    {
        get => GetValue(StartingBarBrushProperty);
        set => SetValue(StartingBarBrushProperty, value);
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
        else if (change.Property == StartingBarBrushProperty)
        {
            _startingBarPen = new ImmutablePen(StartingBarBrush?.ToImmutable(), 1.25);
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
        _startingBarPen ??= new ImmutablePen(StartingBarBrush?.ToImmutable(), 1.25);
        _endingBarPen ??= new ImmutablePen(EndingBarBrush?.ToImmutable(), 1.25);

        Rect rect = _selectionRange.Normalize();
        context.FillRectangle(TimelineSharedObject.SelectionFillBrush, rect);

        context.DrawRectangle(TimelineSharedObject.SelectionPen, rect);

        using (context.PushTransform(Matrix.CreateTranslation(0, _offset.Y)))
        {
            double height = _viewport.Height;
            var seekbar = new Point(_seekBarMargin.Left, 0);
            var startingbar = new Point(_startingBarMargin.Left, 0);
            var endingbar = new Point(_endingBarMargin.Left, 0);
            var bottom = new Point(0, height);

            context.DrawLine(_seekBarPen, seekbar, seekbar + bottom);
            context.DrawLine(_startingBarPen, startingbar, startingbar + bottom);
            context.DrawLine(_endingBarPen, endingbar, endingbar + bottom);
        }
    }
}
