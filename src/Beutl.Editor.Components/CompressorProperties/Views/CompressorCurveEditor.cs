using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Beutl.Editor.Components.CompressorProperties.ViewModels;
using Beutl.Editor.Components.Helpers;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.Editor.Components.CompressorProperties.Views;

// The compressor's static transfer curve: input dB on x, output dB on y. The handle sets the
// threshold; the curve above the threshold sets the ratio. The values come from the number editors,
// so the curve and the rows always show the same thing, animations included.
public sealed partial class CompressorCurveEditor : AudioCanvas
{
    private const double PlotInset = 12.0;
    private const double HandleRadius = 6.0;
    private const double HandleHitRadius = 12.0;
    private const double CurveHitRadius = 8.0;
    private const int CurveSamples = 121;
    // The plot spans the same decibels as the threshold's range.
    private const float MinPlotDb = MinThresholdDb;
    private const float MaxPlotDb = MaxThresholdDb;

    private enum DragKind
    {
        None,
        Threshold,
        Ratio,
    }

    private DragKind _dragging;
    private float _ratioInputDb;
    private float _ratioStartOutputDb;
    private float _ratioStartPointerDb;
    private Pen? _curvePen;

    static CompressorCurveEditor()
    {
        AffectsRender<CompressorCurveEditor>(
            ThresholdProperty, RatioProperty, KneeProperty, MakeupGainProperty);
    }

    [GeneratedStyledProperty(DefaultValue = DefaultThresholdDb)]
    public partial float Threshold { get; set; }

    [GeneratedStyledProperty(DefaultValue = DefaultRatio)]
    public partial float Ratio { get; set; }

    [GeneratedStyledProperty(DefaultValue = DefaultKneeDb)]
    public partial float Knee { get; set; }

    [GeneratedStyledProperty(DefaultValue = DefaultMakeupGainDb)]
    public partial float MakeupGain { get; set; }

    [GeneratedStyledProperty]
    public partial CompressorPropertiesViewModel? Editor { get; set; }

