using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public sealed class GraphEditorScale : Control
{
    public static readonly DirectProperty<GraphEditorScale, double> ScaleProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorScale, double>(
            nameof(Scale),
            o => o.Scale, (o, v) => o.Scale = v,
            1d);

    public static readonly DirectProperty<GraphEditorScale, double> BaselineProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorScale, double>(
            nameof(Baseline),
            o => o.Baseline, (o, v) => o.Baseline = v);

    public static readonly DirectProperty<GraphEditorScale, Vector> OffsetProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorScale, Vector>(
            nameof(Offset), o => o.Offset, (o, v) => o.Offset = v);

    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private readonly Pen _pen;
    private IBrush _brush = Brushes.White;
    private double _scale = 1;
    private double _baseline;
    private Vector _offset;
    private IDisposable? _disposable;

    static GraphEditorScale()
    {
        AffectsRender<GraphEditorScale>(ScaleProperty, BaselineProperty, OffsetProperty);
    }

    public GraphEditorScale()
    {
        ClipToBounds = false;
        _pen = new Pen()
        {
            Brush = _brush
        };
    }

    public double Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public double Baseline
    {
        get => _baseline;
        set => SetAndRaise(BaselineProperty, ref _baseline, value);
    }

    public Vector Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _disposable = this.GetResourceObservable("GraphEditorScaleTextBrush").Subscribe(b =>
        {
            if (b is IBrush brush)
            {
                _brush = brush;
                _pen.Brush = brush;
                InvalidateVisual();
            }
        });
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _disposable?.Dispose();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double step = GraphEditorGridMetrics.Step(_scale);
        double top = (_baseline - Offset.Y) / _scale;
        double bottom = (_baseline - Offset.Y - Bounds.Height) / _scale;
        // Include the major tick below the viewport so its minor ticks fill the bottom interval.
        double first = Math.Floor(bottom / step) * step;
        using (context.PushClip(new Rect(Bounds.Size)))
            for (double value = first; value <= top; value += step)
            {
                double y = _baseline - value * _scale - Offset.Y;
                if (y >= 0 && y <= Bounds.Height)
                {
                    context.DrawLine(_pen, new(Bounds.Width - 5, y), new(Bounds.Width, y));
                    string label = Math.Abs(value) < step * 0.0001 ? "0" : value.ToString("G4", CultureInfo.CurrentCulture);
                    using var text = new TextLayout(label, s_typeface, 13, _brush);
                    text.Draw(context, new(Math.Max(2, Bounds.Width - text.Width - 9), y - text.Height / 2));
                }
                for (int minor = 1; minor < 4; minor++)
                {
                    double yy = y - step * _scale * minor / 4;
                    if (yy >= 0 && yy <= Bounds.Height)
                        context.DrawLine(_pen, new(Bounds.Width - 3, yy), new(Bounds.Width, yy));
                }
            }
    }
}
