using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed class AgentContextRing : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<AgentContextRing, double>(nameof(Progress));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<AgentContextRing, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<AgentContextRing, IBrush?>(nameof(IndicatorBrush));

    static AgentContextRing() => AffectsRender<AgentContextRing>(ProgressProperty, TrackBrushProperty, IndicatorBrushProperty);

    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? IndicatorBrush { get => GetValue(IndicatorBrushProperty); set => SetValue(IndicatorBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        const double thickness = 1.65;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double radius = Math.Max(0, Math.Min(center.X, center.Y) - thickness);
        if (TrackBrush is { } track)
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        if (IndicatorBrush is not { } indicator || Progress <= 0 || radius == 0)
            return;

        var pen = new Pen(indicator, thickness, lineCap: PenLineCap.Round);
        if (Progress >= 1)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        double angle = Progress * Math.Tau;
        var arc = new StreamGeometry();
        using (var geometry = arc.Open())
        {
            geometry.BeginFigure(new Point(center.X, center.Y - radius), isFilled: false);
            geometry.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                new Size(radius, radius), 0, Progress > .5, SweepDirection.Clockwise);
            geometry.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, pen, arc);
    }
}
