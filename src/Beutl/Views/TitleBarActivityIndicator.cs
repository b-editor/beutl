using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.ViewModels;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.Views;

// The status button's icon. It shows the Pulse icon while nothing runs and one animation per kind of task
// while tasks run, and the tasks take turns when several run at once. The animations are drawn in an 18-px box,
// as the progress ring before them was.
public sealed class TitleBarActivityIndicator : Control
{
    public static readonly StyledProperty<TitleBarActivities> ActivitiesProperty =
        AvaloniaProperty.Register<TitleBarActivityIndicator, TitleBarActivities>(nameof(Activities));

    public static readonly StyledProperty<double?> UpdateProgressProperty =
        AvaloniaProperty.Register<TitleBarActivityIndicator, double?>(nameof(UpdateProgress));

    public static readonly StyledProperty<bool> IsUpdateInstallingProperty =
        AvaloniaProperty.Register<TitleBarActivityIndicator, bool>(nameof(IsUpdateInstalling));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<TitleBarActivityIndicator>();

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<TitleBarActivityIndicator, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<IBrush?> AccentLineProperty =
        AvaloniaProperty.Register<TitleBarActivityIndicator, IBrush?>(nameof(AccentLine));

    // How long each task shows before the next one takes its turn, and how long a hand-over fades.
    internal const double TurnSeconds = 2.4;
    internal const double FadeSeconds = 0.3;

    private const double BoxSize = 18;
    private const double TrackOpacity = 0.17;

    // The Pulse icon's line is thinner than the drawn tracks, so it stays darker to keep its shape while lit.
    private const double PulseTrackOpacity = 0.5;
    private const double TracePeriod = 1.6;
    private const double TraceTail = 9;
    private const double TraceEdge = 0.8;

    // The output and AI animations share one 4 x 4 grid of dots, so a hand-over between them keeps every dot in place.
    private const int DotSide = 4;
    private const int DotCount = DotSide * DotSide;
    private const double DotPitch = BoxSize / DotSide;
    private const double DotRadius = 1.25;

    // The order the tasks take turns in, the one the user waits on longest first.
    private static readonly TitleBarActivities[] s_turnOrder =
    [
        TitleBarActivities.Output,
        TitleBarActivities.Update,
        TitleBarActivities.AiJob,
        TitleBarActivities.Startup,
    ];

    private static readonly Geometry s_downloadArrow = CreateDownloadArrow();
    private static readonly Geometry s_gear = CreateGear();

    private readonly FluentIcon _pulse;
    private readonly FluentIcon _pulseTrace;
    private TopLevel? _topLevel;
    private bool _isFrameRequested;
    private TimeSpan? _clockOrigin;
    private double? _fixedTime;
    private double _time;
    private TitleBarActivities _shown;
    private TitleBarActivities? _fading;
    private double _shownSince;
    private double? _updateProgress;
    private bool _isUpdateInstalling;
    private Pen? _ringPen;
    private Pen? _trackPen;
    private Pen? _glyphPen;

    static TitleBarActivityIndicator()
    {
        AffectsRender<TitleBarActivityIndicator>(ForegroundProperty, AccentProperty);
    }

    public TitleBarActivityIndicator()
    {
        // Startup lights up the idle icon itself, so the icon never changes shape when the work starts.
        // The light runs along the filled icon, whose bolder line stands out from the regular one under it.
        _pulse = CreatePulseIcon();
        _pulseTrace = CreatePulseIcon();
        _pulseTrace.IconVariant = IconVariant.Filled;
        _pulseTrace.Opacity = 0;
        _pulseTrace.Bind(
            FluentIcon.ForegroundProperty,
            this.GetObservable(AccentLineProperty).CombineLatest(this.GetObservable(AccentProperty), (line, fill) => line ?? fill));

        LogicalChildren.Add(_pulse);
        VisualChildren.Add(_pulse);
        LogicalChildren.Add(_pulseTrace);
        VisualChildren.Add(_pulseTrace);
    }

    public TitleBarActivities Activities
    {
        get => GetValue(ActivitiesProperty);
        set => SetValue(ActivitiesProperty, value);
    }

    // From 0 to 1 while the update downloads something of known size, and null otherwise.
    public double? UpdateProgress
    {
        get => GetValue(UpdateProgressProperty);
        set => SetValue(UpdateProgressProperty, value);
    }

