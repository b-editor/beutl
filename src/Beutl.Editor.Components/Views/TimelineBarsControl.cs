using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Editor.Components.Views;

// The seek, starting and ending bars that the timeline scale and overlay both draw.
public abstract class TimelineBarsControl : Control
{
    public static readonly DirectProperty<TimelineBarsControl, Thickness> StartingBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineBarsControl, Thickness>(
            nameof(StartingBarMargin), o => o.StartingBarMargin, (o, v) => o.StartingBarMargin = v);

    public static readonly DirectProperty<TimelineBarsControl, Thickness> EndingBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineBarsControl, Thickness>(
            nameof(EndingBarMargin), o => o.EndingBarMargin, (o, v) => o.EndingBarMargin = v);

    public static readonly DirectProperty<TimelineBarsControl, Thickness> SeekBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineBarsControl, Thickness>(
            nameof(SeekBarMargin), o => o.SeekBarMargin, (o, v) => o.SeekBarMargin = v);

    public static readonly StyledProperty<IBrush?> SeekBarBrushProperty
        = AvaloniaProperty.Register<TimelineBarsControl, IBrush?>(nameof(SeekBarBrush));

    public static readonly StyledProperty<IBrush?> StartingBarBrushProperty
        = AvaloniaProperty.Register<TimelineBarsControl, IBrush?>(nameof(StartingBarBrush));

    public static readonly StyledProperty<IBrush?> EndingBarBrushProperty
        = AvaloniaProperty.Register<TimelineBarsControl, IBrush?>(nameof(EndingBarBrush));

    private Thickness _startingBarMargin;
    private Thickness _endingBarMargin;
    private Thickness _seekBarMargin;
    private ImmutablePen? _seekBarPen;
    private ImmutablePen? _startingBarPen;
    private ImmutablePen? _endingBarPen;

    static TimelineBarsControl()
    {
        AffectsRender<TimelineBarsControl>(
            StartingBarMarginProperty,
            EndingBarMarginProperty,
            SeekBarMarginProperty,
            SeekBarBrushProperty,
            StartingBarBrushProperty,
            EndingBarBrushProperty);
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

    protected void DrawBars(DrawingContext context, double height)
    {
        _seekBarPen ??= new ImmutablePen(SeekBarBrush?.ToImmutable(), 1.25);
        _startingBarPen ??= new ImmutablePen(StartingBarBrush?.ToImmutable(), 1.25);
        _endingBarPen ??= new ImmutablePen(EndingBarBrush?.ToImmutable(), 1.25);

        var seekbar = new Point(_seekBarMargin.Left, 0);
        var startingbar = new Point(_startingBarMargin.Left, 0);
        var endingbar = new Point(_endingBarMargin.Left, 0);
        var bottom = new Point(0, height);

        context.DrawLine(_seekBarPen, seekbar, seekbar + bottom);
        context.DrawLine(_startingBarPen, startingbar, startingbar + bottom);
        context.DrawLine(_endingBarPen, endingbar, endingbar + bottom);
    }
}
