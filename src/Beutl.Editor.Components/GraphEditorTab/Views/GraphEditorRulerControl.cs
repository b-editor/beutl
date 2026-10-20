using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

// The vertical value ticks of the graph editor, drawn with a theme brush: the scale labels them and the
// background draws them as grid lines.
public abstract class GraphEditorRulerControl : Control
{
    public static readonly DirectProperty<GraphEditorRulerControl, double> ScaleProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorRulerControl, double>(
            nameof(Scale),
            o => o.Scale, (o, v) => o.Scale = v,
            1d);

    public static readonly DirectProperty<GraphEditorRulerControl, double> BaselineProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorRulerControl, double>(
            nameof(Baseline),
            o => o.Baseline, (o, v) => o.Baseline = v);

    public static readonly DirectProperty<GraphEditorRulerControl, Vector> OffsetProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorRulerControl, Vector>(
            nameof(Offset), o => o.Offset, (o, v) => o.Offset = v);

    private readonly string _brushResourceKey;
    private double _scale = 1;
    private double _baseline;
    private Vector _offset;
    private IDisposable? _disposable;

    static GraphEditorRulerControl()
    {
        AffectsRender<GraphEditorRulerControl>(ScaleProperty, BaselineProperty, OffsetProperty);
    }

    protected GraphEditorRulerControl(string brushResourceKey)
    {
        _brushResourceKey = brushResourceKey;
        Pen = new Pen()
        {
            Brush = Brush
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

    protected IBrush Brush { get; private set; } = Brushes.White;

    protected Pen Pen { get; }

    // The major ticks from the baseline between top and bottom, in pixels from the top of the graph.
    protected IEnumerable<(double Value, double Y)> MajorTicks(double top, double bottom)
        => GraphEditorGridMetrics.MajorTicks(_scale, _baseline, top, bottom);

    // The three minor ticks above a major tick at y.
    protected IEnumerable<double> MinorTicks(double y)
    {
        double step = GraphEditorGridMetrics.Step(_scale);
        for (int minor = 1; minor < 4; minor++)
            yield return y - step * _scale * minor / 4;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _disposable = this.GetResourceObservable(_brushResourceKey).Subscribe(b =>
        {
            if (b is IBrush brush)
            {
                Brush = brush;
                Pen.Brush = brush;
                InvalidateVisual();
            }
        });
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _disposable?.Dispose();
    }
}