    // The square the curve is drawn in, centered when the control is wider than it is tall.
    internal Rect PlotBounds
    {
        get
        {
            double side = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) - PlotInset * 2);
            return new Rect((Bounds.Width - side) / 2, (Bounds.Height - side) / 2, side, side);
        }
    }

    internal Point ThresholdHandleCenter => new(XFromDb(EffectiveThreshold), YFromDb(OutputDb(EffectiveThreshold)));

    // A point on the curve at inputDb, and one for any input and output, in this control's coordinates.
    internal Point PointOf(float inputDb) => new(XFromDb(inputDb), YFromDb(OutputDb(inputDb)));

    internal Point PointOf(float inputDb, float outputDb) => new(XFromDb(inputDb), YFromDb(outputDb));

    // The shared gain computer, with the values the rows show clamped the way the node clamps them.
    internal float OutputDb(float inputDb)
        => ComputeOutputDb(inputDb, EffectiveThreshold, EffectiveRatio, EffectiveKnee, EffectiveMakeupGain);

    private float EffectiveThreshold => Sanitize(Threshold, DefaultThresholdDb, MinThresholdDb, MaxThresholdDb);

    private float EffectiveRatio => Sanitize(Ratio, DefaultRatio, MinRatio, MaxRatio);

    private float EffectiveKnee => Sanitize(Knee, DefaultKneeDb, MinKneeDb, MaxKneeDb);

    private float EffectiveMakeupGain => Sanitize(MakeupGain, DefaultMakeupGainDb, MinMakeupGainDb, MaxMakeupGainDb);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Editor is not { } editor) return;

        Point point = e.GetPosition(this);
        DragKind kind = HitTest(point);
        if (kind == DragKind.None) return;
        // A gesture that cannot take history (or sits under an expression) leaves the rows to edit.
        if (!editor.BeginDrag(this, kind == DragKind.Threshold)) return;

        _dragging = kind;
        if (kind == DragKind.Ratio)
        {
            _ratioInputDb = InputDbFromX(point.X);
            // The curve may be drawn clipped by makeup, so remember the real (unclipped) output without
            // makeup and the pointer's place; the drag applies only the pointer's delta to that output.
            _ratioStartOutputDb = GainReducedOutputDb(_ratioInputDb);
            _ratioStartPointerDb = OutputDbFromY(point.Y);
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging == DragKind.None || Editor is not { } editor) return;

        Point point = e.GetPosition(this);
        if (_dragging == DragKind.Threshold)
        {
            editor.SetThreshold(InputDbFromX(point.X));
        }
        else
        {
            float pointerDelta = OutputDbFromY(point.Y) - _ratioStartPointerDb;
            if (pointerDelta != 0f)
            {
                editor.SetRatio(RatioFromGainReducedOutputDb(
                    _ratioInputDb, _ratioStartOutputDb + pointerDelta));
            }
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging == DragKind.None) return;

        FinishDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        FinishDrag();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FinishDrag();
        base.OnDetachedFromVisualTree(e);
    }

    private void FinishDrag()
    {
        _dragging = DragKind.None;
        Editor?.EndDrag();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        DrawFrame(context);
        Rect area = PlotBounds;
        Pen grid = LinePen;

        using (context.PushOpacity(0.45))
        {
            context.DrawLine(grid, area.BottomLeft, area.TopRight);
        }

        for (int db = (int)MinPlotDb; db <= (int)MaxPlotDb; db += 12)
        {
            double x = XFromDb(db);
            double y = YFromDb(db);
            context.DrawLine(grid, new Point(x, area.Top), new Point(x, area.Bottom));
            context.DrawLine(grid, new Point(area.Left, y), new Point(area.Right, y));
        }

        context.DrawRectangle(null, grid, area);

        StreamGeometry curve = BuildCurveGeometry();
        context.DrawGeometry(null, GetPen(ref _curvePen, AccentBrush, 2, PenLineJoin.Round), curve);

        DrawHandle(context, ThresholdHandleCenter, HandleRadius, true);
    }

    private StreamGeometry BuildCurveGeometry()
    {
        var curve = new StreamGeometry();
        using (StreamGeometryContext context = curve.Open())
        {
            for (int i = 0; i < CurveSamples; i++)
            {
                float inputDb = MinPlotDb + (MaxPlotDb - MinPlotDb) * i / (CurveSamples - 1);
                var point = new Point(XFromDb(inputDb), YFromDb(OutputDb(inputDb)));
                if (i == 0)
                {
                    context.BeginFigure(point, false);
                }
                else
                {
                    context.LineTo(point);
                }
            }

            context.EndFigure(false);
        }

        return curve;
    }

    private DragKind HitTest(Point point)
    {
        if (!PlotBounds.Inflate(HandleHitRadius).Contains(point)) return DragKind.None;

        Point handle = ThresholdHandleCenter;
        double dx = point.X - handle.X;
        double dy = point.Y - handle.Y;
        if (dx * dx + dy * dy <= HandleHitRadius * HandleHitRadius) return DragKind.Threshold;

        float inputDb = InputDbFromX(point.X);
        // The soft knee compresses below the threshold too; anywhere the shared curve reduces the gain
        // is a place the ratio can be dragged.
        if (ComputeGainReductionDb(
                inputDb, EffectiveThreshold, EffectiveRatio, EffectiveKnee) <= 0f)
            return DragKind.None;

        double curveY = YFromDb(OutputDb(inputDb));
        return Math.Abs(point.Y - curveY) <= CurveHitRadius ? DragKind.Ratio : DragKind.None;
    }

    // The curve's output without makeup: what the gain computer alone produces. The ratio drag works
    // in this space so makeup cannot clip the value the pointer is compared against.
    private float GainReducedOutputDb(float inputDb)
        => inputDb - ComputeGainReductionDb(inputDb, EffectiveThreshold, EffectiveRatio, EffectiveKnee);

    // Bisects the shared gain computer so the dragged point lands where the pointer is. More ratio
    // lowers the gain-reduced output for an input above the threshold, also through the knee.
    private float RatioFromGainReducedOutputDb(float inputDb, float targetOutputDb)
    {
        float low = MinRatio;
        float high = MaxRatio;
        for (int i = 0; i < 24; i++)
        {
            float middle = (low + high) * 0.5f;
            float output = inputDb - ComputeGainReductionDb(
                inputDb, EffectiveThreshold, middle, EffectiveKnee);
            if (output > targetOutputDb)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return Math.Clamp((low + high) * 0.5f, MinRatio, MaxRatio);
    }

    private double XFromDb(double db)
    {
        Rect area = PlotBounds;
        double t = (Math.Clamp(db, MinPlotDb, MaxPlotDb) - MinPlotDb) / (MaxPlotDb - MinPlotDb);
        return area.Left + t * area.Width;
    }

    private float InputDbFromX(double x)
    {
        Rect area = PlotBounds;
        double t = Math.Clamp((x - area.Left) / area.Width, 0, 1);
        return (float)(MinPlotDb + t * (MaxPlotDb - MinPlotDb));
    }

    private double YFromDb(double db)
    {
        Rect area = PlotBounds;
        double t = (Math.Clamp(db, MinPlotDb, MaxPlotDb) - MinPlotDb) / (MaxPlotDb - MinPlotDb);
        return area.Bottom - t * area.Height;
    }

    private float OutputDbFromY(double y)
    {
        Rect area = PlotBounds;
        double t = Math.Clamp((area.Bottom - y) / area.Height, 0, 1);
        return (float)(MinPlotDb + t * (MaxPlotDb - MinPlotDb));
    }

    private static float Sanitize(float value, float fallback, float min, float max)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
