using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

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

    public static readonly StyledProperty<IBrush?> ReadoutBackgroundProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(ReadoutBackground));

    public static readonly StyledProperty<IBrush?> ReadoutForegroundProperty
        = AvaloniaProperty.Register<TimelineOverlay, IBrush?>(nameof(ReadoutForeground));

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
            ReadoutForegroundProperty);
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

    public IBrush? ReadoutBackground
    {
        get => GetValue(ReadoutBackgroundProperty);
        set => SetValue(ReadoutBackgroundProperty, value);
    }

    public IBrush? ReadoutForeground
    {
        get => GetValue(ReadoutForegroundProperty);
        set => SetValue(ReadoutForegroundProperty, value);
    }

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

        if (DragReadout is { } readout)
            DrawReadout(context, readout.Position, readout.Change, readout.Details);
    }

    private void DrawReadout(DrawingContext context, Point pointer, string change, string details)
    {
        const double gap = 12;
        const double padding = 8;
        double width = Math.Min(320, Viewport.Width - padding * 2);
        int detailLines = width < 290 ? 2 : 1;
        double height = detailLines == 1 ? 44 : 58;
        if (width <= padding * 2 || Viewport.Height < height) return;

        double x = Math.Clamp(pointer.X + gap, Offset.X + padding, Offset.X + Viewport.Width - width - padding);
        double y = pointer.Y + gap;
        if (y + height > Offset.Y + Viewport.Height)
            y = pointer.Y - height - gap;
        y = Math.Clamp(y, Offset.Y, Offset.Y + Viewport.Height - height);

        context.DrawRectangle(ReadoutBackground, new Pen(ReadoutForeground, 0.5), new Rect(x, y, width, height), 4, 4);
        var typeface = new Typeface(new FontFamily("Consolas, Menlo, monospace"));
        using var delta = new TextLayout(change, typeface, 12, ReadoutForeground);
        using var result = new TextLayout(details, typeface, 11, ReadoutForeground,
            textWrapping: TextWrapping.Wrap, textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: width - padding * 2, maxLines: detailLines);
        delta.Draw(context, new Point(x + padding, y + 5));
        result.Draw(context, new Point(x + padding, y + 22));
    }
}
