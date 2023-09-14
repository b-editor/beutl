using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

using Beutl.Utilities;

namespace Beutl.Views;

public sealed class EaseLine : Line
{
    public static readonly DirectProperty<EaseLine, double> StartXProperty
        = AvaloniaProperty.RegisterDirect<EaseLine, double>(nameof(StartX), o => o.StartX, (o, v) => o.StartX = v);

    public static readonly DirectProperty<EaseLine, double> StartYProperty
        = AvaloniaProperty.RegisterDirect<EaseLine, double>(nameof(StartY), o => o.StartY, (o, v) => o.StartY = v);

    public static readonly DirectProperty<EaseLine, double> EndXProperty
        = AvaloniaProperty.RegisterDirect<EaseLine, double>(nameof(EndX), o => o.EndX, (o, v) => o.EndX = v);

    public static readonly DirectProperty<EaseLine, double> EndYProperty
        = AvaloniaProperty.RegisterDirect<EaseLine, double>(nameof(EndY), o => o.EndY, (o, v) => o.EndY = v);

    public static readonly DirectProperty<EaseLine, double> BaselineProperty
        = AvaloniaProperty.RegisterDirect<EaseLine, double>(nameof(Baseline), o => o.Baseline, (o, v) => o.Baseline = v);

    public static readonly StyledProperty<Animation.Easings.Easing> EasingProperty
        = AvaloniaProperty.Register<EaseLine, Animation.Easings.Easing>(nameof(Easing));

    private static readonly ScaleTransform s_transform = new(1, -1);
    private double _baseline;

    static EaseLine()
    {
        StrokeLineCapProperty.OverrideDefaultValue<EaseLine>(PenLineCap.Round);
        StrokeJoinProperty.OverrideDefaultValue<EaseLine>(PenLineJoin.Round);
        AffectsGeometry<EaseLine>(EasingProperty, BaselineProperty);
    }

    public double StartX
    {
        get => StartPoint.X;
        set
        {
            double f = StartX;
            if (SetAndRaise(StartXProperty, ref f, value))
            {
                StartPoint = StartPoint.WithX(f);
            }
        }
    }

    public double StartY
    {
        get => StartPoint.Y;
        set
        {
            double f = StartY;
            if (SetAndRaise(StartYProperty, ref f, value))
            {
                StartPoint = StartPoint.WithY(f);
            }
        }
    }

    public double EndX
    {
        get => EndPoint.X;
        set
        {
            double f = EndX;
            if (SetAndRaise(EndXProperty, ref f, value))
            {
                EndPoint = EndPoint.WithX(f);
            }
        }
    }

    public double EndY
    {
        get => EndPoint.Y;
        set
        {
            double f = EndY;
            if (SetAndRaise(EndYProperty, ref f, value))
            {
                EndPoint = EndPoint.WithY(f);
            }
        }
    }

    public double Baseline
    {
        get => _baseline;
        set => SetAndRaise(BaselineProperty, ref _baseline, value);
    }

    public Animation.Easings.Easing Easing
    {
        get => GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EasingProperty)
        {
            if (change.OldValue is Animation.Easings.SplineEasing oldValue)
            {
                oldValue.Changed -= OnSplineEasingChanged;
            }

            if (change.NewValue is Animation.Easings.SplineEasing newValue)
            {
                newValue.Changed += OnSplineEasingChanged;
            }
        }
    }

    private void OnSplineEasingChanged(object? sender, EventArgs e)
    {
        InvalidateGeometry();
    }

    protected override Geometry CreateDefiningGeometry()
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            double startX = StartX;
            double startY = StartY;
            double endX = EndX;
            double endY = EndY;
            double baseline = Baseline;
            double baseY = -startY + baseline;
            context.BeginFigure(new Point(startX, -startY + baseline), false);
            Animation.Easings.Easing easing = Easing;
            double width = endX - startX;
            double height = endY - startY;

            if (easing is Animation.Easings.SplineEasing splineEasing)
            {
                context.CubicBezierTo(
                    new Point((splineEasing.X1 * width) + startX, -(splineEasing.Y1 * height) + baseY),
                    new Point((splineEasing.X2 * width) + startX, -(splineEasing.Y2 * height) + baseY),
                    new Point(endX, -endY + baseline));
            }
            else if (easing is Animation.Easings.LinearEasing)
            {
                float widthF = (float)width;
                context.LineTo(new Point(widthF + startX, -height + baseY));
            }
            else if (easing != null)
            {
                float widthF = (float)width;
                float increment = (float)(FrameNumberHelper.SecondWidth / 30);
                for (float x = 0F; MathUtilities.LessThanOrClose(x, widthF); x += increment)
                {
                    float progress = x / widthF;
                    context.LineTo(new Point(x + startX, -(easing.Ease(progress) * height) + baseY));
                }

                context.LineTo(new Point(widthF + startX, -(easing.Ease(1) * height) + baseY));
            }

            context.EndFigure(false);
        }

        return geometry;
    }
}
