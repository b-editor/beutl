using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Views;

public sealed class GraphEditorBackground : Control
{
    public static readonly DirectProperty<GraphEditorBackground, double> ScaleProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorBackground, double>(
            nameof(Scale),
            o => o.Scale, (o, v) => o.Scale = v,
            1d);

    public static readonly DirectProperty<GraphEditorBackground, double> BaselineProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorBackground, double>(
            nameof(Baseline),
            o => o.Baseline, (o, v) => o.Baseline = v);

    public static readonly DirectProperty<GraphEditorBackground, Vector> OffsetProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorBackground, Vector>(
            nameof(Offset), o => o.Offset, (o, v) => o.Offset = v);

    public static readonly DirectProperty<GraphEditorBackground, Size> ViewportProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorBackground, Size>(
            nameof(Viewport), o => o.Viewport, (o, v) => o.Viewport = v);

    private readonly Pen _pen;
    private IBrush _brush = Brushes.White;
    private double _scale = 1;
    private double _baseline;
    private Vector _offset;
    private Size _viewport;
    private IDisposable? _disposable;

    static GraphEditorBackground()
    {
        AffectsRender<GraphEditorBackground>(ScaleProperty, BaselineProperty, OffsetProperty, ViewportProperty);
    }

    public GraphEditorBackground()
    {
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

    public Size Viewport
    {
        get => _viewport;
        set => SetAndRaise(ViewportProperty, ref _viewport, value);
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();
        _disposable = this.GetResourceObservable("TextFillColorTertiaryBrush").Subscribe(b =>
        {
            if (b is IBrush brush)
            {
                _brush = brush;
                _pen.Brush = brush;
                InvalidateVisual();
            }
        });
    }

    protected override void OnUnloaded()
    {
        base.OnUnloaded();
        _disposable?.Dispose();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var viewport = new Rect((Point)_offset, _viewport);

        double PixelsPer1 = 1 * Math.Clamp(_scale, 1, 1.75);
        double PixelsPer5 = PixelsPer1 * 5;
        double PixelsPer100 = PixelsPer5 * 20;

        double originY = _baseline;
        for (double y = originY - PixelsPer100; y >= viewport.Top; y -= PixelsPer100)
        {
            if (y < viewport.Bottom)
            {
                context.DrawLine(_pen, new(viewport.Left, y), new(viewport.Right, y));
            }
        }

        for (double y = originY; y < viewport.Bottom; y += PixelsPer100)
        {
            if (viewport.Top <= y)
            {
                context.DrawLine(_pen, new(viewport.Left, y), new(viewport.Right, y));
            }
        }

        double PixelsPerSecond = Helper.SecondWidth;
        double right = viewport.Right;
        double originX = Math.Floor(viewport.X / PixelsPerSecond) * PixelsPerSecond;
        for (double x = originX; x < right; x += PixelsPerSecond)
        {
            context.DrawLine(_pen, new(x, viewport.Top), new(x, viewport.Bottom));
        }
    }
}