    public bool IsUpdateInstalling
    {
        get => GetValue(IsUpdateInstallingProperty);
        set => SetValue(IsUpdateInstallingProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    // The accent for filled shapes: the dots and the update's ring.
    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    // The accent for the Pulse icon's thin line. The fill accent can be too dark to read as a line on the
    // dark title bar, so this takes the lighter shade made for accent text. Falls back to Accent.
    public IBrush? AccentLine
    {
        get => GetValue(AccentLineProperty);
        set => SetValue(AccentLineProperty, value);
    }

    // What the indicator shows now: one task, or None for the idle icon.
    internal TitleBarActivities Shown => _shown;

    internal bool NeedsFrames => Activities != TitleBarActivities.None || _fading != null;

    // Test seam: frames read this clock, in seconds, instead of the compositor's, so a capture shows a chosen moment.
    internal double? FixedTime
    {
        get => _fixedTime;
        set
        {
            _fixedTime = value;
            RequestFrame();
        }
    }

    // Moves the animations to the given time, in seconds since the first frame.
    internal void Advance(double time)
    {
        _time = time;

        // An update that ends keeps the look it had, the gear or the full ring, while it fades out.
        if (Activities.HasFlag(TitleBarActivities.Update))
        {
            _updateProgress = UpdateProgress;
            _isUpdateInstalling = IsUpdateInstalling;
        }

        TitleBarActivities next = ChooseShown(time);
        if (next != _shown)
        {
            _fading = _shown;
            _shown = next;
            _shownSince = time;
        }
        else if (_fading != null && time - _shownSince >= FadeSeconds)
        {
            _fading = null;
        }

        UpdatePulse();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActivitiesProperty
            || change.Property == UpdateProgressProperty
            || change.Property == IsUpdateInstallingProperty)
        {
            // The hand-over happens on the next frame, which knows the time.
            RequestFrame();
        }
        else if (change.Property == ForegroundProperty || change.Property == AccentProperty)
        {
            _ringPen = null;
            _trackPen = null;
            _glyphPen = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _pulse.Measure(availableSize);
        _pulseTrace.Measure(availableSize);
        return new Size(
            Math.Max(BoxSize, _pulse.DesiredSize.Width),
            Math.Max(BoxSize, _pulse.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        _pulse.Arrange(rect);
        _pulseTrace.Arrange(rect);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } foreground)
            return;

        IBrush accent = Accent ?? foreground;
        double fade = FadeProgress;
        var origin = Matrix.CreateTranslation(
            Math.Round((Bounds.Width - BoxSize) / 2),
            Math.Round((Bounds.Height - BoxSize) / 2));
        using (context.PushTransform(origin))
        {
            if (_fading is { } fading)
                DrawActivity(context, fading, 1 - fade, foreground, accent);

            DrawActivity(context, _shown, fade, foreground, accent);
        }
    }

    private static FluentIcon CreatePulseIcon()
    {
        return new FluentIcon
        {
            Icon = Icon.Pulse,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    private double FadeProgress => _fading is null ? 1 : Math.Clamp((_time - _shownSince) / FadeSeconds, 0, 1);

    private TitleBarActivities ChooseShown(double time)
    {
        TitleBarActivities running = Activities;
        if (running == TitleBarActivities.None)
            return TitleBarActivities.None;

        int current = Array.IndexOf(s_turnOrder, _shown);
        if (current < 0)
            return s_turnOrder.First(kind => running.HasFlag(kind));

        // A task that ends hands over at once, to the one after it, so the others keep their turns.
        if (running.HasFlag(_shown) && time - _shownSince < TurnSeconds)
            return _shown;

        for (int step = 1; step <= s_turnOrder.Length; step++)
        {
            TitleBarActivities kind = s_turnOrder[(current + step) % s_turnOrder.Length];
            if (running.HasFlag(kind))
                return kind;
        }

        return _shown;
    }

    private double Weight(TitleBarActivities kind)
    {
        double fade = FadeProgress;
        return (kind == _shown ? fade : 0) + (kind == _fading ? 1 - fade : 0);
    }

    private void UpdatePulse()
    {
        double idle = Weight(TitleBarActivities.None);
        double startup = Weight(TitleBarActivities.Startup);

        // Opacity alone hides them: hiding would change the indicator's size and lay out the title bar again.
        _pulse.Opacity = idle + (startup * PulseTrackOpacity);
        _pulseTrace.Opacity = startup;
        if (startup > 0)
            _pulseTrace.OpacityMask = CreateTraceMask(_time, _pulseTrace.Bounds.Width);
    }

    // A light that runs along the Pulse icon from left to right, fading out behind its head.
    private static ImmutableLinearGradientBrush CreateTraceMask(double time, double width)
    {
        double head = -TraceEdge + (Fract(time / TracePeriod) * (width + TraceTail + (2 * TraceEdge)));
        double tail = TraceTail / (TraceTail + TraceEdge);
        return new ImmutableLinearGradientBrush(
            [
                new ImmutableGradientStop(0, Color.FromArgb(0, 255, 255, 255)),
                new ImmutableGradientStop(tail * 0.45, Color.FromArgb(96, 255, 255, 255)),
                new ImmutableGradientStop(tail * 0.85, Colors.White),
                new ImmutableGradientStop(tail, Colors.White),
                new ImmutableGradientStop(1, Color.FromArgb(0, 255, 255, 255)),
            ],
            startPoint: new RelativePoint(head - TraceTail, 0, RelativeUnit.Absolute),
            endPoint: new RelativePoint(head + TraceEdge, 0, RelativeUnit.Absolute));
    }

    private void RequestFrame()
    {
        if (_isFrameRequested || _topLevel == null)
            return;

        _isFrameRequested = true;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        _isFrameRequested = false;
        if (_topLevel == null)
            return;

        _clockOrigin ??= time;
        Advance(_fixedTime ?? (time - _clockOrigin.Value).TotalSeconds);
        if (NeedsFrames)
            RequestFrame();
    }

    private void DrawActivity(DrawingContext context, TitleBarActivities kind, double opacity, IBrush foreground, IBrush accent)
    {
        if (opacity <= 0)
            return;

        using (context.PushOpacity(opacity))
        {
            switch (kind)
            {
                case TitleBarActivities.Output:
                    DrawFrameGrid(context, _time, foreground, accent);
                    break;
                case TitleBarActivities.AiJob:
                    DrawDotWave(context, _time, accent);
                    break;
                case TitleBarActivities.Update:
                    DrawUpdate(context, foreground, accent);
                    break;
            }
        }
    }

    // The dots fill one by one, then fade out and start over.
    private static void DrawFrameGrid(DrawingContext context, double time, IBrush foreground, IBrush accent)
    {
        const double fillSeconds = 2.25;

        double phase = Fract(time / 3) * 3;
        double filled = phase < fillSeconds ? phase / (fillSeconds / DotCount) : DotCount;
        double fade = phase > 2.55 ? 1 - ((phase - 2.55) / 0.45) : 1;
        for (int i = 0; i < DotCount; i++)
        {
            Point center = DotCenter(i % DotSide, i / DotSide);
            using (context.PushOpacity(TrackOpacity))
                context.DrawEllipse(foreground, null, center, DotRadius, DotRadius);

            double local = Math.Clamp(filled - i, 0, 1);
            if (local <= 0)
                continue;

            double radius = DotRadius * (0.5 + (0.5 * OutBack(local)));
            using (context.PushOpacity(Math.Clamp(local * 2, 0, 1) * fade))
                context.DrawEllipse(accent, null, center, radius, radius);
        }
    }

    // A band of light crosses the dots from the top left corner to the bottom right one.
    private static void DrawDotWave(DrawingContext context, double time, IBrush accent)
    {
        const double period = 1.6;
        const double lag = 0.1;

        for (int row = 0; row < DotSide; row++)
        {
            for (int column = 0; column < DotSide; column++)
            {
                double light = 0.5 + (0.5 * Math.Cos(2 * Math.PI * ((time / period) - ((row + column) * lag))));
                using (context.PushOpacity(0.2 + (0.8 * Math.Pow(light, 1.4))))
                    context.DrawEllipse(accent, null, DotCenter(column, row), DotRadius, DotRadius);
            }
        }
    }

    private static Point DotCenter(int column, int row)
    {
        return new Point((DotPitch / 2) + (column * DotPitch), (DotPitch / 2) + (row * DotPitch));
    }

    // A ring around a download arrow: it fills with the download's progress where that is known and spins
    // where it is not, and the arrow turns into a spinning gear while the update installs.
    private void DrawUpdate(DrawingContext context, IBrush foreground, IBrush accent)
    {
        const double radius = 7.8;
        var center = new Point(9, 9);
        _ringPen ??= new Pen(accent, 1.6, null, PenLineCap.Round, PenLineJoin.Round);
        _trackPen ??= new Pen(foreground, 1.6);
        _glyphPen ??= new Pen(foreground, 1.35, null, PenLineCap.Round, PenLineJoin.Round);

        if (_isUpdateInstalling)
        {
            DrawSpinningArc(context, center, radius, _time, _ringPen);
            Matrix spin = Matrix.CreateRotation(_time * 2 * Math.PI / 3) * Matrix.CreateTranslation(center.X, center.Y);
            using (context.PushTransform(spin))
                context.DrawGeometry(foreground, null, s_gear);
            return;
        }

        if (_updateProgress is { } progress)
        {
            using (context.PushOpacity(TrackOpacity))
                context.DrawEllipse(null, _trackPen, center, radius, radius);

            DrawArc(context, center, radius, -Math.PI / 2, (-Math.PI / 2) + (2 * Math.PI * Math.Clamp(progress, 0, 1)), _ringPen);
        }
        else
        {
            DrawSpinningArc(context, center, radius, _time, _ringPen);
        }

        context.DrawGeometry(null, _glyphPen, s_downloadArrow);
    }

    // The progress ring's spin: the head runs ahead, then the tail catches up, while the whole arc turns.
    private static void DrawSpinningArc(DrawingContext context, Point center, double radius, double time, IPen pen)
    {
        const double period = 1.3;
        const double degrees = Math.PI / 180;

        double turns = Math.Floor(time / period);
        double phase = Fract(time / period);
        double offset = (turns * 248) + (time * 140) - 90;
        double start;
        double end;
        if (phase < 0.5)
        {
            start = 0;
            end = 12 + (248 * InOutCubic(phase * 2));
        }
        else
        {
            start = 248 * InOutCubic((phase - 0.5) * 2);
            end = 260;
        }

        DrawArc(context, center, radius, (offset + start) * degrees, (offset + end) * degrees, pen);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double from, double to, IPen pen)
    {
        double sweep = to - from;
        if (sweep <= 0.001)
            return;

        if (sweep >= (2 * Math.PI) - 0.001)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(PointOnCircle(center, radius, from), false);
            ctx.ArcTo(PointOnCircle(center, radius, to), new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise, true);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        return new Point(center.X + (Math.Cos(angle) * radius), center.Y + (Math.Sin(angle) * radius));
    }

    private static Geometry CreateDownloadArrow()
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(9, 4.8), false);
            ctx.LineTo(new Point(9, 11.2));
            ctx.EndFigure(false);

            ctx.BeginFigure(new Point(6.4, 8.7), false);
            ctx.LineTo(new Point(9, 11.3));
            ctx.LineTo(new Point(11.6, 8.7));
            ctx.EndFigure(false);

            ctx.BeginFigure(new Point(5.6, 13.4), false);
            ctx.LineTo(new Point(12.4, 13.4));
            ctx.EndFigure(false);
        }

        return geometry;
    }

    // A six-toothed gear centered on the origin, with a hole in the middle.
    private static Geometry CreateGear()
    {
        const int teeth = 6;
        const double outer = 4.1;
        const double inner = 3.0;
        const double hole = 1.25;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.EvenOdd);
            bool first = true;
            for (int tooth = 0; tooth < teeth; tooth++)
            {
                double angle = tooth * 2 * Math.PI / teeth;
                foreach ((double radius, double offset) in new[] { (inner, -0.34), (outer, -0.2), (outer, 0.2), (inner, 0.34) })
                {
                    Point point = PointOnCircle(default, radius, angle + offset);
                    if (first)
                    {
                        ctx.BeginFigure(point, true);
                        first = false;
                    }
                    else
                    {
                        ctx.LineTo(point);
                    }
                }
            }

            ctx.EndFigure(true);

            ctx.BeginFigure(new Point(hole, 0), true);
            ctx.ArcTo(new Point(-hole, 0), new Size(hole, hole), 0, false, SweepDirection.Clockwise, true);
            ctx.ArcTo(new Point(hole, 0), new Size(hole, hole), 0, false, SweepDirection.Clockwise, true);
            ctx.EndFigure(true);
        }

        return geometry;
    }

    private static double Fract(double value) => value - Math.Floor(value);

    private static double InOutCubic(double x) => x < 0.5 ? 4 * x * x * x : 1 - (Math.Pow((-2 * x) + 2, 3) / 2);

    private static double OutBack(double x)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + (c3 * Math.Pow(x - 1, 3)) + (c1 * Math.Pow(x - 1, 2));
    }
}
