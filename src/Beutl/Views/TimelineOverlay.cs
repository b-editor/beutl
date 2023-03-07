using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Views;

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

    private readonly Pen _pen;
    private readonly IBrush _selectionFillBrush = new ImmutableSolidColorBrush(Colors.CornflowerBlue, 0.3);
    private readonly IBrush _selectionStrokeBrush = Brushes.CornflowerBlue;
    private Vector _offset;
    private Thickness _endingBarMargin;
    private Thickness _seekBarMargin;
    private Size _viewport;
    private Rect _selectionRange;

    static TimelineOverlay()
    {
        AffectsRender<TimelineOverlay>(OffsetProperty, ViewportProperty, SelectionRangeProperty, EndingBarMarginProperty, SeekBarMarginProperty);
    }

    public TimelineOverlay()
    {
        ClipToBounds = true;
        _pen = new Pen();
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect rect = _selectionRange.Normalize();
        context.FillRectangle(_selectionFillBrush, rect);

        _pen.Thickness = 0.5;
        _pen.Brush = _selectionStrokeBrush;
        context.DrawRectangle(_pen, rect);

        using (context.PushPreTransform(Matrix.CreateTranslation(0, _offset.Y)))
        {
            double height = _viewport.Height;
            var seekbar = new Point(_seekBarMargin.Left, 0);
            var endingbar = new Point(_endingBarMargin.Left, 0);
            var bottom = new Point(0, height);

            _pen.Thickness = 1.25;
            _pen.Brush = Brushes.Red;
            context.DrawLine(_pen, seekbar, seekbar + bottom);

            _pen.Brush = Brushes.Blue;
            context.DrawLine(_pen, endingbar, endingbar + bottom);
        }
    }
}
