using Avalonia;
using Avalonia.Media;

using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public sealed class GraphEditorBackground : GraphEditorRulerControl
{
    public static readonly StyledProperty<bool> IsSpeedGraphProperty
        = AvaloniaProperty.Register<GraphEditorBackground, bool>(nameof(IsSpeedGraph));

    public bool IsSpeedGraph
    {
        get => GetValue(IsSpeedGraphProperty);
        set => SetValue(IsSpeedGraphProperty, value);
    }

    public static readonly StyledProperty<double> MaximumProperty
        = AvaloniaProperty.Register<GraphEditorBackground, double>(nameof(Maximum), double.MaxValue);

    public static readonly StyledProperty<double> MinimumProperty
        = AvaloniaProperty.Register<GraphEditorBackground, double>(nameof(Minimum), double.MinValue);

    public static readonly DirectProperty<GraphEditorBackground, Size> ViewportProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorBackground, Size>(
            nameof(Viewport), o => o.Viewport, (o, v) => o.Viewport = v);

    private Size _viewport;

    static GraphEditorBackground()
    {
        AffectsRender<GraphEditorBackground>(IsSpeedGraphProperty, ViewportProperty);
    }

    public GraphEditorBackground()
        : base("GraphEditorGridLineBrush")
    {
    }

    public Size Viewport
    {
        get => _viewport;
        set => SetAndRaise(ViewportProperty, ref _viewport, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var viewport = new Rect((Point)Offset, _viewport);

        double originY = Baseline;
        if (!(Scale > 0) || !double.IsFinite(Scale) || !double.IsFinite(originY)) return;
        foreach (var (_, y) in MajorTicks(viewport.Top, viewport.Bottom))
        {
            if (y >= viewport.Top && y <= viewport.Bottom)
                context.DrawLine(Pen, new(viewport.Left, y), new(viewport.Right, y));
            using (context.PushOpacity(0.35))
                foreach (double yy in MinorTicks(y))
                {
                    if (yy >= viewport.Top && yy <= viewport.Bottom)
                        context.DrawLine(Pen, new(viewport.Left, yy), new(viewport.Right, yy));
                }
        }

        if (IsSpeedGraph) return;

        using (context.PushOpacity(0.1))
        {
            double maxPixel = Maximum * Scale;
            double maxY = originY - maxPixel;
            if (double.IsFinite(maxY) && maxY >= 0)
            {
                context.FillRectangle(
                    Brush,
                    new Rect(viewport.Left, 0, viewport.Width, maxY));
            }

            double minPixel = Minimum * Scale;
            double minY = originY - minPixel;
            if (double.IsFinite(minY))
            {
                context.FillRectangle(
                    Brush,
                    new Rect(new Point(viewport.Left, minY), new Point(viewport.Right, viewport.Bottom)));
            }
        }
    }
}
