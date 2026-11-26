using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace Beutl.Editor.Components.Views;

public sealed partial class TimelineOverlay : TimelineBarsControl
{
    private const double ReadoutWidth = 252;
    private const double ReadoutHeight = 44;
    private const double ReadoutPadding = 9;
    private const double PointerGap = 12;
    private static readonly Typeface s_readoutTypeface = new(FontFamily.Default);
    private static readonly Typeface s_deltaTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly FontFeatureCollection s_tabularNumbers = [new FontFeature { Tag = "tnum" }];

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
            SnapBarBrushProperty,
            ReadoutBackgroundProperty,
            ReadoutForegroundProperty,
            ReadoutBorderBrushProperty);
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

    [GeneratedStyledProperty]
    public partial IBrush? SnapBarBrush { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? ReadoutBackground { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? ReadoutForeground { get; set; }

    [GeneratedStyledProperty]
    public partial IBrush? ReadoutBorderBrush { get; set; }

    internal (Point Position, string Change, string Details)? DragReadout { get; private set; }

    internal void SetDragReadout((Point Position, string Change, string Details)? value)
    {
        if (DragReadout == value) return;
        DragReadout = value;
        InvalidateVisual();
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

        if (DragReadout is { } readout && GetReadoutBounds(readout.Position) is { } bounds)
        {
            context.DrawRectangle(ReadoutBackground, new Pen(ReadoutBorderBrush), bounds, 5, 5);
            double textWidth = bounds.Width - 2 * ReadoutPadding;
            using var change = new TextLayout(readout.Change, s_deltaTypeface, 13, ReadoutForeground,
                textTrimming: TextTrimming.CharacterEllipsis, maxWidth: textWidth, maxLines: 1, fontFeatures: s_tabularNumbers);
            using var details = new TextLayout(readout.Details, s_readoutTypeface, 11, ReadoutForeground,
                textTrimming: TextTrimming.CharacterEllipsis, maxWidth: textWidth, maxLines: 1, fontFeatures: s_tabularNumbers);
            change.Draw(context, new Point(bounds.X + ReadoutPadding, bounds.Y + 5));
            details.Draw(context, new Point(bounds.X + ReadoutPadding, bounds.Y + 24));
        }
    }

    internal Rect? GetReadoutBounds(Point pointer)
    {
        if (_viewport.Width <= 2 * ReadoutPadding || _viewport.Height < ReadoutHeight) return null;

        double width = Math.Min(ReadoutWidth, _viewport.Width);
        double x = pointer.X + PointerGap;
        double y = pointer.Y + PointerGap;
        if (x + width > _viewport.Width) x = pointer.X - PointerGap - width;
        if (y + ReadoutHeight > _viewport.Height) y = pointer.Y - PointerGap - ReadoutHeight;

        return new Rect(
            _offset.X + Math.Clamp(x, 0, _viewport.Width - width),
            _offset.Y + Math.Clamp(y, 0, _viewport.Height - ReadoutHeight),
            width, ReadoutHeight);
    }
}
